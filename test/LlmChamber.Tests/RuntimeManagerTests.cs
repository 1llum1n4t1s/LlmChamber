using System.Diagnostics;
using System.Reflection;
using LlmChamber.Internal;
using Microsoft.Extensions.Options;
using Xunit;

namespace LlmChamber.Tests;

public sealed class RuntimeManagerTests
{
    [Fact]
    public async Task GetRuntimeVersionAsync_WhenApiRequestIsCanceled_PropagatesCancellation()
    {
        var options = new LlmChamberOptions
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
        };
        using var downloadClient = new HttpClient();
        using var apiClient = new HttpClient(new CanceledRequestHandler())
        {
            BaseAddress = new Uri("http://localhost"),
        };
        using var currentProcess = Process.GetCurrentProcess();
        var processManager = new OllamaProcessManager(Options.Create(options));
        var processField = typeof(OllamaProcessManager).GetField(
            "_process", BindingFlags.Instance | BindingFlags.NonPublic)!;
        processField.SetValue(processManager, currentProcess);

        var manager = new RuntimeManager(
            new OllamaDownloader(downloadClient),
            new OllamaApiClient(apiClient),
            processManager,
            Options.Create(options));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                manager.GetRuntimeVersionAsync(cancellation.Token));
        }
        finally
        {
            processField.SetValue(processManager, null);
            processManager.Dispose();
        }
    }

    private sealed class CanceledRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
}
