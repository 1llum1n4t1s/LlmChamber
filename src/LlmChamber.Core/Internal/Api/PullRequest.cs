using System.Text.Json.Serialization;

namespace LlmChamber.Internal.Api;

internal sealed class PullRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;
}

internal sealed class PullResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("total")]
    public long? Total { get; init; }

    [JsonPropertyName("completed")]
    public long? Completed { get; init; }
}
