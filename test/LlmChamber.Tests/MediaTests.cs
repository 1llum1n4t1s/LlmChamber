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

    // ── Options デフォルト値 ──

    [Fact]
    public void VideoAnalysisOptions_Defaults_AreReasonable()
    {
        var opts = new VideoAnalysisOptions();

        Assert.Equal(1.0, opts.FrameIntervalSeconds);
        Assert.Equal(60, opts.MaxFrames);
        Assert.True(opts.AnalyzeFrames);
        Assert.Contains("{timestamp}", opts.FramePromptTemplate);
        Assert.Contains("{frame_index}", opts.FramePromptTemplate);
        Assert.Equal(1280, opts.MaxFrameWidth);
        Assert.Equal(VideoFrameFormat.Jpeg, opts.FrameFormat);
    }

    [Fact]
    public void MediaOptions_Defaults_AutoDownloadEnabled()
    {
        var opts = new MediaOptions();
        Assert.True(opts.AutoDownload);
        Assert.Null(opts.FFmpegBinaryPath);
    }

    [Fact]
    public void VideoFrameAnalysis_Record_EqualityWorks()
    {
        byte[] bytes = [1, 2, 3];
        var a = new VideoFrameAnalysis(0, TimeSpan.FromSeconds(1), bytes, "desc");
        var b = new VideoFrameAnalysis(0, TimeSpan.FromSeconds(1), bytes, "desc");
        // 同じ参照のバイト列・同じフィールド → 等価
        Assert.Equal(a, b);
    }
}
