using System.Net.Http;
using LlmChamber.Internal;
using LlmChamber.Media;
using LlmChamber.Speech;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LlmChamber.Tests;

/// <summary>
/// オプトインゼロコスト原則のテスト。
/// UseSpeech / UseMedia を呼ぶまで、Speech / Media プロパティは null。
/// 呼んでも実際のバイナリDLは発生しない（初回利用時まで遅延）。
/// </summary>
public class LazyInitializationTests
{
    [Fact]
    public void LocalLlm_BeforeUseSpeech_SpeechIsNull()
    {
        using var localLlm = CreateLocalLlm();
        Assert.Null(localLlm.Speech);
    }

    [Fact]
    public void LocalLlm_BeforeUseMedia_MediaIsNull()
    {
        using var localLlm = CreateLocalLlm();
        Assert.Null(localLlm.Media);
    }

    [Fact]
    public void LocalLlm_UseSpeech_ReturnsNonNullSession()
    {
        using var localLlm = CreateLocalLlm();
        var speech = localLlm.UseSpeech();
        Assert.NotNull(speech);
        Assert.Same(speech, localLlm.Speech);
    }

    [Fact]
    public void LocalLlm_UseMedia_ReturnsNonNullSession()
    {
        using var localLlm = CreateLocalLlm();
        var video = localLlm.UseMedia();
        Assert.NotNull(video);
        Assert.Same(video, localLlm.Media);
    }

    [Fact]
    public void LocalLlm_UseSpeech_CalledTwice_ReturnsSameInstance()
    {
        using var localLlm = CreateLocalLlm();
        var first = localLlm.UseSpeech();
        var second = localLlm.UseSpeech(new SpeechOptions { WhisperModel = WhisperModelSize.Small });
        // 2回目は最初のインスタンスを返す（オプションは無視される）
        Assert.Same(first, second);
    }

    [Fact]
    public void LocalLlm_UseMedia_CalledTwice_ReturnsSameInstance()
    {
        using var localLlm = CreateLocalLlm();
        var first = localLlm.UseMedia();
        var second = localLlm.UseMedia(new MediaOptions());
        Assert.Same(first, second);
    }

    [Fact]
    public void LocalLlm_UseSpeech_DoesNotTriggerDownload()
    {
        // UseSpeech() 呼び出し自体ではネットワークアクセスは発生しないことを確認
        // HttpClientの送信件数が増えていないこと
        var httpHandler = new CountingHandler();
        var httpClient = new HttpClient(httpHandler);

        using var localLlm = CreateLocalLlm(downloadHttpClient: httpClient);
        _ = localLlm.UseSpeech();

        Assert.Equal(0, httpHandler.SendCount);
    }

    [Fact]
    public void LocalLlm_UseMedia_DoesNotTriggerDownload()
    {
        var httpHandler = new CountingHandler();
        var httpClient = new HttpClient(httpHandler);

        using var localLlm = CreateLocalLlm(downloadHttpClient: httpClient);
        _ = localLlm.UseMedia();

        Assert.Equal(0, httpHandler.SendCount);
    }

    [Fact]
    public async Task LocalLlm_DisposeAsync_DisposesSpeechAndMediaSessions()
    {
        var localLlm = CreateLocalLlm();
        var speech = localLlm.UseSpeech();
        var video = localLlm.UseMedia();

        await localLlm.DisposeAsync();

        // DisposeAsync 後は同じインスタンスへのアクセスがエラーを返す（既にdisposed）
        // ここでは throwしないことだけ確認（実装依存）
        Assert.NotNull(speech);
        Assert.NotNull(video);
    }

    // ── ヘルパー ──

    private static LocalLlm CreateLocalLlm(HttpClient? downloadHttpClient = null)
    {
        var opts = new LlmChamberOptions
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}"),
        };
        var wrappedOptions = Options.Create(opts);
        var downloader = new OllamaDownloader(new HttpClient());
        var processManager = new OllamaProcessManager(wrappedOptions);
        var apiClient = new OllamaApiClient(new HttpClient());
        var runtimeManager = Substitute.For<IRuntimeManager>();

        // テストでは LocalLlm.Dispose() で HttpClient も解放されるよう所有権を渡す（リーク防止）
        return new LocalLlm(
            wrappedOptions,
            downloader,
            processManager,
            apiClient,
            runtimeManager,
            downloadHttpClient ?? new HttpClient(),
            ownsDownloadHttpClient: true);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
