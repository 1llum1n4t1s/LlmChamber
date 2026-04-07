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
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>初期化が完了しOllamaプロセスが稼働中かどうか。</summary>
    bool IsReady { get; }

    /// <summary>ランタイム・モデル管理インターフェース。</summary>
    IRuntimeManager Runtime { get; }

    /// <summary>
    /// プロンプトからテキストをストリーミング生成する。
    /// 初回呼び出し時に自動的に初期化される。
    /// </summary>
    IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// プロンプトからテキストを一括生成して返す。
    /// 初回呼び出し時に自動的に初期化される。
    /// </summary>
    Task<string> GenerateCompleteAsync(
        string prompt,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>新しいチャットセッションを作成する。</summary>
    IChatSession CreateChatSession(ChatOptions? options = null);

    /// <summary>テキストのEmbeddingベクトルを取得する。</summary>
    Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>Ollamaランタイムのダウンロード進捗イベント。</summary>
    event EventHandler<DownloadProgress>? RuntimeDownloadProgress;

    /// <summary>モデルのダウンロード進捗イベント。</summary>
    event EventHandler<DownloadProgress>? ModelDownloadProgress;
}
