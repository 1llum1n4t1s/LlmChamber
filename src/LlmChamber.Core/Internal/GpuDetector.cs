using System.Diagnostics;
using SuperLightLogger;

namespace LlmChamber.Internal;

/// <summary>GPU/NPUの検出情報。</summary>
internal sealed record GpuInfo(string Vendor, string Name, long? VramBytes, bool HasNpu);

/// <summary>
/// GPU/NPUの自動検出。
/// 検出結果に基づいて推奨RuntimeVariantを返す。
/// </summary>
internal static class GpuDetector
{
    private static readonly ILog _logger = LogManager.GetLogger(typeof(GpuDetector));

    /// <summary>推奨RuntimeVariantを検出する。</summary>
    public static RuntimeVariant DetectRecommendedVariant()
    {
        try
        {
            var gpu = DetectGpu();
            if (gpu is null)
            {
                _logger.Info("GPUが検出されませんでした。CPU-onlyモードを使用します。");
                return RuntimeVariant.CpuOnly;
            }

            _logger.Info($"GPU検出: {gpu.Vendor} {gpu.Name}");

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
            _logger.Warn($"GPU検出に失敗しました。CPU-onlyモードを使用します。: {ex.Message}");
            return RuntimeVariant.CpuOnly;
        }
    }

    /// <summary>GPUハードウェア情報を検出する。</summary>
    public static GpuInfo? DetectGpu()
    {
        var os = PlatformInfo.GetCurrentOs();
        return os switch
        {
            OsPlatform.Windows => DetectGpuWindows(),
            OsPlatform.Linux => DetectGpuLinux(),
            OsPlatform.MacOS => DetectGpuMacOs(),
            _ => null,
        };
    }

    private static GpuInfo? DetectGpuWindows()
    {
        // PowerShell CIM (Get-CimInstance) を使用。wmic.exeはWin11で廃止済み。
        try
        {
            string output = RunCommand("powershell", "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object AdapterCompatibility,AdapterRAM,Name | ConvertTo-Csv -NoTypeInformation\"");
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // NPU検出は高コスト（全PnPデバイス列挙）のため1回だけ実行
            bool hasNpu = DetectNpu();
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

                var gpuInfo = new GpuInfo(vendor, name, vram > 0 ? vram : null, hasNpu);

                // 専用GPUを優先
                if (IsDiscreteGpu(vendor))
                    return gpuInfo;

                firstGpu ??= gpuInfo;
            }

            return firstGpu;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Windows GPU検出エラー (PowerShell CIM): {ex.Message}");
        }
        return null;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var field = new System.Text.StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuote)
            {
                if (c == '"')
                {
                    // RFC 4180: "" はエスケープされた " として扱う
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++; // 次の " をスキップ
                    }
                    else
                    {
                        inQuote = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuote = true;
                }
                else if (c == ',')
                {
                    result.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
        }
        result.Add(field.ToString());
        return result.ToArray();
    }

    private static GpuInfo? DetectGpuLinux()
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
            _logger.Debug($"Linux GPU検出エラー: {ex.Message}");
        }
        return null;
    }

    private static GpuInfo? DetectGpuMacOs()
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
            _logger.Debug($"macOS GPU検出エラー: {ex.Message}");
        }
        return null;
    }

    private static bool DetectNpu()
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

    private static bool IsDiscreteGpu(string vendor)
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
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(); } catch { /* ベストエフォート */ }
            return output;
        }
        return output;
    }
}
