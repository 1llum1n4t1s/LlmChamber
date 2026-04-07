using Xunit;

namespace LlmChamber.Tests;

public class ChatMessageTests
{
    [Fact]
    public void FromUser_CreatesUserMessage()
    {
        var msg = ChatMessage.FromUser("こんにちは");

        Assert.Equal(ChatRole.User, msg.Role);
        Assert.Equal("こんにちは", msg.Content);
        Assert.True(msg.Timestamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void FromAssistant_CreatesAssistantMessage()
    {
        var msg = ChatMessage.FromAssistant("お手伝いします");

        Assert.Equal(ChatRole.Assistant, msg.Role);
        Assert.Equal("お手伝いします", msg.Content);
    }

    [Fact]
    public void FromSystem_CreatesSystemMessage()
    {
        var msg = ChatMessage.FromSystem("あなたはアシスタントです");

        Assert.Equal(ChatRole.System, msg.Role);
        Assert.Equal("あなたはアシスタントです", msg.Content);
    }
}
