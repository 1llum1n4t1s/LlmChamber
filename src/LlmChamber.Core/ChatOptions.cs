namespace LlmChamber;

/// <summary>チャットセッションの設定。</summary>
public sealed record ChatOptions
{
    /// <summary>システムプロンプト。セッション作成時に設定。</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>推論パラメータ。nullの場合はモデルプリセットのデフォルト値。</summary>
    public InferenceOptions? InferenceOptions { get; init; }

    /// <summary>
    /// 履歴に保持する最大メッセージ数。システムプロンプトは含まない。
    /// nullの場合は無制限。
    /// </summary>
    public int? MaxHistoryMessages { get; init; }
}
