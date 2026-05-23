using System.Net.Http;
using System.Runtime.CompilerServices;
using LlmChamber.Internal.Media;
using LlmChamber.Internal.Speech;
using Microsoft.Extensions.Options;
using SuperLightLogger;
// MAUI の Microsoft.Maui.Media.SpeechOptions と名前衝突するためエイリアスで解決
using SpeechOptions = LlmChamber.Speech.SpeechOptions;
using ISpeechSession = LlmChamber.Speech.ISpeechSession;
using SpeechSession = LlmChamber.Internal.Speech.SpeechSession;
using MediaOptions = LlmChamber.Media.MediaOptions;
using IVideoSession = LlmChamber.Media.IVideoSession;
using VideoSession = LlmChamber.Internal.Media.VideoSession;

namespace LlmChamber.Internal;

/// <summary>ILocalLlmの実装。全体のオーケストレーション。</summary>
internal sealed class LocalLlm : ILocalLlm
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly OllamaDownloader _downloader;
    private readonly OllamaProcessManager _processManager;
    private readonly OllamaApiClient _apiClient;
    private readonly LlmChamberOptions _options;
    private readonly HttpClient _downloadHttpClient;
    /// <summary>true なら DisposeAsync 時に _downloadHttpClient.Dispose() を呼ぶ。Factory 経由は所有、DI 経由は不所有。</summary>
    private readonly bool _ownsDownloadHttpClient;
    private readonly object _speechLock = new();
    /// <summary>volatile: lazy init double-check の outer 読み取りで ARM64 などのメモリモデル下でも stale を防ぐ。</summary>
    private volatile SpeechSession? _speechSession;
    private readonly object _mediaLock = new();
    private volatile VideoSession? _videoSession;
    private static readonly ILog _logger = LogManager.GetLogger<LocalLlm>();
    private volatile bool _initialized;
    /// <summary>Interlocked.Exchange でアトミック化（0=未破棄, 1=破棄済）。</summary>
    private int _disposed;

    public LocalLlm(
        IOptions<LlmChamberOptions> options,
        OllamaDownloader downloader,
        OllamaProcessManager processManager,
        OllamaApiClient apiClient,
        IRuntimeManager runtimeManager,
        HttpClient downloadHttpClient,
        bool ownsDownloadHttpClient = false)
    {
        _options = options.Value;
        _downloader = downloader;
        _processManager = processManager;
        _apiClient = apiClient;
        _downloadHttpClient = downloadHttpClient;
        _ownsDownloadHttpClient = ownsDownloadHttpClient;
        Runtime = runtimeManager;
    }

    public bool IsReady => _initialized && _processManager.IsRunning;
    public IRuntimeManager Runtime { get; }

    public event EventHandler<DownloadProgress>? RuntimeDownloadProgress;
    public event EventHandler<DownloadProgress>? ModelDownloadProgress;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_initialized && _processManager.IsRunning) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && _processManager.IsRunning) return;

            // 1. ランタイムを確保
            var runtimeProgress = new Progress<DownloadProgress>(p =>
                RuntimeDownloadProgress?.Invoke(this, p));

            string binaryPath = await Runtime.EnsureRuntimeAsync(runtimeProgress, cancellationToken);

            // 2. プロセス起動
            await _processManager.StartAsync(binaryPath, cancellationToken);
            _apiClient.SetBaseUrl(_processManager.BaseUrl);

            // 3. デフォルトモデルをpull
            if (_options.AutoPullModel)
            {
                var modelProgress = new Progress<DownloadProgress>(p =>
                    ModelDownloadProgress?.Invoke(this, p));

                string modelTag = OllamaModels.ResolveModelTag(_options.DefaultModel);
                await Runtime.EnsureModelAsync(modelTag, modelProgress, cancellationToken);
            }

            _initialized = true;
            _logger.Info($"LlmChamber初期化完了。モデル: {_options.DefaultModel}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
        => GenerateAsync(prompt, images: null, options, cancellationToken);

    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        IReadOnlyList<byte[]>? images,
        InferenceOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await EnsureInitializedAsync(cancellationToken);
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        var mergedOptions = MergeOptions(options, model);

        await foreach (string chunk in _apiClient.GenerateStreamAsync(
            model, prompt, mergedOptions, images, cancellationToken))
        {
            yield return chunk;
        }
    }

    public Task<string> GenerateCompleteAsync(
        string prompt,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
        => GenerateCompleteAsync(prompt, images: null, options, cancellationToken);

    public async Task<string> GenerateCompleteAsync(
        string prompt,
        IReadOnlyList<byte[]>? images,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await EnsureInitializedAsync(cancellationToken);
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        var mergedOptions = MergeOptions(options, model);

        return await _apiClient.GenerateCompleteAsync(model, prompt, mergedOptions, images, cancellationToken);
    }

    public IChatSession CreateChatSession(ChatOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        return new ChatSession(_apiClient, model, options, ensureInitialized: EnsureInitializedAsync);
    }

    public async Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await EnsureInitializedAsync(cancellationToken);
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        return await _apiClient.GetEmbeddingAsync(model, text, cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized || !_processManager.IsRunning)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private static InferenceOptions? MergeOptions(InferenceOptions? userOptions, string model)
    {
        if (userOptions is not null) return userOptions;

        // プリセットのデフォルトを使用
        var preset = OllamaModels.FindPreset(model);
        return preset?.DefaultInferenceOptions;
    }

    public ISpeechSession UseSpeech(SpeechOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_speechSession is { } existing) return existing;

        lock (_speechLock)
        {
            // ロック取得後に dispose race を再チェック（lock の外側のチェックを通った後に
            // 別スレッドが DisposeAsync を完了させる窓を塞ぐ）
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_speechSession is { } existing2) return existing2;

            var speechOptions = options ?? new SpeechOptions();
            var whisperBinDl = new WhisperBinaryDownloader(_downloadHttpClient);
            var whisperModelDl = new WhisperModelDownloader(_downloadHttpClient);
            var piperBinDl = new PiperBinaryDownloader(_downloadHttpClient);
            var piperVoiceDl = new PiperVoiceDownloader(_downloadHttpClient);

            _speechSession = new SpeechSession(
                speechOptions,
                _options.CacheDirectory,
                whisperBinDl,
                whisperModelDl,
                piperBinDl,
                piperVoiceDl);

            _logger.Debug("Speech セッションを作成しました（バイナリDLは初回利用時まで遅延されます）");
            return _speechSession;
        }
    }

    public ISpeechSession? Speech => _speechSession;

    public IVideoSession UseMedia(MediaOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_videoSession is { } existing) return existing;

        lock (_mediaLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_videoSession is { } existing2) return existing2;

            var mediaOptions = options ?? new MediaOptions();
            var ffmpegDownloader = new FFmpegBinaryDownloader(_downloadHttpClient);

            // フレーム解析用デリゲート: Vision API を呼び出す
            VideoSession.FrameAnalyzer analyzer = async (prompt, imageBytes, ct) =>
            {
                return await GenerateCompleteAsync(
                    prompt,
                    new[] { imageBytes },
                    options: null,
                    cancellationToken: ct);
            };

            _videoSession = new VideoSession(mediaOptions, _options.CacheDirectory, ffmpegDownloader, analyzer);
            _logger.Debug("Media セッションを作成しました（FFmpegDLは初回利用時まで遅延されます）");
            return _videoSession;
        }
    }

    public IVideoSession? Media => _videoSession;

    public async ValueTask DisposeAsync()
    {
        // Interlocked.Exchange でアトミックに dispose フラグを立てる。
        // 2 スレッドが同時に Dispose を呼んでも実際の破棄は 1 回だけ
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // lock を取って session 参照を snapshot し、後発の UseSpeech/UseMedia で
        // 作成された未管理セッションが残らないようにする
        SpeechSession? speechToDispose;
        lock (_speechLock)
        {
            speechToDispose = _speechSession;
            _speechSession = null;
        }
        if (speechToDispose is not null) await speechToDispose.DisposeAsync();

        VideoSession? videoToDispose;
        lock (_mediaLock)
        {
            videoToDispose = _videoSession;
            _videoSession = null;
        }
        if (videoToDispose is not null) await videoToDispose.DisposeAsync();

        await _processManager.DisposeAsync();
        if (_ownsDownloadHttpClient) _downloadHttpClient.Dispose();
        _initLock.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        SpeechSession? speechToDispose;
        lock (_speechLock)
        {
            speechToDispose = _speechSession;
            _speechSession = null;
        }
        // SpeechSession.DisposeAsync は ValueTask.CompletedTask を同期的に返すため
        // IsCompletedSuccessfully は true で、ブロッキングパスには入らない
        if (speechToDispose is not null)
        {
            var task = speechToDispose.DisposeAsync();
            if (!task.IsCompletedSuccessfully) task.AsTask().GetAwaiter().GetResult();
        }

        VideoSession? videoToDispose;
        lock (_mediaLock)
        {
            videoToDispose = _videoSession;
            _videoSession = null;
        }
        if (videoToDispose is not null)
        {
            var task = videoToDispose.DisposeAsync();
            if (!task.IsCompletedSuccessfully) task.AsTask().GetAwaiter().GetResult();
        }

        _processManager.Dispose();
        if (_ownsDownloadHttpClient) _downloadHttpClient.Dispose();
        _initLock.Dispose();
    }
}
