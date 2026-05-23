namespace LlmChamber;

/// <summary>
/// マルチターン対話の管理。
/// 会話履歴を自動的に管理し、ストリーミング応答をサポートする。
/// </summary>
public interface IChatSession
{
    /// <summary>
    /// ユーザーメッセージを送信し、アシスタントの応答をストリーミングで返す。
    /// メッセージと応答は自動的に履歴に追加される。
    /// API失敗時はユーザーメッセージを自動ロールバックする。
    /// </summary>
    /// <exception cref="OllamaApiException">Ollama Chat APIのHTTPエラー。</exception>
    /// <exception cref="LlmChamberException">Ollamaとの通信に失敗した場合。</exception>
    IAsyncEnumerable<string> SendAsync(
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像付きユーザーメッセージを送信し、アシスタントの応答をストリーミングで返す。
    /// multimodal モデル（gemma3、llava、qwen2.5vl 等）使用時のみ画像が解析される。
    /// </summary>
    /// <param name="message">テキスト本文。</param>
    /// <param name="images">添付画像（PNG/JPEG等のバイナリ）。nullまたは空なら通常のテキスト送信。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    IAsyncEnumerable<string> SendAsync(
        string message,
        IReadOnlyList<byte[]>? images,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーメッセージを送信し、アシスタントの完全な応答を返す。
    /// メッセージと応答は自動的に履歴に追加される。
    /// API失敗時はユーザーメッセージを自動ロールバックする。
    /// </summary>
    /// <exception cref="OllamaApiException">Ollama Chat APIのHTTPエラー。</exception>
    /// <exception cref="LlmChamberException">Ollamaとの通信に失敗した場合。</exception>
    Task<string> SendCompleteAsync(
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像付きユーザーメッセージを送信し、アシスタントの完全な応答を返す。
    /// multimodal モデル使用時のみ画像が解析される。
    /// </summary>
    Task<string> SendCompleteAsync(
        string message,
        IReadOnlyList<byte[]>? images,
        CancellationToken cancellationToken = default);

    /// <summary>会話履歴（読み取り専用スナップショット）。</summary>
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>会話履歴をクリアする。SystemPromptが設定されている場合は保持される。</summary>
    void ClearHistory();

    /// <summary>このセッションのチャット設定。</summary>
    ChatOptions Options { get; }
}
