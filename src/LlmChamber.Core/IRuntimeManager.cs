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
    /// <exception cref="UnsupportedPlatformException">サポートされていないOS/アーキテクチャの場合。</exception>
    /// <exception cref="RuntimeNotFoundException">バイナリが見つからず、自動ダウンロードが無効の場合。</exception>
    /// <exception cref="RuntimeInstallException">ダウンロードまたは展開に失敗した場合。</exception>
    Task<string> EnsureRuntimeAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>インストール済みOllamaのバージョンを取得する。未インストールの場合はnull。</summary>
    Task<string?> GetRuntimeVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>指定モデルがpull済みか確認し、未pullならダウンロードする。</summary>
    /// <exception cref="OllamaApiException">モデルpullのAPI呼び出しに失敗した場合。</exception>
    /// <exception cref="ProcessStartException">Ollamaプロセスが稼働していない場合。</exception>
    Task EnsureModelAsync(
        string modelTag,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>ローカルにインストール済みのモデル一覧を取得する。</summary>
    /// <exception cref="OllamaApiException">Ollama APIの呼び出しに失敗した場合。</exception>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>指定モデルをローカルから削除する。</summary>
    /// <exception cref="OllamaApiException">Ollama APIの呼び出しに失敗した場合。</exception>
    Task DeleteModelAsync(string modelTag, CancellationToken cancellationToken = default);

    /// <summary>キャッシュディレクトリの合計サイズ（バイト）を取得する。</summary>
    Task<long> GetCacheSizeBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>利用可能なモデルプリセット一覧を取得する。</summary>
    IReadOnlyList<ModelPreset> GetAvailablePresets();
}
