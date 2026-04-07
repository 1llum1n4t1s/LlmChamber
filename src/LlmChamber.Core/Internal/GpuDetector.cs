using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LlmChamber.Internal;

/// <summary>GPU/NPUの検出情報。</summary>
internal sealed record GpuInfo(string Vendor, string Name, long? VramBytes, bool HasNpu);

/// <summary>
/// GPU/NPUの自動検出。
/// 検出結果に基づいて推奨RuntimeVariantを返す。
/// </summary>
internal static class GpuDetector
{
    /// <summary>推奨RuntimeVariantを検出する。</summary>
    public static RuntimeVariant DetectRecommendedVariant(ILogger? logger = null)
    {
        try
        {
            var gpu = DetectGpu(logger);
            if (gpu is null)
            {
                logger?.LogInformation("GPUが検出されませんでした。CPU-onlyモードを使用します。");
                return RuntimeVariant.CpuOnly;
            }

            logger?.LogInformation("GPU検出: {Vendor} {Name}", gpu.Vendor, gpu.Name);

            return gpu.Vendor.ToUpperInvariant() switch
            {
                "NVIDIA" or "NVIDIA CORPORATION" => RuntimeVariant.Full,
                "AMD" or "ADVANCED MICRO DEVICES" or "ADVANCED MICRO DEVICES, INC." => RuntimeVariant.Rocm,
                // Intel GPUはCUDA/ROCmに対応しないため、Ollamaではフル版(CUDA)でもCPUフォールバック可能
                "INTEL" or "INTEL CORPORATION" => RuntimeVariant.Full,
                _ => RuntimeVariant.CpuOnly,
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "GPU検出に失敗しました。CPU-onlyモードを使用します。");
            return RuntimeVariant.CpuOnly;
        }
    }

    /// <summary>GPUハードウェア情報を検出する。</summary>
    public static GpuInfo? DetectGpu(ILogger? logger = null)
    {
        var os = PlatformInfo.GetCurrentOs();
        return os switch
        {
            OsPlatform.Windows => DetectGpuWindows(logger),
            OsPlatform.Linux => DetectGpuLinux(logger),
            OsPlatform.MacOS => DetectGpuMacOs(logger),
            _ => null,
        };
    }

    private static GpuInfo? DetectGpuWindows(ILogger? logger)
    {
        // PowerShell CIM (Get-CimInstance) を使用。wmic.exeはWin11で廃止済み。
        try
        {
            string output = RunCommand("powershell", "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object AdapterCompatibility,AdapterRAM,Name | ConvertTo-Csv -NoTypeInformation\"");
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            GpuInfo? firstGpu = null;

            foreach (string line in lines.Skip(1)) // CSVヘッダーをスキップ
            {
                // CSVパース（引用符あり）
                string[] parts = ParseCsvLine(line);
                if (parts.Length < 3) continue;

                string vendor = parts[0].Trim('"', ' ');
                long.TryParse(parts[1].Trim('"', ' '), out long vram);
                string name = parts[2].Trim('"', ' ');

                if (string.IsNullOrEmpty(vendor)) continue;

                var gpuInfo = new GpuInfo(vendor, name, vram > 0 ? vram : null, DetectNpu(logger));

                // 専用GPUを優先
                if (IsDiscreteGpu(vendor, name))
                    return gpuInfo;

                firstGpu ??= gpuInfo;
            }

            return firstGpu;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Windows GPU検出エラー (PowerShell CIM)");
        }
        return null;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuote = false;
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            else if (line[i] == ',' && !inQuote)
            {
                result.Add(line[start..i]);
                start = i + 1;
            }
        }
        result.Add(line[start..]);
        return result.ToArray();
    }

    private static GpuInfo? DetectGpuLinux(ILogger? logger)
    {
        try
        {
            string output = RunCommand("lspci", "-nn");
            foreach (string line in output.Split('\n'))
            {
                if (!line.Contains("VGA", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("3D controller", StringComparison.OrdinalIgnoreCase))
                    continue;

                string vendor = line.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA" :
                                line.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD" :
                                line.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Unknown";

                return new GpuInfo(vendor, line.Trim(), null, false);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Linux GPU検出エラー");
        }
        return null;
    }

    private static GpuInfo? DetectGpuMacOs(ILogger? logger)
    {
        // macOSはApple Silicon (Metal) を使用。バリアント選択は不要（単一バイナリ）。
        try
        {
            string output = RunCommand("sysctl", "-n machdep.cpu.brand_string");
            bool isAppleSilicon = output.Contains("Apple", StringComparison.OrdinalIgnoreCase);
            return new GpuInfo(
                isAppleSilicon ? "Apple" : "Intel",
                output.Trim(),
                null,
                isAppleSilicon); // Apple SiliconのNeural EngineをNPUとして検出
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "macOS GPU検出エラー");
        }
        return null;
    }

    private static bool DetectNpu(ILogger? logger)
    {
        // Windows NPU検出: PowerShell CIM使用（wmic廃止対応）
        try
        {
            string output = RunCommand("powershell", "-NoProfile -Command \"Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'NPU|Neural' } | Select-Object -ExpandProperty Name\"");
            return output.Contains("NPU", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("Neural", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDiscreteGpu(string vendor, string name)
    {
        string v = vendor.ToUpperInvariant();
        return v.Contains("NVIDIA") || v.Contains("AMD") || v.Contains("ADVANCED MICRO");
    }

    private static string RunCommand(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }
}
