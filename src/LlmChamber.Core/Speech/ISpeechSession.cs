using System.IO;

namespace LlmChamber.Speech;

/// <summary>
/// 音声機能（STT/TTS）のセッション。
/// <see cref="ILocalLlm.UseSpeech"/> の戻り値として取得する。
/// </summary>
public interface ISpeechSession : IAsyncDisposable
{
    /// <summary>
    /// 音声ストリーム（WAV/MP3/OGG/FLAC等）を文字起こしする。
    /// </summary>
    /// <param name="audioStream">読み取り可能な音声ストリーム。</param>
    /// <param name="options">STTオプション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="SpeechRuntimeException">Whisperプロセスの実行に失敗した場合。</exception>
    Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        TranscribeOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 音声ファイル（WAV/MP3/OGG/FLAC等）を文字起こしする。
    /// </summary>
    /// <param name="audioFilePath">音声ファイルへのパス。</param>
    /// <param name="options">STTオプション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="SpeechRuntimeException">Whisperプロセスの実行に失敗した場合。</exception>
    Task<TranscriptionResult> TranscribeFileAsync(
        string audioFilePath,
        TranscribeOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// テキストを音声合成し、WAVバイナリとして返す。
    /// </summary>
    /// <param name="text">読み上げるテキスト。</param>
    /// <param name="options">TTSオプション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="SpeechRuntimeException">Piperプロセスの実行に失敗した場合。</exception>
    Task<byte[]> SpeakAsync(
        string text,
        SpeakOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// テキストを音声合成し、WAVファイルとして保存する。
    /// </summary>
    /// <param name="text">読み上げるテキスト。</param>
    /// <param name="outputFilePath">出力WAVファイルパス。</param>
    /// <param name="options">TTSオプション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    Task SpeakToFileAsync(
        string text,
        string outputFilePath,
        SpeakOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whisperバイナリ・モデル・Piper voice 等の追加リソースダウンロード進捗。
    /// </summary>
    event EventHandler<DownloadProgress>? ResourceDownloadProgress;
}
