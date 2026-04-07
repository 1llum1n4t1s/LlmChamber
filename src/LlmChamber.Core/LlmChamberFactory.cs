using System.Net.Http;
using LlmChamber.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        var loggerFactory = NullLoggerFactory.Instance;
        var httpClient = new HttpClient();

        var downloader = new OllamaDownloader(httpClient, loggerFactory.CreateLogger<OllamaDownloader>());
        var wrappedOptions = Options.Create(options);
        var processManager = new OllamaProcessManager(loggerFactory.CreateLogger<OllamaProcessManager>(), wrappedOptions);
        var apiClient = new OllamaApiClient(httpClient, loggerFactory.CreateLogger<OllamaApiClient>());
        var runtimeManager = new RuntimeManager(downloader, apiClient, processManager, wrappedOptions,
            loggerFactory.CreateLogger<RuntimeManager>());

        return new LocalLlm(
            Options.Create(options),
            downloader,
            processManager,
            apiClient,
            runtimeManager,
            loggerFactory.CreateLogger<LocalLlm>());
    }
}
