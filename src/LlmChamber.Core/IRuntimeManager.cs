namespace LlmChamber;

/// <summary>
/// Ollamaバイナリとモデルファイルの管理。
/// </summary>
public interface IRuntimeManager
{
    /// <summary>
    /// Ollamaバイナリがローカルにあることを確認し、なければダウンロードする。
    /// バイナリのパスを返す。
    /// </summary>
    Task<string> EnsureRuntimeAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>インストール済みOllamaのバージョンを取得する。未インストールの場合はnull。</summary>
    Task<string?> GetRuntimeVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>指定モデルがpull済みか確認し、未pullならダウンロードする。</summary>
    Task EnsureModelAsync(
        string modelTag,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>ローカルにインストール済みのモデル一覧を取得する。</summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>指定モデルをローカルから削除する。</summary>
    Task DeleteModelAsync(string modelTag, CancellationToken cancellationToken = default);

    /// <summary>キャッシュディレクトリの合計サイズ（バイト）を取得する。</summary>
    Task<long> GetCacheSizeBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>利用可能なモデルプリセット一覧を取得する。</summary>
    IReadOnlyList<ModelPreset> GetAvailablePresets();
}
