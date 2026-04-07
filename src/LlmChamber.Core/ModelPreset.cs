namespace LlmChamber;

/// <summary>
/// モデルプリセット定義。
/// ライブラリに同梱される推奨モデルの情報。
/// </summary>
public sealed record ModelPreset
{
    /// <summary>ライブラリ内のプリセットID（例: "gemma4-e2b"）。</summary>
    public required string Id { get; init; }

    /// <summary>Ollamaのモデルタグ（例: "gemma4:e2b"）。</summary>
    public required string OllamaTag { get; init; }

    /// <summary>表示名（例: "Gemma 4 E2B"）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>モデルファミリ（例: "Gemma 4"）。</summary>
    public required string Family { get; init; }

    /// <summary>概算ダウンロードサイズ（バイト）。</summary>
    public required long ApproximateDownloadSize { get; init; }

    /// <summary>推奨最小RAM（バイト）。</summary>
    public required long RecommendedMinRam { get; init; }

    /// <summary>デフォルト推論パラメータ。</summary>
    public InferenceOptions? DefaultInferenceOptions { get; init; }

    /// <summary>説明文。</summary>
    public string? Description { get; init; }

    /// <summary>概算ダウンロードサイズを人間が読みやすい形式で取得。</summary>
    public string FormattedDownloadSize => FormatBytes(ApproximateDownloadSize);

    /// <summary>推奨最小RAMを人間が読みやすい形式で取得。</summary>
    public string FormattedMinRam => FormatBytes(RecommendedMinRam);

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
            _ => $"{bytes / (double)(1L << 10):F1} KB",
        };
    }
}
