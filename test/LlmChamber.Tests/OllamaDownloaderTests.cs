using LlmChamber.Internal;
using Xunit;

namespace LlmChamber.Tests;

public class OllamaDownloaderTests
{
    [Fact]
    public void BuildDownloadUrl_WindowsX64Full()
    {
        string url = OllamaDownloader.BuildDownloadUrl("0.20.2", OsPlatform.Windows, CpuArchitecture.X64, RuntimeVariant.Full);
        Assert.Equal("https://github.com/ollama/ollama/releases/download/v0.20.2/ollama-windows-amd64.zip", url);
    }

    [Fact]
    public void BuildDownloadUrl_WindowsX64Rocm()
    {
        string url = OllamaDownloader.BuildDownloadUrl("0.20.2", OsPlatform.Windows, CpuArchitecture.X64, RuntimeVariant.Rocm);
        Assert.Equal("https://github.com/ollama/ollama/releases/download/v0.20.2/ollama-windows-amd64-rocm.zip", url);
    }

    [Fact]
    public void BuildDownloadUrl_LinuxX64()
    {
        string url = OllamaDownloader.BuildDownloadUrl("0.20.2", OsPlatform.Linux, CpuArchitecture.X64, RuntimeVariant.Full);
        Assert.Equal("https://github.com/ollama/ollama/releases/download/v0.20.2/ollama-linux-amd64.tar.zst", url);
    }

    [Fact]
    public void BuildDownloadUrl_MacOS()
    {
        string url = OllamaDownloader.BuildDownloadUrl("0.20.2", OsPlatform.MacOS, CpuArchitecture.Arm64, RuntimeVariant.Full);
        Assert.Equal("https://github.com/ollama/ollama/releases/download/v0.20.2/ollama-darwin.tgz", url);
    }

    [Fact]
    public void FindExistingBinary_NoVersionMarker_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var httpClient = new System.Net.Http.HttpClient();
            var downloader = new OllamaDownloader(httpClient,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaDownloader>.Instance);

            string? result = downloader.FindExistingBinary(tempDir);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindExistingBinary_WithMatchingVersion_ReturnsBinaryPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            string runtimeDir = Path.Combine(tempDir, "runtime");
            Directory.CreateDirectory(runtimeDir);

            File.WriteAllText(Path.Combine(tempDir, ".version"), "0.20.2");
            string execName = PlatformInfo.GetOllamaExecutableName(PlatformInfo.GetCurrentOs());
            File.WriteAllText(Path.Combine(runtimeDir, execName), "dummy");

            var httpClient = new System.Net.Http.HttpClient();
            var downloader = new OllamaDownloader(httpClient,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaDownloader>.Instance);

            string? result = downloader.FindExistingBinary(tempDir, "0.20.2");
            Assert.NotNull(result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindExistingBinary_VersionMismatch_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            string runtimeDir = Path.Combine(tempDir, "runtime");
            Directory.CreateDirectory(runtimeDir);

            File.WriteAllText(Path.Combine(tempDir, ".version"), "0.19.0");
            string execName = PlatformInfo.GetOllamaExecutableName(PlatformInfo.GetCurrentOs());
            File.WriteAllText(Path.Combine(runtimeDir, execName), "dummy");

            var httpClient = new System.Net.Http.HttpClient();
            var downloader = new OllamaDownloader(httpClient,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaDownloader>.Instance);

            string? result = downloader.FindExistingBinary(tempDir, "0.20.2");
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
