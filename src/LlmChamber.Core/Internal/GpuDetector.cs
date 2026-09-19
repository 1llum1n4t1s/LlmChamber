using System.Diagnostics;

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
    public static RuntimeVariant DetectRecommendedVariant()
        => DetectRecommendedVariant(PlatformInfo.GetCurrentOs(), RunCommand);

    internal static RuntimeVariant DetectRecommendedVariant(
        OsPlatform os,
        Func<string, string, string> commandRunner)
    {
        // RuntimeVariantの選択にNPU情報は使わないため、高コストなNPU列挙は行わない。
        var gpu = DetectGpu(os, includeNpu: false, commandRunner);
        if (gpu is null)
        {
            return RuntimeVariant.CpuOnly;
        }

        return gpu.Vendor.ToUpperInvariant() switch
        {
            "NVIDIA" or "NVIDIA CORPORATION" => RuntimeVariant.Full,
            "AMD" or "ADVANCED MICRO DEVICES" or "ADVANCED MICRO DEVICES, INC." => RuntimeVariant.Rocm,
            // Intel GPUはCUDA/ROCmに対応しないため、Ollamaではフル版(CUDA)でもCPUフォールバック可能
            "INTEL" or "INTEL CORPORATION" => RuntimeVariant.Full,
            _ => RuntimeVariant.CpuOnly,
        };
    }

    /// <summary>GPUハードウェア情報を検出する。</summary>
    public static GpuInfo? DetectGpu()
        => DetectGpu(PlatformInfo.GetCurrentOs(), includeNpu: true, RunCommand);

    internal static GpuInfo? DetectGpu(
        OsPlatform os,
        bool includeNpu,
        Func<string, string, string> commandRunner)
    {
        return os switch
        {
            OsPlatform.Windows => DetectGpuWindows(includeNpu, commandRunner),
            OsPlatform.Linux => DetectGpuLinux(commandRunner),
            OsPlatform.MacOS => DetectGpuMacOs(commandRunner),
            _ => null,
        };
    }

    private static GpuInfo? DetectGpuWindows(
        bool includeNpu,
        Func<string, string, string> commandRunner)
    {
        // PowerShell CIM (Get-CimInstance) を使用。wmic.exeはWin11で廃止済み。
        string output = commandRunner("powershell", "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object AdapterCompatibility,AdapterRAM,Name | ConvertTo-Csv -NoTypeInformation\"");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 詳細情報を求める経路でのみ、全PnPデバイスを1回列挙する。
        bool hasNpu = includeNpu && DetectNpu(commandRunner);
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

    private static GpuInfo? DetectGpuLinux(Func<string, string, string> commandRunner)
    {
        string output = commandRunner("lspci", "-nn");
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

        return null;
    }

    private static GpuInfo? DetectGpuMacOs(Func<string, string, string> commandRunner)
    {
        // macOSはApple Silicon (Metal) を使用。バリアント選択は不要（単一バイナリ）。
        string output = commandRunner("sysctl", "-n machdep.cpu.brand_string");
        bool isAppleSilicon = output.Contains("Apple", StringComparison.OrdinalIgnoreCase);
        return new GpuInfo(
            isAppleSilicon ? "Apple" : "Intel",
            output.Trim(),
            null,
            isAppleSilicon); // Apple SiliconのNeural EngineをNPUとして検出
    }

    private static bool DetectNpu(Func<string, string, string> commandRunner)
    {
        // Windows NPU検出: PowerShell CIM使用（wmic廃止対応）
        string output = commandRunner("powershell", "-NoProfile -Command \"Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'NPU|Neural' } | Select-Object -ExpandProperty Name\"");
        return output.Contains("NPU", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Neural", StringComparison.OrdinalIgnoreCase);
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
        if (!process.Start())
        {
            throw new InvalidOperationException($"'{fileName}' によるハードウェア検出を開始できませんでした。");
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ベストエフォート */ }
            throw new TimeoutException($"'{fileName}' によるハードウェア検出がタイムアウトしました。");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{fileName}' によるハードウェア検出に失敗しました (exit code: {process.ExitCode})。{Environment.NewLine}{error}".TrimEnd());
        }

        return output;
    }
}
