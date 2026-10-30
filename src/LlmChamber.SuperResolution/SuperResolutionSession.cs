using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LlmChamber.SuperResolution;

/// <summary>モデルを遅延取得し、RGBA32 画像を4倍に拡大するセッション。</summary>
public sealed class SuperResolutionSession : IDisposable
{
    private readonly string _directory;
    private readonly ModelInfo _model;
    private readonly int _tileSize, _padding;
    private readonly long _maximumPixels;
    private readonly bool _preferGpu;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _session;
    private volatile bool _disposed;

    /// <summary>設定を固定してセッションを作成する。</summary>
    public SuperResolutionSession(SuperResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.CacheDirectory)) throw new ArgumentException("キャッシュディレクトリを指定してください。", nameof(options));
        if (options.TileSize <= 0 || options.TilePadding < 0 || (long)options.TileSize + 2L * options.TilePadding > 1024) throw new ArgumentOutOfRangeException(nameof(options), "タイルの寸法が範囲外です。");
        if (options.MaximumOutputPixels <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        _directory = Path.GetFullPath(options.CacheDirectory);
        _model = ModelInfo.Get(options.Model);
        _tileSize = options.TileSize;
        _padding = options.TilePadding;
        _maximumPixels = options.MaximumOutputPixels;
        _preferGpu = options.PreferGpu;
    }

    /// <summary>選択したモデルのダウンロードサイズ（バイト）。</summary>
    public long ModelDownloadSizeBytes => _model.Size;

    /// <summary>モデルのキャッシュを検証し、必要な場合は取得する。推論用セッションは初期化しない。</summary>
    public async Task EnsureModelAsync(IProgress<SuperResolutionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await ModelCache.GetAsync(_directory, _model, progress, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>初期化済みの場合、選択した実行プロバイダーを返す。</summary>
    public string? ExecutionProvider { get; private set; }

    /// <summary>画像を非同期に4倍拡大する。入力は直線アルファの RGBA32。</summary>
    public async Task<SuperResolutionImage> UpscaleAsync(ReadOnlyMemory<byte> rgba32, int width, int height, IProgress<SuperResolutionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        long inputPixels = (long)width * height;
        if (inputPixels > int.MaxValue / 4 || width > int.MaxValue / 4 || height > int.MaxValue / 4) throw new ArgumentException("画像サイズが範囲外です。", nameof(rgba32));
        long inputBytes = inputPixels * 4;
        long outputPixels = inputPixels * 16;
        if (inputBytes != rgba32.Length || outputPixels > _maximumPixels || outputPixels > int.MaxValue / 4 || width > int.MaxValue / 4 || height > int.MaxValue / 4)
            throw new ArgumentException("画像サイズまたはデータ長が範囲外です。", nameof(rgba32));
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null)
            {
                string path = await ModelCache.GetAsync(_directory, _model, progress, cancellationToken).ConfigureAwait(false);
                await Task.Run(() => Initialize(path, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            return await Task.Run(() => Process(rgba32, width, height, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private void Initialize(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_preferGpu)
        {
            try
            {
                using var options = new SessionOptions { EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
                options.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(path, options);
                ExecutionProvider = "DirectML";
                return;
            }
            catch (OnnxRuntimeException) { }
        }
        using var cpuOptions = new SessionOptions();
        _session = new InferenceSession(path, cpuOptions);
        ExecutionProvider = "CPU";
    }

    private SuperResolutionImage Process(ReadOnlyMemory<byte> rgba, int width, int height, IProgress<SuperResolutionProgress>? progress, CancellationToken token)
    {
        var session = _session!;
        if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1) throw new InvalidDataException("モデルの入出力が不正です。");
        string inputName = session.InputMetadata.Keys.Single();
        string outputName = session.OutputMetadata.Keys.Single();
        byte[] output = new byte[checked(width * height * 64)];
        int outputWidth = width * 4;
        int columns = (width - 1) / _tileSize + 1;
        int rows = (height - 1) / _tileSize + 1;
        int total = checked(columns * rows), completed = 0;
        using var runOptions = new RunOptions();
        using var registration = token.Register(() => runOptions.Terminate = true);
        for (int ty = 0; ty < height; ty += _tileSize)
        for (int tx = 0; tx < width; tx += _tileSize)
        {
            token.ThrowIfCancellationRequested();
            int tileWidth = Math.Min(_tileSize, width - tx), tileHeight = Math.Min(_tileSize, height - ty);
            int modelWidth = Align16(tileWidth + _padding * 2), modelHeight = Align16(tileHeight + _padding * 2);
            var tensor = new DenseTensor<float>(new[] { 1, 3, modelHeight, modelWidth });
            var pixels = rgba.Span;
            for (int y = 0; y < modelHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < modelWidth; x++)
                {
                    int sx = Reflect(tx + x - _padding, width), sy = Reflect(ty + y - _padding, height);
                    int source = (sy * width + sx) * 4;
                    for (int c = 0; c < 3; c++) tensor[0, c, y, x] = pixels[source + c] / 255f;
                }
            }
            try
            {
                using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) }, new[] { outputName }, runOptions);
                var result = results.Single().AsTensor<float>();
                if (result.Dimensions.Length != 4 || result.Dimensions[0] != 1 || result.Dimensions[1] != 3 || result.Dimensions[2] != modelHeight * 4 || result.Dimensions[3] != modelWidth * 4) throw new InvalidDataException("モデル出力の寸法が不正です。");
                for (int y = 0; y < tileHeight * 4; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < tileWidth * 4; x++)
                    {
                        int ox = tx * 4 + x, oy = ty * 4 + y;
                        int target = (oy * outputWidth + ox) * 4;
                        for (int c = 0; c < 3; c++) output[target + c] = (byte)Math.Clamp((int)MathF.Round(result[0, c, y + _padding * 4, x + _padding * 4] * 255f), 0, 255);
                        output[target + 3] = Alpha(pixels, width, height, ox, oy);
                    }
                }
            }
            catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            progress?.Report(new(SuperResolutionStage.Upscaling, 1, ++completed, total));
        }
        token.ThrowIfCancellationRequested();
        return new(outputWidth, height * 4, output);
    }

    private static int Align16(int value) => (value + 15) / 16 * 16;
    internal static int Reflect(int value, int length)
    {
        if (length == 1) return 0;
        int period = 2 * (length - 1);
        int position = ((value % period) + period) % period;
        return position < length ? position : period - position;
    }
    internal static byte Alpha(ReadOnlySpan<byte> rgba, int width, int height, int x, int y)
    {
        float sx = (x + 0.5f) / 4 - 0.5f, sy = (y + 0.5f) / 4 - 0.5f;
        int x0 = (int)MathF.Floor(sx), y0 = (int)MathF.Floor(sy);
        float fx = sx - x0, fy = sy - y0;
        int x1 = Math.Clamp(x0 + 1, 0, width - 1), y1 = Math.Clamp(y0 + 1, 0, height - 1);
        x0 = Math.Clamp(x0, 0, width - 1); y0 = Math.Clamp(y0, 0, height - 1);
        float top = rgba[(y0 * width + x0) * 4 + 3] * (1 - fx) + rgba[(y0 * width + x1) * 4 + 3] * fx;
        float bottom = rgba[(y1 * width + x0) * 4 + 3] * (1 - fx) + rgba[(y1 * width + x1) * 4 + 3] * fx;
        return (byte)Math.Clamp((int)MathF.Round(top * (1 - fy) + bottom * fy), 0, 255);
    }
    /// <summary>モデルと実行資源を解放する。</summary>
    public void Dispose()
    {
        _disposed = true;
        _gate.Wait();
        try { _session?.Dispose(); _session = null; }
        finally { _gate.Release(); }
    }
}
