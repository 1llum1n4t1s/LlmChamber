using System.Text.Json.Serialization;

namespace LlmChamber.Internal.Api;

/// <summary>Ollama API共通のオプションパラメータ。</summary>
internal sealed class OllamaOptions
{
    [JsonPropertyName("num_predict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NumPredict { get; init; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; init; }

    [JsonPropertyName("top_k")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopK { get; init; }

    [JsonPropertyName("repeat_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? RepeatPenalty { get; init; }

    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; init; }

    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Stop { get; init; }

    /// <summary>InferenceOptionsからOllamaOptionsに変換する。</summary>
    public static OllamaOptions? FromInferenceOptions(InferenceOptions? options)
    {
        if (options is null) return null;

        return new OllamaOptions
        {
            NumPredict = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            RepeatPenalty = options.RepeatPenalty,
            Seed = options.Seed,
            Stop = options.StopSequences,
        };
    }
}
