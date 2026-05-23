using System.IO;
using System.Runtime.CompilerServices;
using LlmChamber.Media;
using SuperLightLogger;

namespace LlmChamber.Internal.Media;

/// <summary>
/// IVideoSession の実装。FFmpegでフレーム抽出 → Vision API デリゲート で解析する。
/// </summary>
internal sealed class VideoSession : IVideoSession
{
    /// <summary>
    /// 1フレームに対する Vision 解析を実行するデリゲート。
    /// LocalLlm が ILocalLlm.GenerateCompleteAsync(prompt, images) をラップして注入する。
    /// </summary>
    public delegate Task<string> FrameAnalyzer(string prompt, byte[] imageBytes, CancellationToken cancellationToken);

    private static readonly ILog _logger = LogManager.GetLogger<VideoSession>();
    private readonly MediaOptions _options;
    private readonly string _cacheDirectory;
    private readonly FFmpegBinaryDownloader _ffmpegBinaryDownloader;
    private readonly FrameAnalyzer _frameAnalyzer;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    /// <summary>DisposeAsync 時に待機中の WaitAsync を OperationCanceledException で起こすための CTS。</summary>
    private readonly CancellationTokenSource _shutdownCts = new();
    /// <summary>volatile: lazy init double-check の outer 読み取りで ARM64 などのメモリモデル下でも stale を防ぐ。</summary>
    private volatile FFmpegFrameExtractor? _extractor;
    private int _disposed;

    public VideoSession(
        MediaOptions options,
        string cacheDirectory,
        FFmpegBinaryDownloader ffmpegBinaryDownloader,
        FrameAnalyzer frameAnalyzer)
    {
        _options = options;
        _cacheDirectory = cacheDirectory;
        _ffmpegBinaryDownloader = ffmpegBinaryDownloader;
        _frameAnalyzer = frameAnalyzer;
    }

    public event EventHandler<DownloadProgress>? ResourceDownloadProgress;

    public async IAsyncEnumerable<VideoFrameAnalysis> AnalyzeAsync(
        string videoFilePath,
        string? prompt = null,
        VideoAnalysisOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        options ??= new VideoAnalysisOptions();
        var extractor = await EnsureExtractorAsync(cancellationToken);

        await foreach (var frame in extractor.ExtractAsync(videoFilePath, options, cancellationToken))
        {
            if (!options.AnalyzeFrames)
            {
                yield return frame;
                continue;
            }

            string framePrompt = BuildFramePrompt(prompt, options.FramePromptTemplate, frame);
            string description = await _frameAnalyzer(framePrompt, frame.ImageBytes, cancellationToken);

            yield return frame with { Description = description };
        }
    }

    public async IAsyncEnumerable<VideoFrameAnalysis> ExtractFramesAsync(
        string videoFilePath,
        VideoAnalysisOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        options ??= new VideoAnalysisOptions { AnalyzeFrames = false };

        var extractor = await EnsureExtractorAsync(cancellationToken);
        await foreach (var frame in extractor.ExtractAsync(videoFilePath, options, cancellationToken))
        {
            yield return frame;
        }
    }

    private async Task<FFmpegFrameExtractor> EnsureExtractorAsync(CancellationToken cancellationToken)
    {
        if (_extractor is not null) return _extractor;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        await _initLock.WaitAsync(linkedCts.Token);
        try
        {
            if (_extractor is not null) return _extractor;

            string mediaDir = Path.Combine(_cacheDirectory, "media");
            var progress = new Progress<DownloadProgress>(p => ResourceDownloadProgress?.Invoke(this, p));

            string binaryPath = _options.FFmpegBinaryPath
                ?? await _ffmpegBinaryDownloader.EnsureBinaryAsync(mediaDir, progress, cancellationToken);

            if (!File.Exists(binaryPath))
                throw new FFmpegBinaryNotFoundException($"FFmpegバイナリが見つかりません: {binaryPath}");

            _extractor = new FFmpegFrameExtractor(binaryPath);
            _logger.Info($"FFmpeg 初期化完了: {binaryPath}");
            return _extractor;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string BuildFramePrompt(string? userPrompt, string template, VideoFrameAnalysis frame)
    {
        string framePart = template
            .Replace("{frame_index}", frame.FrameIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{timestamp}", frame.Timestamp.ToString(@"hh\:mm\:ss\.fff"));

        if (string.IsNullOrWhiteSpace(userPrompt)) return framePart;
        return userPrompt + "\n\n" + framePart;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        try { _shutdownCts.Cancel(); } catch { /* ignore */ }
        _initLock.Dispose();
        _shutdownCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
