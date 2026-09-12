using System.Net.Http;
using System.Reflection;
using LlmChamber;
using LlmChamber.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LlmChamber.Tests;

public class HttpClientOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factory_Dispose_ReleasesBothClients(bool asynchronously)
    {
        var llm = LlmChamberFactory.Create();
        var download = GetField<HttpClient>(llm, "_downloadHttpClient");
        var api = GetField<HttpClient>(GetField<OllamaApiClient>(llm, "_apiClient"), "_httpClient");
        try
        {
            if (asynchronously) await llm.DisposeAsync();
            else llm.Dispose();

            Assert.Throws<ObjectDisposedException>(() => download.Timeout = TimeSpan.FromSeconds(1));
            Assert.Throws<ObjectDisposedException>(() => api.Timeout = TimeSpan.FromSeconds(1));
            await llm.DisposeAsync();
            llm.Dispose();
        }
        finally
        {
            await llm.DisposeAsync();
            download.Dispose();
            api.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Di_Dispose_LeavesClientsOwnedByContainer(bool asynchronously)
    {
        var services = new ServiceCollection();
        services.AddLlmChamber();
        await using var provider = services.BuildServiceProvider();
        var llm = provider.GetRequiredService<ILocalLlm>();
        var download = provider.GetRequiredKeyedService<HttpClient>(LlmChamberHttpClients.Downloader);
        var api = provider.GetRequiredKeyedService<HttpClient>(LlmChamberHttpClients.Api);

        if (asynchronously) await llm.DisposeAsync();
        else llm.Dispose();

        download.Timeout = TimeSpan.FromSeconds(1);
        api.Timeout = TimeSpan.FromSeconds(1);
        await provider.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => download.Timeout = TimeSpan.FromSeconds(2));
        Assert.Throws<ObjectDisposedException>(() => api.Timeout = TimeSpan.FromSeconds(2));
    }

    private static T GetField<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
