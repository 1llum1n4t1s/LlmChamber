using System.Net;
using System.Text;
using LlmChamber.Internal;
using LlmChamber.Internal.Api;
using Xunit;

namespace LlmChamber.Tests;

public sealed class OllamaApiClientTests
{
    [Fact]
    public async Task SetBaseUrl_AfterFirstRequest_UsesNewUriWithoutMutatingHttpClient()
    {
        var handler = new StubHandler(_ => JsonResponse("{\"version\":\"1.0\"}"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434"),
        };
        var apiClient = new OllamaApiClient(httpClient);

        await apiClient.GetVersionAsync(TestContext.Current.CancellationToken);
        apiClient.SetBaseUrl("http://localhost:22468");
        await apiClient.GetVersionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [new Uri("http://localhost:11434/api/version"), new Uri("http://localhost:22468/api/version")],
            handler.RequestUris);
        Assert.Equal(new Uri("http://localhost:11434"), httpClient.BaseAddress);
    }

    [Fact]
    public async Task GenerateStreamAsync_HttpError_ThrowsOllamaApiExceptionWithResponseDetails()
    {
        const string responseBody = "{\"error\":\"model not found\"}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        });
        using var httpClient = CreateHttpClient(handler);
        var apiClient = new OllamaApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaApiException>(async () =>
        {
            await foreach (string _ in apiClient.GenerateStreamAsync(
                "missing", "prompt", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal((int)HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal(responseBody, exception.ResponseBody);
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("chat")]
    [InlineData("pull")]
    public async Task StreamingApi_ErrorChunk_ThrowsOllamaApiException(string api)
    {
        const string errorLine = "{\"error\":\"stream failed\"}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(errorLine + "\n", Encoding.UTF8, "application/x-ndjson"),
        });
        using var httpClient = CreateHttpClient(handler);
        var apiClient = new OllamaApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaApiException>(
            () => DrainStreamingApiAsync(apiClient, api));

        Assert.Equal((int)HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal(errorLine, exception.ResponseBody);
        Assert.Contains("stream failed", exception.Message);
    }

    [Fact]
    public async Task GenerateCompleteAsync_ErrorBodyWithSuccessStatus_ThrowsOllamaApiException()
    {
        const string responseBody = "{\"error\":\"generation failed\"}";
        var handler = new StubHandler(_ => JsonResponse(responseBody));
        using var httpClient = CreateHttpClient(handler);
        var apiClient = new OllamaApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaApiException>(() =>
            apiClient.GenerateCompleteAsync(
                "model", "prompt", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal(responseBody, exception.ResponseBody);
    }

    [Fact]
    public async Task PullModelAsync_StreamEndsBeforeSuccess_ThrowsOllamaApiException()
    {
        const string progressLine = "{\"status\":\"pulling manifest\"}";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(progressLine + "\n", Encoding.UTF8, "application/x-ndjson"),
        });
        using var httpClient = CreateHttpClient(handler);
        var apiClient = new OllamaApiClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaApiException>(async () =>
        {
            await foreach (PullResponse _ in apiClient.PullModelAsync(
                "model", TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal((int)HttpStatusCode.OK, exception.StatusCode);
        Assert.Contains("正常完了通知", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PullModelAsync_SuccessChunk_CompletesNormally()
    {
        const string responseBody = "{\"status\":\"pulling manifest\"}\n{\"status\":\"success\"}\n";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/x-ndjson"),
        });
        using var httpClient = CreateHttpClient(handler);
        var apiClient = new OllamaApiClient(httpClient);
        var statuses = new List<string>();

        await foreach (PullResponse response in apiClient.PullModelAsync(
            "model", TestContext.Current.CancellationToken))
        {
            statuses.Add(response.Status);
        }

        Assert.Equal(["pulling manifest", "success"], statuses);
    }

    [Theory]
    [InlineData("post")]
    [InlineData("delete")]
    public async Task ApiRequest_DisposesRequestResponseAndContent(string operation)
    {
        string responseJson = operation == "post"
            ? "{\"response\":\"done\",\"done\":true}"
            : "";
        var responseContent = new TrackingContent(responseJson);
        var response = new TrackingResponseMessage(HttpStatusCode.OK)
        {
            Content = responseContent,
        };
        var handler = new StubHandler(_ => response, trackRequestContent: true);
        using var httpClient = CreateHttpClient(handler);
        var apiClient = new OllamaApiClient(httpClient);

        if (operation == "post")
        {
            await apiClient.GenerateCompleteAsync(
                "model", "prompt", cancellationToken: TestContext.Current.CancellationToken);
        }
        else
        {
            await apiClient.DeleteModelAsync("model", TestContext.Current.CancellationToken);
        }

        Assert.True(response.IsDisposed);
        Assert.True(responseContent.IsDisposed);
        Assert.NotNull(handler.RequestContent);
        Assert.True(handler.RequestContent.IsDisposed);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://localhost:11434"),
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task DrainStreamingApiAsync(OllamaApiClient apiClient, string api)
    {
        switch (api)
        {
            case "generate":
                await foreach (string _ in apiClient.GenerateStreamAsync(
                    "model", "prompt", cancellationToken: TestContext.Current.CancellationToken))
                {
                }
                break;
            case "chat":
                await foreach (string _ in apiClient.ChatStreamAsync(
                    "model",
                    [new OllamaMessage { Role = "user", Content = "prompt" }],
                    cancellationToken: TestContext.Current.CancellationToken))
                {
                }
                break;
            case "pull":
                await foreach (PullResponse _ in apiClient.PullModelAsync(
                    "model", TestContext.Current.CancellationToken))
                {
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(api));
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        bool trackRequestContent = false) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];
        public TrackingContent? RequestContent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (trackRequestContent && request.Content is not null)
            {
                request.Content.Dispose();
                RequestContent = new TrackingContent("{}");
                request.Content = RequestContent;
            }

            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TrackingResponseMessage(HttpStatusCode statusCode)
        : HttpResponseMessage(statusCode)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingContent(string value) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(value);

        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
