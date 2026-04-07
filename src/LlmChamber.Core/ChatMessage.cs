namespace LlmChamber;

/// <summary>チャットメッセージのロール。</summary>
public enum ChatRole
{
    /// <summary>システムプロンプト</summary>
    System,
    /// <summary>ユーザー入力</summary>
    User,
    /// <summary>アシスタント応答</summary>
    Assistant,
}

/// <summary>チャットメッセージ。</summary>
/// <param name="Role">メッセージのロール。</param>
/// <param name="Content">メッセージ本文。</param>
/// <param name="Timestamp">メッセージのタイムスタンプ。</param>
public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    DateTimeOffset Timestamp)
{
    /// <summary>ユーザーメッセージを作成する。</summary>
    public static ChatMessage FromUser(string content) =>
        new(ChatRole.User, content, DateTimeOffset.UtcNow);

    /// <summary>アシスタントメッセージを作成する。</summary>
    public static ChatMessage FromAssistant(string content) =>
        new(ChatRole.Assistant, content, DateTimeOffset.UtcNow);

    /// <summary>システムメッセージを作成する。</summary>
    public static ChatMessage FromSystem(string content) =>
        new(ChatRole.System, content, DateTimeOffset.UtcNow);
}
