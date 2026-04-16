using System.Runtime.InteropServices;

namespace LlmChamber.Internal;

/// <summary>OS/アーキテクチャの検出。</summary>
internal static class PlatformInfo
{
    /// <summary>現在のOSを取得する。</summary>
    public static OsPlatform GetCurrentOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OsPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OsPlatform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OsPlatform.MacOS;
        throw new UnsupportedPlatformException(
            $"サポートされていないOS: {RuntimeInformation.OSDescription}",
            detectedOs: RuntimeInformation.OSDescription);
    }

    /// <summary>現在のCPUアーキテクチャを取得する。</summary>
    public static CpuArchitecture GetCurrentArchitecture()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => CpuArchitecture.X64,
            Architecture.Arm64 => CpuArchitecture.Arm64,
            _ => throw new UnsupportedPlatformException(
                $"サポートされていないアーキテクチャ: {RuntimeInformation.OSArchitecture}",
                detectedArchitecture: RuntimeInformation.OSArchitecture.ToString()),
        };
    }

    /// <summary>
    /// Ollamaバイナリのダウンロードファイル名を取得する。
    /// </summary>
    public static (string FileName, string Extension) GetOllamaBinaryInfo(
        OsPlatform os, CpuArchitecture arch, RuntimeVariant variant)
    {
        return os switch
        {
            OsPlatform.Windows => GetDesktopBinaryInfo("windows", ".zip", arch, variant),
            OsPlatform.Linux => GetDesktopBinaryInfo("linux", ".tar.zst", arch, variant),
            OsPlatform.MacOS => ("ollama-darwin", ".tgz"),
            _ => throw new UnsupportedPlatformException(
                $"サポートされていないプラットフォーム: {os}/{arch}",
                detectedOs: os.ToString(),
                detectedArchitecture: arch.ToString()),
        };
    }

    private static (string, string) GetDesktopBinaryInfo(
        string osName, string extension, CpuArchitecture arch, RuntimeVariant variant)
    {
        string archStr = arch == CpuArchitecture.Arm64 ? "arm64" : "amd64";
        string suffix = variant == RuntimeVariant.Rocm ? "-rocm" : "";
        return ($"ollama-{osName}-{archStr}{suffix}", extension);
    }

    /// <summary>Ollamaのダウンロード先ファイル名（拡張子込み）を取得する。</summary>
    public static string GetDownloadFileName(OsPlatform os, CpuArchitecture arch, RuntimeVariant variant)
    {
        var (name, ext) = GetOllamaBinaryInfo(os, arch, variant);
        return name + ext;
    }

    /// <summary>Ollamaの実行ファイル名を取得する。</summary>
    public static string GetOllamaExecutableName(OsPlatform os)
    {
        return os == OsPlatform.Windows ? "ollama.exe" : "ollama";
    }
}

internal enum OsPlatform
{
    Windows,
    Linux,
    MacOS,
}

internal enum CpuArchitecture
{
    X64,
    Arm64,
}
