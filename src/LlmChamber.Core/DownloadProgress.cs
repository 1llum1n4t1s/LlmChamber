namespace LlmChamber;

/// <summary>ダウンロード進捗情報。</summary>
/// <param name="BytesDownloaded">ダウンロード済みバイト数。</param>
/// <param name="TotalBytes">全体のバイト数。不明の場合はnull。</param>
/// <param name="Percentage">進捗率（0〜100）。TotalBytesが不明の場合はnull。</param>
/// <param name="StatusMessage">現在のステータスメッセージ。</param>
public sealed record DownloadProgress(
    long BytesDownloaded,
    long? TotalBytes,
    double? Percentage,
    string StatusMessage)
{
    /// <summary>ダウンロード完了かどうか。</summary>
    public bool IsCompleted =>
        (TotalBytes.HasValue && BytesDownloaded >= TotalBytes.Value) ||
        (Percentage.HasValue && Percentage.Value >= 100.0);
}
