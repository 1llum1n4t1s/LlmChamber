using LlmChamber.Internal;
using LlmChamber.Internal.Media;
using LlmChamber.Media;
using Xunit;

namespace LlmChamber.Tests;

/// <summary>Media (Video) オプトイン機能のユニットテスト（ネットワーク不要）。</summary>
public class MediaTests
{
    // ── FFmpeg Binary Downloader ──
    // 注: OsPlatform/CpuArchitecture が internal なため、テストパラメータには int を使う

    [Theory]
    [InlineData((int)OsPlatform.Windows, (int)CpuArchitecture.X64, "ffmpeg-master-latest-win64-gpl.zip", ".zip")]
    [InlineData((int)OsPlatform.Linux, (int)CpuArchitecture.X64, "ffmpeg-master-latest-linux64-gpl.tar.xz", ".tar.xz")]
    [InlineData((int)OsPlatform.Linux, (int)CpuArchitecture.Arm64, "ffmpeg-master-latest-linuxarm64-gpl.tar.xz", ".tar.xz")]
    public void FFmpegBinaryDownloader_KnownPlatform_ReturnsAsset(int osIdx, int archIdx, string expectedFile, string expectedExt)
    {
        var result = FFmpegBinaryDownloader.GetReleaseAsset((OsPlatform)osIdx, (CpuArchitecture)archIdx);
        Assert.NotNull(result);
        Assert.Equal(expectedFile, result.Value.FileName);
        Assert.Equal(expectedExt, result.Value.Extension);
    }

    [Theory]
    [InlineData((int)OsPlatform.MacOS, (int)CpuArchitecture.X64)]
    [InlineData((int)OsPlatform.MacOS, (int)CpuArchitecture.Arm64)]
    public void FFmpegBinaryDownloader_UnsupportedPlatform_ReturnsNull(int osIdx, int archIdx)
    {
        var result = FFmpegBinaryDownloader.GetReleaseAsset((OsPlatform)osIdx, (CpuArchitecture)archIdx);
        Assert.Null(result);
    }

    [Theory]
    [InlineData((int)OsPlatform.Windows, "ffmpeg.exe")]
    [InlineData((int)OsPlatform.Linux, "ffmpeg")]
    [InlineData((int)OsPlatform.MacOS, "ffmpeg")]
    public void FFmpegBinaryDownloader_ExecutableName_MatchesPlatform(int osIdx, string expected)
    {
        Assert.Equal(expected, FFmpegBinaryDownloader.GetExecutableName((OsPlatform)osIdx));
    }

    [Fact]
    public async Task VideoSession_AutoDownloadDisabled_MissingFFmpeg_DoesNotSendHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        await using var session = new VideoSession(
            new MediaOptions { AutoDownload = false },
            directory,
            new FFmpegBinaryDownloader(httpClient),
            (_, _, _) => Task.FromResult(string.Empty));

        try
        {
            await Assert.ThrowsAsync<FFmpegBinaryNotFoundException>(async () =>
            {
                await using var enumerator = session.ExtractFramesAsync(
                    Path.Combine(directory, "video.mp4"), cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
                await enumerator.MoveNextAsync();
            });
            Assert.Equal(0, handler.SendCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FFmpegDownloader_AutoDownloadDisabled_ValidCacheReturnsWithoutHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);

        try
        {
            string ffmpegDirectory = Path.Combine(directory, "ffmpeg");
            Directory.CreateDirectory(ffmpegDirectory);
            string binaryPath = Path.Combine(
                ffmpegDirectory,
                FFmpegBinaryDownloader.GetExecutableName(PlatformInfo.GetCurrentOs()));
            await File.WriteAllBytesAsync(binaryPath, [], cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(ffmpegDirectory, ".version"), FFmpegBinaryDownloader.MarkerValue, cancellationToken);

            string result = await new FFmpegBinaryDownloader(httpClient).EnsureBinaryAsync(
                directory, cancellationToken: cancellationToken, allowDownload: false);

            Assert.Equal(binaryPath, result);
            Assert.Equal(0, handler.SendCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"llmchamber-media-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
