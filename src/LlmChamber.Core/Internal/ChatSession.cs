using System.Runtime.CompilerServices;
using System.Text;
using LlmChamber.Internal.Api;

namespace LlmChamber.Internal;

/// <summary>IChatSessionの実装。</summary>
internal sealed class ChatSession : IChatSession
{
    private readonly OllamaApiClient _apiClient;
    private readonly string _model;
    private readonly Func<CancellationToken, Task>? _ensureInitialized;
    private readonly List<ChatMessage> _history = [];
    private readonly object _historyLock = new();

    public ChatSession(OllamaApiClient apiClient, string model, ChatOptions? options = null,
        Func<CancellationToken, Task>? ensureInitialized = null)
    {
        _apiClient = apiClient;
        _model = model;
        _ensureInitialized = ensureInitialized;
        Options = options ?? new ChatOptions();

        // システムプロンプトがあれば履歴に追加
        if (Options.SystemPrompt is not null)
        {
            _history.Add(ChatMessage.FromSystem(Options.SystemPrompt));
        }
    }

    public ChatOptions Options { get; }

    public IReadOnlyList<ChatMessage> History
    {
        get
        {
            lock (_historyLock)
            {
                return _history.ToList();
            }
        }
    }

    public async IAsyncEnumerable<string> SendAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_ensureInitialized is not null)
            await _ensureInitialized(cancellationToken);

        AddUserMessage(message);
        bool success = false;

        try
        {
            var messages = BuildOllamaMessages();
            var sb = new StringBuilder();

            await foreach (string chunk in _apiClient.ChatStreamAsync(
                _model, messages, ResolveInferenceOptions(), cancellationToken))
            {
                sb.Append(chunk);
                yield return chunk;
            }

            AddAssistantMessage(sb.ToString());
            success = true;
        }
        finally
        {
            // API失敗・キャンセル時はユーザーメッセージをロールバック
            if (!success) RemoveLastUserMessage();
        }
    }

    public async Task<string> SendCompleteAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (_ensureInitialized is not null)
            await _ensureInitialized(cancellationToken);

        AddUserMessage(message);

        try
        {
            var messages = BuildOllamaMessages();
            string response = await _apiClient.ChatCompleteAsync(
                _model, messages, ResolveInferenceOptions(), cancellationToken);

            AddAssistantMessage(response);
            return response;
        }
        catch
        {
            // API失敗時はユーザーメッセージをロールバック
            RemoveLastUserMessage();
            throw;
        }
    }

    public void ClearHistory()
    {
        lock (_historyLock)
        {
            // システムプロンプトは保持
            var systemMessages = _history.Where(m => m.Role == ChatRole.System).ToList();
            _history.Clear();
            _history.AddRange(systemMessages);
        }
    }

    private void AddUserMessage(string content)
    {
        lock (_historyLock)
        {
            _history.Add(ChatMessage.FromUser(content));
        }
    }

    /// <summary>API失敗時に最後のユーザーメッセージをロールバックする。</summary>
    private void RemoveLastUserMessage()
    {
        lock (_historyLock)
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].Role == ChatRole.User)
                {
                    _history.RemoveAt(i);
                    return;
                }
            }
        }
    }

    private void AddAssistantMessage(string content)
    {
        lock (_historyLock)
        {
            _history.Add(ChatMessage.FromAssistant(content));
            TrimHistory();
        }
    }

    private void TrimHistory()
    {
        if (Options.MaxHistoryMessages is not { } max) return;

        // システムメッセージ以外をカウント
        int nonSystemCount = _history.Count(m => m.Role != ChatRole.System);
        while (nonSystemCount > max)
        {
            int idx = _history.FindIndex(m => m.Role != ChatRole.System);
            if (idx < 0) break;
            _history.RemoveAt(idx);
            nonSystemCount--;
        }
    }

    /// <summary>ユーザー指定のInferenceOptionsがなければプリセットデフォルトを返す。</summary>
    private InferenceOptions? ResolveInferenceOptions()
    {
        if (Options.InferenceOptions is not null) return Options.InferenceOptions;

        // プリセットのデフォルト推論パラメータを適用
        var preset = OllamaModels.FindPreset(_model);
        return preset?.DefaultInferenceOptions;
    }

    private IReadOnlyList<OllamaMessage> BuildOllamaMessages()
    {
        lock (_historyLock)
        {
            return _history.Select(m => new OllamaMessage
            {
                Role = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user",
                    ChatRole.Assistant => "assistant",
                    _ => "user",
                },
                Content = m.Content,
            }).ToList();
        }
    }
}
