using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LlmChamber.Internal.Api;

namespace LlmChamber.Internal;

/// <summary>
/// Ollama HTTP APIクライアント。
/// ストリーミング応答をIAsyncEnumerableで返す。
/// </summary>
internal sealed class OllamaApiClient
{
    private readonly HttpClient _httpClient;
    private Uri? _baseUri;

    public OllamaApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _baseUri = httpClient.BaseAddress;
    }

    /// <summary>APIのベースアドレスを設定する。</summary>
    public void SetBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Ollama APIのベースURLには絶対HTTP(S) URLを指定してください。", nameof(baseUrl));
        }

        Volatile.Write(ref _baseUri, baseUri);
    }

    /// <summary>Ollamaのバージョンを取得する。</summary>
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetJsonAsync("/api/version",
            OllamaJsonContext.Instance.VersionResponse, cancellationToken);
        return response.Version;
    }

    /// <summary>テキスト生成（ストリーミング）。</summary>
    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string model, string prompt, InferenceOptions? options = null,
        IReadOnlyList<byte[]>? images = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new GenerateRequest
        {
            Model = model,
            Prompt = prompt,
            Stream = true,
            Options = OllamaOptions.FromInferenceOptions(options),
            Images = EncodeImages(images),
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
        IReadOnlyList<byte[]>? images = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GenerateRequest
        {
            Model = model,
            Prompt = prompt,
            Stream = false,
            Options = OllamaOptions.FromInferenceOptions(options),
            Images = EncodeImages(images),
        };

        var response = await PostJsonAsync<GenerateRequest, GenerateResponse>(
            "/api/generate", request, OllamaJsonContext.Instance.GenerateRequest,
            OllamaJsonContext.Instance.GenerateResponse, cancellationToken);

        return response.Response;
    }

    /// <summary>byte[] 画像を Ollama API が要求する Base64 文字列に変換する。</summary>
    internal static IReadOnlyList<string>? EncodeImages(IReadOnlyList<byte[]>? images)
    {
        if (images is null || images.Count == 0) return null;
        var encoded = new List<string>(images.Count);
        foreach (var image in images)
        {
            if (image is null || image.Length == 0) continue;
            encoded.Add(Convert.ToBase64String(image));
        }
        return encoded.Count > 0 ? encoded : null;
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
        bool completed = false;

        await foreach (var chunk in PostStreamAsync<PullRequest, PullResponse>(
            "/api/pull", request, OllamaJsonContext.Instance.PullRequest,
            OllamaJsonContext.Instance.PullResponse, cancellationToken))
        {
            completed |= string.Equals(chunk.Status, "success", StringComparison.OrdinalIgnoreCase);
            yield return chunk;
        }

        if (!completed)
        {
            throw new OllamaApiException(
                "Ollama APIのモデル取得ストリームが正常完了通知の前に終了しました。",
                statusCode: 200);
        }
    }

    /// <summary>ローカルモデル一覧を取得する。</summary>
    public async Task<TagsResponse> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync("/api/tags",
            OllamaJsonContext.Instance.TagsResponse, cancellationToken);
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
        var deleteRequest = new DeleteRequest { Name = modelTag };
        using var request = new HttpRequestMessage(HttpMethod.Delete, CreateRequestUri("/api/delete"))
        {
            Content = JsonContent.Create(deleteRequest, OllamaJsonContext.Instance.DeleteRequest),
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ThrowIfHttpErrorAsync(response, cancellationToken);
    }

    private async IAsyncEnumerable<TResponse> PostStreamAsync<TRequest, TResponse>(
        string endpoint, TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TResponse : IOllamaApiResponse
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CreateRequestUri(endpoint))
        {
            Content = JsonContent.Create(request, requestTypeInfo),
        };
        using var httpResponse = await _httpClient.SendAsync(httpRequest,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        await ThrowIfHttpErrorAsync(httpResponse, cancellationToken);

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        await foreach (string line in NdjsonStreamReader.ReadLinesAsync(reader, cancellationToken))
        {
            TResponse? item = JsonSerializer.Deserialize(line, responseTypeInfo);
            if (item is not null)
            {
                ThrowIfApiError(item, (int)httpResponse.StatusCode, line);
                yield return item;
            }
        }
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string endpoint, TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken = default)
        where TResponse : IOllamaApiResponse
    {
        // 標準クライアントのTimeoutは無制限。ここでは非ストリーミング要求に30分の上限を追加する。
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CreateRequestUri(endpoint))
        {
            Content = JsonContent.Create(request, requestTypeInfo),
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(30)); // 非ストリーミングは最大30分
        using var httpResponse = await _httpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        await ThrowIfHttpErrorAsync(httpResponse, cts.Token);

        string body = await httpResponse.Content.ReadAsStringAsync(cts.Token);
        TResponse? response = JsonSerializer.Deserialize(body, responseTypeInfo);
        if (response is null)
            throw new OllamaApiException("Ollama APIからの応答が空です。", (int)httpResponse.StatusCode, body);

        ThrowIfApiError(response, (int)httpResponse.StatusCode, body);
        return response;
    }

    private async Task<TResponse> GetJsonAsync<TResponse>(
        string endpoint,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
        where TResponse : IOllamaApiResponse
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateRequestUri(endpoint));
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        await ThrowIfHttpErrorAsync(response, cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        TResponse? value = JsonSerializer.Deserialize(body, responseTypeInfo);
        if (value is null)
            throw new OllamaApiException("Ollama APIからの応答が空です。", (int)response.StatusCode, body);

        ThrowIfApiError(value, (int)response.StatusCode, body);
        return value;
    }

    private Uri CreateRequestUri(string endpoint)
    {
        Uri baseUri = Volatile.Read(ref _baseUri)
            ?? throw new InvalidOperationException("Ollama APIのベースURLが設定されていません。");
        return new Uri(baseUri, endpoint);
    }

    private static async Task ThrowIfHttpErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new OllamaApiException(
            $"Ollama API エラー: {response.StatusCode}",
            (int)response.StatusCode,
            body);
    }

    private static void ThrowIfApiError(
        IOllamaApiResponse response,
        int statusCode,
        string responseBody)
    {
        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            throw new OllamaApiException(
                $"Ollama API エラー: {response.Error}",
                statusCode,
                responseBody);
        }
    }
}
