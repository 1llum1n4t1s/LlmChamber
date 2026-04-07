using System.Text.Json.Serialization;

namespace LlmChamber.Internal.Api;

internal sealed class ChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaMessage> Messages { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OllamaOptions? Options { get; init; }
}

internal sealed class ChatResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; init; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }
}

internal sealed class OllamaMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}
