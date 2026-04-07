namespace LlmChamber;

/// <summary>
/// Ollamaランタイムバイナリのバリアント。
/// GPU/NPUの種類に応じて最適なバイナリを選択する。
/// </summary>
public enum RuntimeVariant
{
    /// <summary>自動検出: GPU/NPUを検出して最適なバイナリを選択</summary>
    Auto = 0,

    /// <summary>CUDA対応フルバイナリ（Nvidia GPU向け、~1.9GB Win x64）</summary>
    Full,

    /// <summary>AMD ROCm対応バイナリ（AMD GPU向け、~331MB Win x64）</summary>
    Rocm,

    /// <summary>CPU-only（GPUなし環境向け）</summary>
    CpuOnly,
}
