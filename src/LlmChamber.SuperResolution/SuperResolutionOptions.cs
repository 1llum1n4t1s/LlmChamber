namespace LlmChamber.SuperResolution;

/// <summary>使用する4倍超解像モデル。</summary>
public enum SuperResolutionModel
{
    /// <summary>Real-ESRGAN x4plus。</summary>
    RealEsrgan,
    /// <summary>Real-HAT-GAN x4。</summary>
    RealHatGan
}
/// <summary>処理段階。</summary>
public enum SuperResolutionStage
{
    /// <summary>モデル取得中。</summary>
    DownloadingModel,
    /// <summary>画像処理中。</summary>
    Upscaling
}
/// <summary>モデル取得またはタイル処理の進捗。</summary>
public sealed record SuperResolutionProgress(SuperResolutionStage Stage, double DownloadFraction, int CompletedTiles, int TotalTiles);
/// <summary>超解像処理の設定。</summary>
public sealed class SuperResolutionOptions
{
    /// <summary>検証済みモデルの保存先。</summary>
    public string CacheDirectory { get; set; } = string.Empty;
    /// <summary>使用するモデル。</summary>
    public SuperResolutionModel Model { get; set; } = SuperResolutionModel.RealEsrgan;
    /// <summary>入力画像のタイル辺長。</summary>
    public int TileSize { get; set; } = 128;
    /// <summary>タイル周辺の重複幅。</summary>
    public int TilePadding { get; set; } = 32;
    /// <summary>出力画像の最大画素数。</summary>
    public long MaximumOutputPixels { get; set; } = 64L * 1024 * 1024;
    /// <summary>利用可能なら DirectML を優先する。</summary>
    public bool PreferGpu { get; set; } = true;
}
/// <summary>直線アルファの RGBA32 出力。</summary>
public sealed record SuperResolutionImage(int Width, int Height, byte[] Rgba32);
