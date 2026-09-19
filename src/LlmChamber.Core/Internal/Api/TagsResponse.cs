using System.Text.Json.Serialization;

namespace LlmChamber.Internal.Api;

internal sealed class TagsResponse : IOllamaApiResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("models")]
    public IReadOnlyList<TagModel> Models { get; init; } = [];
}

internal sealed class TagModel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset ModifiedAt { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }
}
