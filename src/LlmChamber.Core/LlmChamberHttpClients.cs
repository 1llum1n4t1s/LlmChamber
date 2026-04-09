namespace LlmChamber;

/// <summary>
/// LlmChamber内部で使用するHttpClientのDIキー定数。
/// AddKeyedSingleton/GetRequiredKeyedServiceでカスタムHttpClient（プロキシ、証明書など）を差し替える場合に使用する。
/// </summary>
public static class LlmChamberHttpClients
{
    /// <summary>Ollamaバイナリダウンロード用（GitHub Releases向け）。</summary>
    public const string Downloader = "LlmChamber.Downloader";

    /// <summary>Ollama APIクライアント用（ローカルOllamaプロセス向け）。</summary>
    public const string Api = "LlmChamber.Api";
}
