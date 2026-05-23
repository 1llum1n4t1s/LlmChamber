namespace LlmChamber.Media;

/// <summary>
/// 動画解析セッション。
/// <see cref="ILocalLlm.UseMedia"/> の戻り値として取得する。
/// </summary>
public interface IVideoSession : IAsyncDisposable
{
    /// <summary>
    /// 動画からフレームを一定間隔で抽出し、各フレームを Vision モデルで解析する。
    /// 結果は <see cref="IAsyncEnumerable{VideoFrameAnalysis}"/> でフレーム順にストリームされる。
    /// </summary>
    /// <param name="videoFilePath">解析対象の動画ファイルパス（mp4/mov/webm等、FFmpegが扱える形式）。</param>
    /// <param name="prompt">追加のコンテキストプロンプト。VisionモデルへFrameTemplate と合わせて渡される。</param>
    /// <param name="options">解析オプション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="FFmpegRuntimeException">FFmpegの実行に失敗した場合。</exception>
    /// <exception cref="FFmpegBinaryNotFoundException">FFmpegバイナリが見つからない場合。</exception>
    IAsyncEnumerable<VideoFrameAnalysis> AnalyzeAsync(
        string videoFilePath,
        string? prompt = null,
        VideoAnalysisOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 動画から指定間隔でフレームを抽出する（Vision 解析なし）。
    /// 軽量で、自前で画像を扱いたい場合に使う。
    /// </summary>
    IAsyncEnumerable<VideoFrameAnalysis> ExtractFramesAsync(
        string videoFilePath,
        VideoAnalysisOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>FFmpegバイナリのダウンロード進捗イベント。</summary>
    event EventHandler<DownloadProgress>? ResourceDownloadProgress;
}
