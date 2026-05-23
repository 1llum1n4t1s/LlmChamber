namespace LlmChamber.Media;

/// <summary>動画機能の初期化オプション。</summary>
public sealed class MediaOptions
{
    /// <summary>
    /// FFmpeg バイナリへの明示的パス。
    /// nullの場合、自動DLでキャッシュディレクトリに展開する。
    /// </summary>
    public string? FFmpegBinaryPath { get; set; }

    /// <summary>FFmpeg/FFprobe の自動ダウンロードを有効にするかどうか。</summary>
    public bool AutoDownload { get; set; } = true;
}
