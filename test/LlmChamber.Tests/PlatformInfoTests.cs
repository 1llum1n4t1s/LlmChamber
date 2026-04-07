using LlmChamber.Internal;
using Xunit;

namespace LlmChamber.Tests;

public class PlatformInfoTests
{
    [Fact]
    public void GetCurrentOs_ReturnsValidPlatform()
    {
        var os = PlatformInfo.GetCurrentOs();
        Assert.True(os is OsPlatform.Windows or OsPlatform.Linux or OsPlatform.MacOS);
    }

    [Fact]
    public void GetCurrentArchitecture_ReturnsValidArch()
    {
        var arch = PlatformInfo.GetCurrentArchitecture();
        Assert.True(arch is CpuArchitecture.X64 or CpuArchitecture.Arm64);
    }

    [Fact]
    public void GetDownloadFileName_WindowsX64Full()
    {
        Assert.Equal("ollama-windows-amd64.zip",
            PlatformInfo.GetDownloadFileName(OsPlatform.Windows, CpuArchitecture.X64, RuntimeVariant.Full));
    }

    [Fact]
    public void GetDownloadFileName_WindowsX64Rocm()
    {
        Assert.Equal("ollama-windows-amd64-rocm.zip",
            PlatformInfo.GetDownloadFileName(OsPlatform.Windows, CpuArchitecture.X64, RuntimeVariant.Rocm));
    }

    [Fact]
    public void GetDownloadFileName_LinuxX64Full()
    {
        Assert.Equal("ollama-linux-amd64.tar.zst",
            PlatformInfo.GetDownloadFileName(OsPlatform.Linux, CpuArchitecture.X64, RuntimeVariant.Full));
    }

    [Fact]
    public void GetDownloadFileName_MacOSArm64()
    {
        Assert.Equal("ollama-darwin.tgz",
            PlatformInfo.GetDownloadFileName(OsPlatform.MacOS, CpuArchitecture.Arm64, RuntimeVariant.Full));
    }

    [Fact]
    public void GetOllamaExecutableName_Windows()
    {
        Assert.Equal("ollama.exe", PlatformInfo.GetOllamaExecutableName(OsPlatform.Windows));
    }

    [Fact]
    public void GetOllamaExecutableName_Linux()
    {
        Assert.Equal("ollama", PlatformInfo.GetOllamaExecutableName(OsPlatform.Linux));
    }
}
