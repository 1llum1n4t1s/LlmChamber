using System.Net.Http;
using LlmChamber.Internal;
using Microsoft.Extensions.Options;

namespace LlmChamber;

/// <summary>
/// DI不使用時のファクトリ。
/// usingパターンで使用する。
/// </summary>
public static class LlmChamberFactory
{
    /// <summary>
    /// ILocalLlmインスタンスを作成する。
    /// Disposeで自動的にOllamaプロセスが停止する。
    /// </summary>
    public static ILocalLlm Create(Action<LlmChamberOptions>? configure = null)
    {
        var options = new LlmChamberOptions();
        configure?.Invoke(options);

        // ダウンローダーとAPIクライアントで別のHttpClientを使用する
        // （HttpClient.BaseAddressはリクエスト送信後に変更できないため）
        var downloadHttpClient = new HttpClient();
        var apiHttpClient = new HttpClient();

        var downloader = new OllamaDownloader(downloadHttpClient);
        var wrappedOptions = Options.Create(options);
        var processManager = new OllamaProcessManager(wrappedOptions);
        var apiClient = new OllamaApiClient(apiHttpClient);
        var runtimeManager = new RuntimeManager(downloader, apiClient, processManager, wrappedOptions);

        return new LocalLlm(
            wrappedOptions,
            downloader,
            processManager,
            apiClient,
            runtimeManager);
    }
}
