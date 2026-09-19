using System.Diagnostics;
using LlmChamber.Internal;
using LlmChamber.Internal.Media;
using LlmChamber.Media;
using Xunit;

namespace LlmChamber.Tests;

public class RuntimeProcessHardeningTests
{
    [Fact]
    public void CreateTarZstdStartInfo_PreservesArgumentsWithSpaces()
    {
        const string archivePath = "/tmp/archive path/ollama.tar.zst";
        const string extractDirectory = "/tmp/extract path";

        ProcessStartInfo startInfo = OllamaDownloader.CreateTarZstdStartInfo(archivePath, extractDirectory);

        Assert.Equal("tar", startInfo.FileName);
        Assert.Equal(["--zstd", "-xf", archivePath, "-C", extractDirectory], startInfo.ArgumentList);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public async Task WaitForTarProcessAsync_NonZeroExit_IncludesStandardError()
    {
        using Process process = StartShellProcess("tar-test-error", exitCode: 7, keepRunning: false);

        RuntimeInstallException exception = await Assert.ThrowsAsync<RuntimeInstallException>(
            () => OllamaDownloader.WaitForTarProcessAsync(process, "archive.tar.zst", CancellationToken.None));

        Assert.Contains("exit 7", exception.Message, StringComparison.Ordinal);
        Assert.Contains("tar-test-error", exception.Message, StringComparison.Ordinal);
        Assert.Equal("archive.tar.zst", exception.ArchivePath);
    }

    [Fact]
    public async Task WaitForTarProcessAsync_Cancellation_KillsProcess()
    {
        using Process process = StartShellProcess(errorText: null, exitCode: 0, keepRunning: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OllamaDownloader.WaitForTarProcessAsync(process, "archive.tar.zst", cancellation.Token));

        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task FFmpegWaitForTarProcessAsync_NonZeroExit_IncludesStandardError()
    {
        using Process process = StartShellProcess("ffmpeg-tar-error", exitCode: 9, keepRunning: false);

        MediaException exception = await Assert.ThrowsAsync<MediaException>(
            () => FFmpegBinaryDownloader.WaitForTarProcessAsync(process, CancellationToken.None));

        Assert.Contains("exit 9", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ffmpeg-tar-error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FFmpegWaitForTarProcessAsync_Cancellation_KillsProcess()
    {
        using Process process = StartShellProcess(errorText: null, exitCode: 0, keepRunning: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FFmpegBinaryDownloader.WaitForTarProcessAsync(process, cancellation.Token));

        Assert.True(process.HasExited);
    }

    [Fact]
    public void DetectRecommendedVariant_Windows_DoesNotProbeNpu()
    {
        var commands = new List<string>();

        RuntimeVariant variant = GpuDetector.DetectRecommendedVariant(
            OsPlatform.Windows,
            (_, arguments) =>
            {
                commands.Add(arguments);
                return "\"AdapterCompatibility\",\"AdapterRAM\",\"Name\"\n" +
                       "\"NVIDIA Corporation\",\"8589934592\",\"Test GPU\"\n";
            });

        Assert.Equal(RuntimeVariant.Full, variant);
        Assert.Single(commands);
        Assert.Contains("Win32_VideoController", commands[0], StringComparison.Ordinal);
        Assert.DoesNotContain(commands, command => command.Contains("Win32_PnPEntity", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectGpu_Windows_NpuProbeFailurePropagates()
    {
        var commands = new List<string>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            GpuDetector.DetectGpu(
                OsPlatform.Windows,
                includeNpu: true,
                (_, arguments) =>
                {
                    commands.Add(arguments);
                    if (arguments.Contains("Win32_PnPEntity", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("NPU probe failed");
                    }

                    return "\"AdapterCompatibility\",\"AdapterRAM\",\"Name\"\n" +
                           "\"NVIDIA Corporation\",\"8589934592\",\"Test GPU\"\n";
                }));

        Assert.Equal("NPU probe failed", exception.Message);
        Assert.Equal(2, commands.Count);
    }

    private static Process StartShellProcess(string? errorText, int exitCode, bool keepRunning)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "powershell";
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(keepRunning
                ? "Start-Sleep -Seconds 30"
                : $"[Console]::Error.Write('{errorText}'); exit {exitCode}");
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(keepRunning
                ? "sleep 30"
                : $"printf '%s' '{errorText}' >&2; exit {exitCode}");
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("検証用プロセスを起動できませんでした。");
    }
}
