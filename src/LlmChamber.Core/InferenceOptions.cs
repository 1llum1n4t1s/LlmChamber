namespace LlmChamber;

/// <summary>推論パラメータ。</summary>
public sealed record InferenceOptions
{
    /// <summary>最大生成トークン数。nullの場合はモデルのデフォルト値。</summary>
    public int? MaxTokens { get; init; }

    /// <summary>サンプリング温度（0.0〜2.0）。高いほどランダム。</summary>
    public float? Temperature { get; init; }

    /// <summary>Top-Pサンプリング（0.0〜1.0）。</summary>
    public float? TopP { get; init; }

    /// <summary>Top-Kサンプリング。</summary>
    public int? TopK { get; init; }

    /// <summary>繰り返しペナルティ（1.0以上）。</summary>
    public float? RepeatPenalty { get; init; }

    /// <summary>乱数シード。再現性のために固定値を指定可能。</summary>
    public int? Seed { get; init; }

    /// <summary>生成を停止するシーケンス。</summary>
    public IReadOnlyList<string>? StopSequences { get; init; }
}
