using System.IO;

namespace LlmChamber;

/// <summary>LlmChamberライブラリ全体の設定。</summary>
public sealed class LlmChamberOptions
{
    /// <summary>デフォルトで使用するモデルのプリセットIDまたはOllamaタグ。</summary>
    public string DefaultModel { get; set; } = "gemma4-e2b";

    /// <summary>
    /// キャッシュディレクトリ。Ollamaバイナリとモデルファイルが配置される。
    /// デフォルト: ~/.llmchamber/
    /// </summary>
    public string CacheDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".llmchamber");

    /// <summary>
    /// Ollamaバイナリの自動ダウンロードを有効にするかどうか。
    /// falseの場合、バイナリが見つからないと例外が発生する。
    /// </summary>
    public bool AutoDownloadRuntime { get; set; } = true;

    /// <summary>
    /// モデルの自動pullを有効にするかどうか。
    /// falseの場合、モデルが見つからないと例外が発生する。
    /// </summary>
    public bool AutoPullModel { get; set; } = true;

    /// <summary>Ollamaプロセスの起動タイムアウト。</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Ollamaプロセスの停止タイムアウト。</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// ランタイムバイナリのバリアント。
    /// AutoはGPU/NPUを自動検出して最適なバリアントを選択する。
    /// </summary>
    public RuntimeVariant RuntimeVariant { get; set; } = RuntimeVariant.Auto;

    /// <summary>
    /// 使用するOllamaのバージョン。nullの場合はライブラリ内蔵のデフォルトバージョン。
    /// </summary>
    public string? OllamaVersion { get; set; }

    /// <summary>
    /// 共有モデルディレクトリ。設定するとグローバルOllamaのモデルを再利用できる。
    /// nullの場合はCacheDirectory配下にモデルを格納。
    /// </summary>
    public string? SharedModelDirectory { get; set; }
}
