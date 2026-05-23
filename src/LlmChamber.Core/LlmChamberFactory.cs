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
        // Timeout = InfiniteTimeSpan: 大きなバイナリ/モデルDLや長時間推論は CancellationToken で制御する
        var downloadHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var apiHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        var downloader = new OllamaDownloader(downloadHttpClient);
        var wrappedOptions = Options.Create(options);
        var processManager = new OllamaProcessManager(wrappedOptions);
        var apiClient = new OllamaApiClient(apiHttpClient);
        var runtimeManager = new RuntimeManager(downloader, apiClient, processManager, wrappedOptions);

        // Factory 経由では HttpClient の所有権を LocalLlm に渡す（Dispose 時に HttpClient も Dispose する）
        return new LocalLlm(
            wrappedOptions,
            downloader,
            processManager,
            apiClient,
            runtimeManager,
            downloadHttpClient,
            ownsDownloadHttpClient: true);
    }
}
