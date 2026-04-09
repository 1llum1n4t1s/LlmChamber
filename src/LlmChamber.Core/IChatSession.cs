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
    /// ユーザーメッセージを送信し、アシスタントの完全な応答を返す。
    /// メッセージと応答は自動的に履歴に追加される。
    /// API失敗時はユーザーメッセージを自動ロールバックする。
    /// </summary>
    /// <exception cref="OllamaApiException">Ollama Chat APIのHTTPエラー。</exception>
    /// <exception cref="LlmChamberException">Ollamaとの通信に失敗した場合。</exception>
    Task<string> SendCompleteAsync(
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>会話履歴（読み取り専用スナップショット）。</summary>
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>会話履歴をクリアする。SystemPromptが設定されている場合は保持される。</summary>
    void ClearHistory();

    /// <summary>このセッションのチャット設定。</summary>
    ChatOptions Options { get; }
}
