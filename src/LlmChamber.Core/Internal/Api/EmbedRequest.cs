using System.Text.Json.Serialization;

namespace LlmChamber.Internal.Api;

internal sealed class EmbedRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required string Input { get; init; }
}

internal sealed class EmbedResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("embeddings")]
    public IReadOnlyList<IReadOnlyList<float>> Embeddings { get; init; } = [];
}
