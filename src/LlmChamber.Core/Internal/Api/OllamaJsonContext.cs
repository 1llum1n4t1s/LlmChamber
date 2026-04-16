using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmChamber.Internal.Api;

/// <summary>System.Text.Json Source Generator コンテキスト。</summary>
[JsonSerializable(typeof(GenerateRequest))]
[JsonSerializable(typeof(GenerateResponse))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(PullResponse))]
[JsonSerializable(typeof(TagsResponse))]
[JsonSerializable(typeof(EmbedRequest))]
[JsonSerializable(typeof(EmbedResponse))]
[JsonSerializable(typeof(OllamaMessage))]
[JsonSerializable(typeof(OllamaOptions))]
[JsonSerializable(typeof(DeleteRequest))]
[JsonSerializable(typeof(VersionResponse))]
internal partial class OllamaJsonContext : JsonSerializerContext
{
    private static readonly Lazy<OllamaJsonContext> _default = new(() =>
        new OllamaJsonContext(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        }));

    /// <summary>デフォルトのインスタンス（snake_case）。</summary>
    public static OllamaJsonContext Instance => _default.Value;
}

internal sealed class VersionResponse
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";
}
