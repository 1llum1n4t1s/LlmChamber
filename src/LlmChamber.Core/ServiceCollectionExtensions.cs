using System.Net.Http;
using LlmChamber.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LlmChamber;

/// <summary>DI登録の拡張メソッド。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// LlmChamberをDIコンテナに登録する。
    /// AddLogging()の呼び出し有無・順序に依存しない。
    /// ILoggerFactoryが登録されていればそれを使い、未登録ならNullLoggerにフォールバックする。
    /// </summary>
    public static IServiceCollection AddLlmChamber(
        this IServiceCollection services,
        Action<LlmChamberOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<LlmChamberOptions>(_ => { });

        // ファクトリデリゲートで登録。ILogger<T>をGetServiceで取得し、未登録時はNullLoggerにフォールバック。
        // これによりAddLogging()の呼び出し有無・順序に完全に非依存。
        services.AddSingleton<HttpClient>();

        services.AddSingleton(sp => new OllamaDownloader(
            sp.GetRequiredService<HttpClient>(),
            ResolveLogger<OllamaDownloader>(sp)));

        services.AddSingleton(sp => new OllamaProcessManager(
            ResolveLogger<OllamaProcessManager>(sp),
            sp.GetRequiredService<IOptions<LlmChamberOptions>>()));

        services.AddSingleton(sp => new OllamaApiClient(
            sp.GetRequiredService<HttpClient>(),
            ResolveLogger<OllamaApiClient>(sp)));

        services.AddSingleton<IRuntimeManager>(sp => new RuntimeManager(
            sp.GetRequiredService<OllamaDownloader>(),
            sp.GetRequiredService<OllamaApiClient>(),
            sp.GetRequiredService<OllamaProcessManager>(),
            sp.GetRequiredService<IOptions<LlmChamberOptions>>(),
            ResolveLogger<RuntimeManager>(sp)));

        services.AddSingleton<ILocalLlm>(sp => new LocalLlm(
            sp.GetRequiredService<IOptions<LlmChamberOptions>>(),
            sp.GetRequiredService<OllamaDownloader>(),
            sp.GetRequiredService<OllamaProcessManager>(),
            sp.GetRequiredService<OllamaApiClient>(),
            sp.GetRequiredService<IRuntimeManager>(),
            ResolveLogger<LocalLlm>(sp)));

        return services;
    }

    /// <summary>ILogger&lt;T&gt;を解決する。未登録ならNullLogger&lt;T&gt;にフォールバック。</summary>
    private static ILogger<T> ResolveLogger<T>(IServiceProvider sp) =>
        sp.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
