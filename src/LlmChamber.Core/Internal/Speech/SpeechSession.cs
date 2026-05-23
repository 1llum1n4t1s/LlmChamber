using System.IO;
using LlmChamber.Speech;
using SuperLightLogger;
// MAUI の Microsoft.Maui.Media.SpeechOptions と名前衝突するためエイリアスで解決
using SpeechOptions = LlmChamber.Speech.SpeechOptions;

namespace LlmChamber.Internal.Speech;

/// <summary>ISpeechSession の実装。Whisper/Piper の協調を担当する。</summary>
internal sealed class SpeechSession : ISpeechSession
{
    private static readonly ILog _logger = LogManager.GetLogger<SpeechSession>();
    private readonly SpeechOptions _options;
    private readonly string _cacheDirectory;
    private readonly WhisperBinaryDownloader _whisperBinaryDownloader;
    private readonly WhisperModelDownloader _whisperModelDownloader;
    private readonly PiperBinaryDownloader _piperBinaryDownloader;
    private readonly PiperVoiceDownloader _piperVoiceDownloader;
    private readonly SemaphoreSlim _whisperInitLock = new(1, 1);
    private readonly SemaphoreSlim _piperInitLock = new(1, 1);
    private readonly Dictionary<string, string> _voiceCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>DisposeAsync 時に待機中の WaitAsync を OperationCanceledException で起こすための CTS。</summary>
    private readonly CancellationTokenSource _shutdownCts = new();
    /// <summary>volatile: lazy init double-check の outer 読み取りで ARM64 などのメモリモデル下でも stale を防ぐ。</summary>
    private volatile WhisperRunner? _whisperRunner;
    private volatile PiperRunner? _piperRunner;
    private int _disposed;

    public SpeechSession(
        SpeechOptions options,
        string cacheDirectory,
        WhisperBinaryDownloader whisperBinaryDownloader,
        WhisperModelDownloader whisperModelDownloader,
        PiperBinaryDownloader piperBinaryDownloader,
        PiperVoiceDownloader piperVoiceDownloader)
    {
        _options = options;
        _cacheDirectory = cacheDirectory;
        _whisperBinaryDownloader = whisperBinaryDownloader;
        _whisperModelDownloader = whisperModelDownloader;
        _piperBinaryDownloader = piperBinaryDownloader;
        _piperVoiceDownloader = piperVoiceDownloader;
    }

    public event EventHandler<DownloadProgress>? ResourceDownloadProgress;

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        TranscribeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        // 入力ストリームを一時 WAV に書き出してから Whisper を呼ぶ
        string tmpWav = Path.Combine(Path.GetTempPath(), $"whisper-in-{Guid.NewGuid():N}.wav");
        try
        {
            await using (var fileStream = new FileStream(tmpWav, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await audioStream.CopyToAsync(fileStream, cancellationToken);
            }
            return await TranscribeFileAsync(tmpWav, options, cancellationToken);
        }
        finally
        {
            if (File.Exists(tmpWav))
            {
                try { File.Delete(tmpWav); } catch { /* ignore */ }
            }
        }
    }

    public async Task<TranscriptionResult> TranscribeFileAsync(
        string audioFilePath,
        TranscribeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var runner = await EnsureWhisperAsync(cancellationToken);
        return await runner.TranscribeAsync(audioFilePath, options, cancellationToken);
    }

    public async Task<byte[]> SpeakAsync(
        string text,
        SpeakOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var (runner, voicePath) = await EnsurePiperAsync(options?.Voice, cancellationToken);
        return await runner.SpeakAsync(text, voicePath, options, cancellationToken);
    }

    public async Task SpeakToFileAsync(
        string text,
        string outputFilePath,
        SpeakOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var (runner, voicePath) = await EnsurePiperAsync(options?.Voice, cancellationToken);
        await runner.SpeakToFileAsync(text, voicePath, outputFilePath, options, cancellationToken);
    }

    /// <summary>Whisper を初期化（バイナリ + モデルの確保）。1回だけ実行。</summary>
    private async Task<WhisperRunner> EnsureWhisperAsync(CancellationToken cancellationToken)
    {
        if (_whisperRunner is not null) return _whisperRunner;

        // DisposeAsync で _shutdownCts.Cancel() が呼ばれると待機中の WaitAsync が
        // OperationCanceledException で起きる（ObjectDisposedException ではなく）
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        await _whisperInitLock.WaitAsync(linkedCts.Token);
        try
        {
            if (_whisperRunner is not null) return _whisperRunner;

            var progress = new Progress<DownloadProgress>(p => ResourceDownloadProgress?.Invoke(this, p));
            string speechDir = Path.Combine(_cacheDirectory, "speech");
            string modelsDir = Path.Combine(speechDir, "models");

            string binaryPath = _options.WhisperBinaryPath
                ?? await _whisperBinaryDownloader.EnsureBinaryAsync(speechDir, progress, cancellationToken);

            string modelPath = _options.WhisperModelPath
                ?? await _whisperModelDownloader.EnsureModelAsync(modelsDir, _options.WhisperModel, progress, cancellationToken);

            if (!File.Exists(binaryPath))
                throw new SpeechBinaryNotFoundException("whisper-cli", $"Whisperバイナリが見つかりません: {binaryPath}");
            if (!File.Exists(modelPath))
                throw new SpeechModelNotFoundException(_options.WhisperModel.ToString(), $"Whisperモデルが見つかりません: {modelPath}");

            _whisperRunner = new WhisperRunner(binaryPath, modelPath);
            _logger.Info($"Whisper 初期化完了: {binaryPath}, model: {modelPath}");
            return _whisperRunner;
        }
        finally
        {
            _whisperInitLock.Release();
        }
    }

    /// <summary>Piper を初期化（バイナリ確保 + voice 確保）。バイナリは1回だけ、voiceは要求毎にキャッシュ。</summary>
    private async Task<(PiperRunner Runner, string VoicePath)> EnsurePiperAsync(string? requestedVoice, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        await _piperInitLock.WaitAsync(linkedCts.Token);
        try
        {
            var progress = new Progress<DownloadProgress>(p => ResourceDownloadProgress?.Invoke(this, p));
            string speechDir = Path.Combine(_cacheDirectory, "speech");
            string voicesDir = _options.PiperVoicesDirectory ?? Path.Combine(speechDir, "voices");

            if (_piperRunner is null)
            {
                string binaryPath = _options.PiperBinaryPath
                    ?? await _piperBinaryDownloader.EnsureBinaryAsync(speechDir, progress, cancellationToken);

                if (!File.Exists(binaryPath))
                    throw new SpeechBinaryNotFoundException("piper", $"Piperバイナリが見つかりません: {binaryPath}");

                _piperRunner = new PiperRunner(binaryPath);
                _logger.Info($"Piper 初期化完了: {binaryPath}");
            }

            string voiceName = requestedVoice ?? _options.DefaultVoice;
            if (!_voiceCache.TryGetValue(voiceName, out var voicePath))
            {
                voicePath = await _piperVoiceDownloader.EnsureVoiceAsync(voicesDir, voiceName, progress, cancellationToken);
                _voiceCache[voiceName] = voicePath;
            }

            return (_piperRunner, voicePath);
        }
        finally
        {
            _piperInitLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        // 待機中の WaitAsync を OperationCanceledException で起こしてから SemaphoreSlim を Dispose する
        try { _shutdownCts.Cancel(); } catch { /* ignore */ }
        _whisperInitLock.Dispose();
        _piperInitLock.Dispose();
        _shutdownCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
