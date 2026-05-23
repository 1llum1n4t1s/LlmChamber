namespace LlmChamber.Internal;

/// <summary>組込みモデルプリセット定義。</summary>
internal static class OllamaModels
{
    public static readonly IReadOnlyList<ModelPreset> Presets =
    [
        new ModelPreset
        {
            Id = "gemma4-e2b",
            OllamaTag = "gemma4:e2b",
            DisplayName = "Gemma 4 E2B",
            Family = "Gemma 4",
            ApproximateDownloadSize = 3L * 1024 * 1024 * 1024, // ~3GB
            RecommendedMinRam = 5L * 1024 * 1024 * 1024, // 5GB
            DefaultInferenceOptions = new InferenceOptions
            {
                Temperature = 1.0f,
                TopP = 0.95f,
                TopK = 64,
            },
            Description = "最軽量のエッジモデル。CPU推論に最適。マルチモーダル対応。",
        },
        new ModelPreset
        {
            Id = "gemma4-e4b",
            OllamaTag = "gemma4:e4b",
            DisplayName = "Gemma 4 E4B",
            Family = "Gemma 4",
            ApproximateDownloadSize = 5L * 1024 * 1024 * 1024, // ~5GB
            RecommendedMinRam = 8L * 1024 * 1024 * 1024, // 8GB
            DefaultInferenceOptions = new InferenceOptions
            {
                Temperature = 1.0f,
                TopP = 0.95f,
                TopK = 64,
            },
            Description = "中型エッジモデル。音声入力対応。バランスの取れた性能。",
        },
        new ModelPreset
        {
            Id = "qwen3.5-2b",
            OllamaTag = "qwen3:2b",
            DisplayName = "Qwen 3.5 2B",
            Family = "Qwen 3.5",
            ApproximateDownloadSize = 2L * 1024 * 1024 * 1024, // ~2GB
            RecommendedMinRam = 4L * 1024 * 1024 * 1024, // 4GB
            DefaultInferenceOptions = new InferenceOptions
            {
                Temperature = 0.7f,
                TopP = 0.9f,
                TopK = 50,
            },
            Description = "日本語・多言語が特に優秀な軽量モデル。",
        },
        new ModelPreset
        {
            Id = "phi4-mini",
            OllamaTag = "phi4-mini",
            DisplayName = "Phi-4 Mini",
            Family = "Phi-4",
            ApproximateDownloadSize = 3L * 1024 * 1024 * 1024, // ~3GB
            RecommendedMinRam = 6L * 1024 * 1024 * 1024, // 6GB
            DefaultInferenceOptions = new InferenceOptions
            {
                Temperature = 0.7f,
                TopP = 0.9f,
                TopK = 50,
            },
            Description = "数学・コーディングに強い小型モデル。",
        },
        // ── Multimodal Vision プリセット ──
        new ModelPreset
        {
            Id = "gemma3-4b",
            OllamaTag = "gemma3:4b",
            DisplayName = "Gemma 3 4B (Vision)",
            Family = "Gemma 3",
            ApproximateDownloadSize = 3L * 1024 * 1024 * 1024, // ~3GB
            RecommendedMinRam = 6L * 1024 * 1024 * 1024, // 6GB
            DefaultInferenceOptions = new InferenceOptions
            {
                Temperature = 0.7f,
                TopP = 0.95f,
                TopK = 64,
            },
            Description = "Google製マルチモーダル。画像+テキスト入力に対応。",
        },
        new ModelPreset
        {
            Id = "qwen2.5vl-3b",
            OllamaTag = "qwen2.5vl:3b",
            DisplayName = "Qwen 2.5 VL 3B (Vision)",
            Family = "Qwen 2.5 VL",
            ApproximateDownloadSize = 3L * 1024 * 1024 * 1024, // ~3GB
            RecommendedMinRam = 6L * 1024 * 1024 * 1024, // 6GB
            DefaultInferenceOptions = new InferenceOptions
            {
                Temperature = 0.7f,
                TopP = 0.9f,
                TopK = 50,
            },
            Description = "Alibaba製軽量Visionモデル。画像理解とOCRに強い。",
        },
        new ModelPreset
        {
            Id = "llava-7b",
            OllamaTag = "llava:7b",
            DisplayName = "LLaVA 7B (Vision)",
            Family = "LLaVA",
            ApproximateDownloadSize = 4L * 1024 * 1024 * 1024, // ~4GB
            RecommendedMinRam = 8L * 1024 * 1024 * 1024, // 8GB
            DefaultInferenceOptions = new InferenceOptions
            {
                Temperature = 0.7f,
                TopP = 0.9f,
                TopK = 50,
            },
            Description = "定番Vision LLM。汎用的な画像説明・VQA向け。",
        },
    ];

    /// <summary>プリセットIDまたはOllamaタグからプリセットを取得する。</summary>
    public static ModelPreset? FindPreset(string idOrTag)
    {
        return Presets.FirstOrDefault(p =>
            string.Equals(p.Id, idOrTag, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.OllamaTag, idOrTag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>プリセットIDまたはOllamaタグからOllamaタグを解決する。</summary>
    public static string ResolveModelTag(string idOrTag)
    {
        var preset = FindPreset(idOrTag);
        return preset?.OllamaTag ?? idOrTag;
    }
}
