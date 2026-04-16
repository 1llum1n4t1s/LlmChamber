using LlmChamber.Internal;
using Xunit;

namespace LlmChamber.Tests;

public class ChatSessionTests
{
    [Fact]
    public void Constructor_WithSystemPrompt_AddsToHistory()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model", new ChatOptions
        {
            SystemPrompt = "テスト用システムプロンプト",
        });

        Assert.Single(session.History);
        Assert.Equal(ChatRole.System, session.History[0].Role);
        Assert.Equal("テスト用システムプロンプト", session.History[0].Content);
    }

    [Fact]
    public void Constructor_WithoutSystemPrompt_EmptyHistory()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model");

        Assert.Empty(session.History);
    }

    [Fact]
    public void ClearHistory_PreservesSystemPrompt()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model", new ChatOptions
        {
            SystemPrompt = "システムプロンプト",
        });

        session.ClearHistory();

        Assert.Single(session.History);
        Assert.Equal(ChatRole.System, session.History[0].Role);
    }

    [Fact]
    public void ClearHistory_WithoutSystemPrompt_EmptiesHistory()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model");

        session.ClearHistory();

        Assert.Empty(session.History);
    }

    [Fact]
    public void Options_ReturnsProvidedOptions()
    {
        var apiClient = CreateMockApiClient();
        var options = new ChatOptions { SystemPrompt = "test" };
        var session = new ChatSession(apiClient, "test-model", options);

        Assert.Same(options, session.Options);
    }

    [Fact]
    public void Options_WithNull_ReturnsDefaultOptions()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model");

        Assert.NotNull(session.Options);
    }

    private static OllamaApiClient CreateMockApiClient()
    {
        // テスト用に最低限のApiClientを作成（実際のAPI呼び出しはしない）
        var httpClient = new System.Net.Http.HttpClient();
        return new OllamaApiClient(httpClient);
    }
}
