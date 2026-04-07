using Xunit;

namespace LlmChamber.Tests;

public class DownloadProgressTests
{
    [Fact]
    public void IsCompleted_WhenBytesMatchTotal_ReturnsTrue()
    {
        var progress = new DownloadProgress(1000, 1000, 100.0, "完了");
        Assert.True(progress.IsCompleted);
    }

    [Fact]
    public void IsCompleted_WhenBytesLessThanTotal_ReturnsFalse()
    {
        var progress = new DownloadProgress(500, 1000, 50.0, "ダウンロード中");
        Assert.False(progress.IsCompleted);
    }

    [Fact]
    public void IsCompleted_WhenTotalBytesNull_ReturnsFalse()
    {
        var progress = new DownloadProgress(500, null, null, "ダウンロード中");
        Assert.False(progress.IsCompleted);
    }
}
