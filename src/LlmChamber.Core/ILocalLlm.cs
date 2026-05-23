namespace LlmChamber;

/// <summary>
/// ローカルLLM推論のメインエントリポイント。
/// 内部でOllamaプロセスのライフサイクルを管理する。
/// </summary>
public interface ILocalLlm : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Ollamaランタイムのダウンロード（必要な場合）、プロセス起動、モデルpullを実行する。
    /// 初回推論時に自動的に呼ばれるが、事前にウォームアップしたい場合に明示的に呼び出し可能。
    /// </summary>
    /// <exception cref="UnsupportedPlatformException">サポートされていないOS/アーキテクチャの場合。</exception>
    /// <exception cref="RuntimeNotFoundException">Ollamaバイナリが見つからず、自動ダウンロードが無効の場合。</exception>
    /// <exception cref="RuntimeInstallException">ランタイムのダウンロードまたは展開に失敗した場合。</exception>
    /// <exception cref="ProcessStartException">Ollamaプロセスの起動に失敗した場合。</exception>
    /// <exception cref="OllamaApiException">モデルpull等のOllama API呼び出しに失敗した場合。</exception>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>初期化が完了しOllamaプロセスが稼働中かどうか。</summary>
    bool IsReady { get; }

    /// <summary>ランタイム・モデル管理インターフェース。</summary>
    IRuntimeManager Runtime { get; }

    /// <summary>
    /// プロンプトからテキストをストリーミング生成する。
    /// 初回呼び出し時に自動的に初期化される。
    /// </summary>
    /// <exception cref="LlmChamberException">初期化またはOllama APIの呼び出しに失敗した場合。</exception>
    /// <exception cref="OllamaApiException">推論リクエストのHTTPエラー。</exception>
    IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像付きプロンプトからテキストをストリーミング生成する。
    /// multimodal モデル（gemma3、llava、qwen2.5vl 等）使用時のみ画像が解析される。
    /// </summary>
    /// <param name="prompt">テキストプロンプト。</param>
    /// <param name="images">添付画像（PNG/JPEG等のバイナリ）。nullまたは空なら通常のテキスト生成。</param>
    /// <param name="options">推論パラメータ。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        IReadOnlyList<byte[]>? images,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// プロンプトからテキストを一括生成して返す。
    /// 初回呼び出し時に自動的に初期化される。
    /// </summary>
    /// <exception cref="LlmChamberException">初期化またはOllama APIの呼び出しに失敗した場合。</exception>
    /// <exception cref="OllamaApiException">推論リクエストのHTTPエラー。</exception>
    Task<string> GenerateCompleteAsync(
        string prompt,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像付きプロンプトからテキストを一括生成する。
    /// multimodal モデル使用時のみ画像が解析される。
    /// </summary>
    Task<string> GenerateCompleteAsync(
        string prompt,
        IReadOnlyList<byte[]>? images,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>新しいチャットセッションを作成する。</summary>
    IChatSession CreateChatSession(ChatOptions? options = null);

    /// <summary>テキストのEmbeddingベクトルを取得する。</summary>
    /// <exception cref="LlmChamberException">初期化またはOllama APIの呼び出しに失敗した場合。</exception>
    /// <exception cref="OllamaApiException">Embedding APIのHTTPエラー。</exception>
    Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>Ollamaランタイムのダウンロード進捗イベント。</summary>
    event EventHandler<DownloadProgress>? RuntimeDownloadProgress;

    /// <summary>モデルのダウンロード進捗イベント。</summary>
    event EventHandler<DownloadProgress>? ModelDownloadProgress;

    /// <summary>
    /// 音声機能（STT/TTS）を初期化する。
    /// このメソッド自体はバイナリDLを行わず軽量。
    /// バイナリ・モデルのダウンロードは <see cref="LlmChamber.Speech.ISpeechSession.TranscribeAsync"/> や
    /// <see cref="LlmChamber.Speech.ISpeechSession.SpeakAsync"/> の初回呼び出し時に発生する。
    /// 既に初期化済みなら同じインスタンスを返す（オプション変更は無視される）。
    /// </summary>
    LlmChamber.Speech.ISpeechSession UseSpeech(LlmChamber.Speech.SpeechOptions? options = null);

    /// <summary>
    /// 音声機能が初期化済みの場合のみ <see cref="LlmChamber.Speech.ISpeechSession"/> を返す。未初期化なら null。
    /// </summary>
    LlmChamber.Speech.ISpeechSession? Speech { get; }

    /// <summary>
    /// 動画機能を初期化する。
    /// このメソッド自体はバイナリDLを行わず軽量。
    /// FFmpeg のダウンロードは <see cref="LlmChamber.Media.IVideoSession.AnalyzeAsync"/> や
    /// <see cref="LlmChamber.Media.IVideoSession.ExtractFramesAsync"/> の初回呼び出し時に発生する。
    /// </summary>
    LlmChamber.Media.IVideoSession UseMedia(LlmChamber.Media.MediaOptions? options = null);

    /// <summary>
    /// 動画機能が初期化済みの場合のみ <see cref="LlmChamber.Media.IVideoSession"/> を返す。未初期化なら null。
    /// </summary>
    LlmChamber.Media.IVideoSession? Media { get; }
}
