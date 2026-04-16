using System.Net.Http;
using LlmChamber.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LlmChamber;

/// <summary>DI登録の拡張メソッド。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// LlmChamberをDIコンテナに登録する。
    /// </summary>
    public static IServiceCollection AddLlmChamber(
        this IServiceCollection services,
        Action<LlmChamberOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<LlmChamberOptions>(_ => { });

        // ダウンローダーとAPIクライアントで別のHttpClientを使用する
        // （HttpClient.BaseAddressはリクエスト送信後に変更できないため）
        // TryAddで登録し、消費者が事前にカスタムHttpClientを登録していれば上書きしない
        services.TryAddKeyedSingleton<HttpClient>(LlmChamberHttpClients.Downloader);
        services.TryAddKeyedSingleton<HttpClient>(LlmChamberHttpClients.Api);

        services.AddSingleton(sp => new OllamaDownloader(
            sp.GetRequiredKeyedService<HttpClient>(LlmChamberHttpClients.Downloader)));

        services.AddSingleton(sp => new OllamaProcessManager(
            sp.GetRequiredService<IOptions<LlmChamberOptions>>()));

        services.AddSingleton(sp => new OllamaApiClient(
            sp.GetRequiredKeyedService<HttpClient>(LlmChamberHttpClients.Api)));

        services.AddSingleton<IRuntimeManager>(sp => new RuntimeManager(
            sp.GetRequiredService<OllamaDownloader>(),
            sp.GetRequiredService<OllamaApiClient>(),
            sp.GetRequiredService<OllamaProcessManager>(),
            sp.GetRequiredService<IOptions<LlmChamberOptions>>()));

        services.AddSingleton<ILocalLlm>(sp => new LocalLlm(
            sp.GetRequiredService<IOptions<LlmChamberOptions>>(),
            sp.GetRequiredService<OllamaDownloader>(),
            sp.GetRequiredService<OllamaProcessManager>(),
            sp.GetRequiredService<OllamaApiClient>(),
            sp.GetRequiredService<IRuntimeManager>()));

        return services;
    }
}
