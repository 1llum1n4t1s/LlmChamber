using LlmChamber.Internal;
using Xunit;

namespace LlmChamber.Tests;

public class OllamaModelsTests
{
    [Fact]
    public void Presets_Contains4Models()
    {
        Assert.Equal(4, OllamaModels.Presets.Count);
    }

    [Theory]
    [InlineData("gemma4-e2b", "gemma4:e2b")]
    [InlineData("gemma4-e4b", "gemma4:e4b")]
    [InlineData("qwen3.5-2b", "qwen3:2b")]
    [InlineData("phi4-mini", "phi4-mini")]
    public void FindPreset_ById_ReturnsCorrectPreset(string id, string expectedTag)
    {
        var preset = OllamaModels.FindPreset(id);
        Assert.NotNull(preset);
        Assert.Equal(expectedTag, preset.OllamaTag);
    }

    [Fact]
    public void FindPreset_ByOllamaTag_ReturnsCorrectPreset()
    {
        var preset = OllamaModels.FindPreset("gemma4:e2b");
        Assert.NotNull(preset);
        Assert.Equal("gemma4-e2b", preset.Id);
    }

    [Fact]
    public void FindPreset_UnknownId_ReturnsNull()
    {
        var preset = OllamaModels.FindPreset("nonexistent-model");
        Assert.Null(preset);
    }

    [Fact]
    public void ResolveModelTag_WithPresetId_ReturnsOllamaTag()
    {
        string tag = OllamaModels.ResolveModelTag("gemma4-e2b");
        Assert.Equal("gemma4:e2b", tag);
    }

    [Fact]
    public void ResolveModelTag_WithUnknownId_ReturnsAsIs()
    {
        string tag = OllamaModels.ResolveModelTag("custom-model:latest");
        Assert.Equal("custom-model:latest", tag);
    }

    [Fact]
    public void AllPresets_HaveRequiredFields()
    {
        foreach (var preset in OllamaModels.Presets)
        {
            Assert.NotEmpty(preset.Id);
            Assert.NotEmpty(preset.OllamaTag);
            Assert.NotEmpty(preset.DisplayName);
            Assert.NotEmpty(preset.Family);
            Assert.True(preset.ApproximateDownloadSize > 0);
            Assert.True(preset.RecommendedMinRam > 0);
        }
    }
}
