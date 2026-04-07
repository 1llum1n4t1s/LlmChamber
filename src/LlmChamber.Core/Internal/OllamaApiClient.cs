using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LlmChamber.Internal.Api;
using Microsoft.Extensions.Logging;

namespace LlmChamber.Internal;

/// <summary>
/// Ollama HTTP APIクライアント。
/// ストリーミング応答をIAsyncEnumerableで返す。
/// </summary>
internal sealed class OllamaApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaApiClient> _logger;

    public OllamaApiClient(HttpClient httpClient, ILogger<OllamaApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>APIのベースアドレスを設定する。</summary>
    public void SetBaseUrl(string baseUrl)
    {
        _httpClient.BaseAddress = new Uri(baseUrl);
        // 推論リクエストは長時間かかるため、HttpClient自体のTimeoutは無制限にし、
        // 各リクエストでCancellationTokenにより個別に制御する
        _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }

    /// <summary>Ollamaのバージョンを取得する。</summary>
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync("/api/version",
            OllamaJsonContext.Instance.VersionResponse, cancellationToken);
        return response?.Version ?? "unknown";
    }

    /// <summary>テキスト生成（ストリーミング）。</summary>
    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string model, string prompt, InferenceOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new GenerateRequest
        {
            Model = model,
            Prompt = prompt,
            Stream = true,
            Options = OllamaOptions.FromInferenceOptions(options),
        };

        await foreach (var chunk in PostStreamAsync<GenerateRequest, GenerateResponse>(
            "/api/generate", request, OllamaJsonContext.Instance.GenerateRequest,
            OllamaJsonContext.Instance.GenerateResponse, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Response))
            {
                yield return chunk.Response;
            }
        }
    }

    /// <summary>テキスト生成（一括）。</summary>
    public async Task<string> GenerateCompleteAsync(
        string model, string prompt, InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GenerateRequest
        {
            Model = model,
            Prompt = prompt,
            Stream = false,
            Options = OllamaOptions.FromInferenceOptions(options),
        };

        var response = await PostJsonAsync<GenerateRequest, GenerateResponse>(
            "/api/generate", request, OllamaJsonContext.Instance.GenerateRequest,
            OllamaJsonContext.Instance.GenerateResponse, cancellationToken);

        return response.Response;
    }

    /// <summary>チャット（ストリーミング）。</summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string model, IReadOnlyList<OllamaMessage> messages, InferenceOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = true,
            Options = OllamaOptions.FromInferenceOptions(options),
        };

        await foreach (var chunk in PostStreamAsync<ChatRequest, ChatResponse>(
            "/api/chat", request, OllamaJsonContext.Instance.ChatRequest,
            OllamaJsonContext.Instance.ChatResponse, cancellationToken))
        {
            if (chunk.Message?.Content is { Length: > 0 } content)
            {
                yield return content;
            }
        }
    }

    /// <summary>チャット（一括）。</summary>
    public async Task<string> ChatCompleteAsync(
        string model, IReadOnlyList<OllamaMessage> messages, InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = false,
            Options = OllamaOptions.FromInferenceOptions(options),
        };

        var response = await PostJsonAsync<ChatRequest, ChatResponse>(
            "/api/chat", request, OllamaJsonContext.Instance.ChatRequest,
            OllamaJsonContext.Instance.ChatResponse, cancellationToken);

        return response.Message?.Content ?? "";
    }

    /// <summary>モデルをpullする（ストリーミング進捗）。</summary>
    public async IAsyncEnumerable<PullResponse> PullModelAsync(
        string modelTag,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new PullRequest { Name = modelTag, Stream = true };

        await foreach (var chunk in PostStreamAsync<PullRequest, PullResponse>(
            "/api/pull", request, OllamaJsonContext.Instance.PullRequest,
            OllamaJsonContext.Instance.PullResponse, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>ローカルモデル一覧を取得する。</summary>
    public async Task<TagsResponse> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync("/api/tags",
            OllamaJsonContext.Instance.TagsResponse, cancellationToken);
        return response ?? new TagsResponse();
    }

    /// <summary>Embeddingを取得する。</summary>
    public async Task<float[]> GetEmbeddingAsync(
        string model, string text, CancellationToken cancellationToken = default)
    {
        var request = new EmbedRequest { Model = model, Input = text };

        var response = await PostJsonAsync<EmbedRequest, EmbedResponse>(
            "/api/embed", request, OllamaJsonContext.Instance.EmbedRequest,
            OllamaJsonContext.Instance.EmbedResponse, cancellationToken);

        if (response.Embeddings.Count > 0 && response.Embeddings[0].Count > 0)
        {
            return response.Embeddings[0].ToArray();
        }

        return [];
    }

    /// <summary>モデルを削除する。</summary>
    public async Task DeleteModelAsync(string modelTag, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
        {
            Content = JsonContent.Create(new { name = modelTag }),
        };
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async IAsyncEnumerable<TResponse> PostStreamAsync<TRequest, TResponse>(
        string endpoint, TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var content = JsonContent.Create(request, requestTypeInfo);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var httpResponse = await _httpClient.SendAsync(httpRequest,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        httpResponse.EnsureSuccessStatusCode();

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        await foreach (string line in NdjsonStreamReader.ReadLinesAsync(reader, cancellationToken))
        {
            TResponse? item = JsonSerializer.Deserialize(line, responseTypeInfo);
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string endpoint, TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken = default)
    {
        // 非ストリーミングは長時間かかるため、HttpClient.Timeoutを無視してCancellationTokenのみで制御
        var content = JsonContent.Create(request, requestTypeInfo);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(30)); // 非ストリーミングは最大30分
        var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        if (!httpResponse.IsSuccessStatusCode)
        {
            string body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new OllamaApiException(
                $"Ollama API エラー: {httpResponse.StatusCode}",
                (int)httpResponse.StatusCode, body);
        }

        var response = await httpResponse.Content.ReadFromJsonAsync(responseTypeInfo, cancellationToken);
        return response ?? throw new OllamaApiException("Ollama APIからの応答が空です。");
    }
}
