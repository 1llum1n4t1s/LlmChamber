namespace LlmChamber;

/// <summary>ローカルにインストール済みのモデル情報。</summary>
public sealed record ModelInfo
{
    /// <summary>モデル名（Ollamaタグ形式、例: "gemma4:e2b"）。</summary>
    public required string Name { get; init; }

    /// <summary>モデルのサイズ（バイト）。</summary>
    public required long Size { get; init; }

    /// <summary>モデルの最終更新日時。</summary>
    public required DateTimeOffset ModifiedAt { get; init; }

    /// <summary>モデルのダイジェスト（ハッシュ）。</summary>
    public string? Digest { get; init; }

    /// <summary>モデルサイズを人間が読みやすい形式で取得。</summary>
    public string FormattedSize => Size switch
    {
        >= 1L << 30 => $"{Size / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{Size / (double)(1L << 20):F1} MB",
        _ => $"{Size / (double)(1L << 10):F1} KB",
    };
}
