namespace LlmChamber.Media;

/// <summary>1フレームの解析結果。</summary>
/// <param name="FrameIndex">先頭から数えたフレーム番号（0始まり）。</param>
/// <param name="Timestamp">動画内のタイムスタンプ。</param>
/// <param name="ImageBytes">抽出されたフレームのJPEG/PNGバイト列。</param>
/// <param name="Description">Vision モデルによる説明文。<see cref="VideoAnalysisOptions.AnalyzeFrames"/>=true の場合のみ。</param>
public sealed record VideoFrameAnalysis(
    int FrameIndex,
    TimeSpan Timestamp,
    byte[] ImageBytes,
    string? Description);

/// <summary>動画解析のオプション。</summary>
public sealed class VideoAnalysisOptions
{
    /// <summary>
    /// フレーム抽出間隔（秒）。デフォルトは1秒に1フレーム。
    /// 値が小さいほど解析対象が増え、コストも増える。
    /// </summary>
    public double FrameIntervalSeconds { get; set; } = 1.0;

    /// <summary>
    /// 抽出する最大フレーム数。0 または負の値は無制限。
    /// </summary>
    public int MaxFrames { get; set; } = 60;

    /// <summary>
    /// 各フレームに対して Vision モデルで解析を実行するか。
    /// false なら抽出のみ。
    /// </summary>
    public bool AnalyzeFrames { get; set; } = true;

    /// <summary>
    /// Vision 解析時のプロンプト雛形。{frame_index} と {timestamp} がフレーム情報で置換される。
    /// </summary>
    public string FramePromptTemplate { get; set; }
        = "この画像を説明してください。動画の {timestamp} 時点のフレーム ({frame_index}枚目) です。";

    /// <summary>抽出フレームの最大幅（ピクセル）。0なら元解像度。</summary>
    public int MaxFrameWidth { get; set; } = 1280;

    /// <summary>抽出フレームの形式（jpg / png）。</summary>
    public VideoFrameFormat FrameFormat { get; set; } = VideoFrameFormat.Jpeg;

    /// <summary>JPEG品質（1-31）。値が小さいほど高品質、大きいほど低品質。</summary>
    public int JpegQuality { get; set; } = 5;
}

/// <summary>抽出フレームの画像形式。</summary>
public enum VideoFrameFormat
{
    /// <summary>JPEG（小サイズ、推奨）。</summary>
    Jpeg,
    /// <summary>PNG（可逆圧縮）。</summary>
    Png,
}
