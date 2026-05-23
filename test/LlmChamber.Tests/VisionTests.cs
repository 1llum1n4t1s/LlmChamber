using System.Text.Json;
using LlmChamber.Internal;
using LlmChamber.Internal.Api;
using Xunit;

namespace LlmChamber.Tests;

/// <summary>Vision (multimodal) 対応の単体テスト。</summary>
public class VisionTests
{
    [Fact]
    public void ChatMessage_FromUser_WithoutImages_HasNullImages()
    {
        var msg = ChatMessage.FromUser("hello");
        Assert.Null(msg.Images);
        Assert.Equal(ChatRole.User, msg.Role);
        Assert.Equal("hello", msg.Content);
    }

    [Fact]
    public void ChatMessage_FromUser_WithImages_KeepsImages()
    {
        byte[] image1 = [0x89, 0x50, 0x4E, 0x47]; // PNGヘッダ
        byte[] image2 = [0xFF, 0xD8, 0xFF, 0xE0]; // JPEGヘッダ
        var msg = ChatMessage.FromUser("画像を説明してください", new[] { image1, image2 });

        Assert.NotNull(msg.Images);
        Assert.Equal(2, msg.Images.Count);
        Assert.Equal(image1, msg.Images[0]);
        Assert.Equal(image2, msg.Images[1]);
    }

    [Fact]
    public void OllamaApiClient_EncodeImages_NullReturnsNull()
    {
        var result = OllamaApiClient.EncodeImages(null);
        Assert.Null(result);
    }

    [Fact]
    public void OllamaApiClient_EncodeImages_EmptyListReturnsNull()
    {
        var result = OllamaApiClient.EncodeImages(Array.Empty<byte[]>());
        Assert.Null(result);
    }

    [Fact]
    public void OllamaApiClient_EncodeImages_SingleImage_ReturnsBase64()
    {
        byte[] image = [0x01, 0x02, 0x03, 0xFF];
        var result = OllamaApiClient.EncodeImages(new[] { image });

        Assert.NotNull(result);
        Assert.Single(result);
        // Base64("01 02 03 FF") = "AQID/w=="
        Assert.Equal(Convert.ToBase64String(image), result[0]);
    }

    [Fact]
    public void OllamaApiClient_EncodeImages_SkipsEmptyBytes()
    {
        byte[] image1 = [0x01, 0x02];
        byte[] empty = Array.Empty<byte>();
        var result = OllamaApiClient.EncodeImages(new[] { image1, empty });

        Assert.NotNull(result);
        Assert.Single(result); // 空のバイト列は除外される
    }

    [Fact]
    public void GenerateRequest_WithImages_SerializesImagesField()
    {
        byte[] image = [0x89, 0x50];
        var request = new GenerateRequest
        {
            Model = "gemma3:4b",
            Prompt = "describe this image",
            Images = new List<string> { Convert.ToBase64String(image) },
        };

        string json = JsonSerializer.Serialize(request, OllamaJsonContext.Instance.GenerateRequest);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("images", out var imagesEl));
        Assert.Equal(JsonValueKind.Array, imagesEl.ValueKind);
        Assert.Equal(1, imagesEl.GetArrayLength());
    }

    [Fact]
    public void GenerateRequest_WithoutImages_OmitsImagesField()
    {
        var request = new GenerateRequest
        {
            Model = "phi4-mini",
            Prompt = "hello",
        };

        string json = JsonSerializer.Serialize(request, OllamaJsonContext.Instance.GenerateRequest);
        using var doc = JsonDocument.Parse(json);

        // imagesプロパティはWhenWritingNullでスキップされる
        Assert.False(doc.RootElement.TryGetProperty("images", out _));
    }

    [Fact]
    public void OllamaMessage_WithImages_SerializesImagesField()
    {
        var msg = new OllamaMessage
        {
            Role = "user",
            Content = "describe",
            Images = new List<string> { "AQID" },
        };

        string json = JsonSerializer.Serialize(msg, OllamaJsonContext.Instance.OllamaMessage);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("images", out var imagesEl));
        Assert.Equal(1, imagesEl.GetArrayLength());
    }

    [Fact]
    public void VisionPresets_AllHaveValidConfiguration()
    {
        var visionPresets = new[] { "gemma3-4b", "qwen2.5vl-3b", "llava-7b" };
        foreach (string id in visionPresets)
        {
            var preset = OllamaModels.FindPreset(id);
            Assert.NotNull(preset);
            Assert.Contains("Vision", preset.DisplayName);
            Assert.True(preset.ApproximateDownloadSize > 0);
            Assert.True(preset.RecommendedMinRam > 0);
        }
    }
}
