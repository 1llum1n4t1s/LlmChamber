using Xunit;

namespace LlmChamber.Tests;

public class ModelPresetTests
{
    [Fact]
    public void FormattedDownloadSize_ReturnsReadableFormat()
    {
        var preset = new ModelPreset
        {
            Id = "test",
            OllamaTag = "test:latest",
            DisplayName = "Test",
            Family = "Test",
            ApproximateDownloadSize = 3L * 1024 * 1024 * 1024, // 3GB
            RecommendedMinRam = 5L * 1024 * 1024 * 1024, // 5GB
        };

        Assert.Equal("3.0 GB", preset.FormattedDownloadSize);
        Assert.Equal("5.0 GB", preset.FormattedMinRam);
    }

    [Fact]
    public void FormattedDownloadSize_MB_ReturnsCorrectFormat()
    {
        var preset = new ModelPreset
        {
            Id = "test",
            OllamaTag = "test:latest",
            DisplayName = "Test",
            Family = "Test",
            ApproximateDownloadSize = 500L * 1024 * 1024, // 500MB
            RecommendedMinRam = 1024L * 1024 * 1024, // 1GB
        };

        Assert.Equal("500.0 MB", preset.FormattedDownloadSize);
    }
}
