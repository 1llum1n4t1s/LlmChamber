using System.Text.Json.Serialization;

namespace LlmChamber.Internal.Api;

internal sealed class DeleteRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}
