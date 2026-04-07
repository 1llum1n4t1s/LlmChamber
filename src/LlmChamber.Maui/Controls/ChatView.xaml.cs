using System.Collections.ObjectModel;

namespace LlmChamber.Maui.Controls;

/// <summary>
/// チャットメッセージの表示用ViewModel。
/// </summary>
public class ChatMessageViewModel
{
    public string RoleLabel { get; set; } = "";
    public string Content { get; set; } = "";
    public Color BackgroundColor { get; set; } = Colors.White;
    public Color TextColor { get; set; } = Colors.Black;
    public Color RoleColor { get; set; } = Colors.Gray;
    public LayoutOptions Alignment { get; set; } = LayoutOptions.Start;
}

/// <summary>
/// チャットインターフェースを提供するMAUI ContentView。
/// メッセージ表示、入力、ストリーミング応答をサポートする。
/// </summary>
public partial class ChatView : ContentView
{
    /// <summary>LLMインスタンスのBindableProperty。</summary>
    public static readonly BindableProperty LlmInstanceProperty =
        BindableProperty.Create(
            nameof(LlmInstance),
            typeof(ILocalLlm),
            typeof(ChatView),
            defaultValue: null,
            propertyChanged: OnLlmInstanceChanged);

    /// <summary>入力が有効かどうかのBindableProperty。</summary>
    public static readonly BindableProperty IsInputEnabledProperty =
        BindableProperty.Create(
            nameof(IsInputEnabled),
            typeof(bool),
            typeof(ChatView),
            defaultValue: true);

    private IChatSession? _chatSession;
    private CancellationTokenSource? _currentCts;

    /// <summary>LLMインスタンス。設定時にChatSessionが自動作成される。</summary>
    public ILocalLlm? LlmInstance
    {
        get => (ILocalLlm?)GetValue(LlmInstanceProperty);
        set => SetValue(LlmInstanceProperty, value);
    }

    /// <summary>入力が有効かどうか。</summary>
    public bool IsInputEnabled
    {
        get => (bool)GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    /// <summary>メッセージ一覧。</summary>
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ChatView()
    {
        InitializeComponent();

        // x:Name="ThisView" の代わりにBindingContextを利用
        // XAMLでは {x:Reference ThisView} を使用
        // ここではイベントハンドラを設定
        SendButton.Clicked += OnSendClicked;
        InputEntry.Completed += OnInputCompleted;
    }

    private static void OnLlmInstanceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ChatView view) return;

        if (newValue is ILocalLlm llm)
        {
            // LlmInstance変更時は常に新しいセッションを作成
            view._chatSession = llm.CreateChatSession();
        }
        else
        {
            // LlmInstance=null時はセッションをクリア
            view._chatSession = null;
        }
    }

    private void OnSendClicked(object? sender, EventArgs e)
    {
        _ = SendMessageAsync();
    }

    private void OnInputCompleted(object? sender, EventArgs e)
    {
        _ = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        var message = InputEntry.Text?.Trim();
        if (string.IsNullOrEmpty(message)) return;

        if (_chatSession == null)
        {
            Messages.Add(new ChatMessageViewModel
            {
                RoleLabel = "System",
                Content = "LLMインスタンスが設定されていません。",
                BackgroundColor = Color.FromArgb("#FFF0F0"),
                TextColor = Colors.Red,
                RoleColor = Colors.Red,
                Alignment = LayoutOptions.Center,
            });
            return;
        }

        // 入力を無効化
        IsInputEnabled = false;
        InputEntry.Text = string.Empty;

        try
        {
            // ユーザーメッセージを追加
            Messages.Add(new ChatMessageViewModel
            {
                RoleLabel = "User",
                Content = message,
                BackgroundColor = Color.FromArgb("#DCF8C6"),
                TextColor = Color.FromArgb("#1A1A1A"),
                RoleColor = Color.FromArgb("#075E54"),
                Alignment = LayoutOptions.End,
            });

            // アシスタントメッセージのプレースホルダーを追加
            var assistantMessage = new ChatMessageViewModel
            {
                RoleLabel = "Assistant",
                Content = "",
                BackgroundColor = Color.FromArgb("#F0F0F0"),
                TextColor = Color.FromArgb("#1A1A1A"),
                RoleColor = Color.FromArgb("#34495E"),
                Alignment = LayoutOptions.Start,
            };
            Messages.Add(assistantMessage);

            _currentCts = new CancellationTokenSource();
            var responseText = string.Empty;

            // ストリーミング応答を処理
            await foreach (var token in _chatSession.SendAsync(message, _currentCts.Token))
            {
                responseText += token;

                // UIスレッドで更新
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // ObservableCollectionの要素を更新するためにreplaceする
                    var index = Messages.Count - 1;
                    Messages[index] = new ChatMessageViewModel
                    {
                        RoleLabel = "Assistant",
                        Content = responseText,
                        BackgroundColor = Color.FromArgb("#F0F0F0"),
                        TextColor = Color.FromArgb("#1A1A1A"),
                        RoleColor = Color.FromArgb("#34495E"),
                        Alignment = LayoutOptions.Start,
                    };
                });
            }

            // 最後までスクロール
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (MessageList.Handler != null)
                {
                    MessageList.ScrollTo(Messages.Count - 1, position: ScrollToPosition.End);
                }
            });
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new ChatMessageViewModel
            {
                RoleLabel = "System",
                Content = "応答がキャンセルされました。",
                BackgroundColor = Color.FromArgb("#FFF8E1"),
                TextColor = Colors.Gray,
                RoleColor = Colors.Gray,
                Alignment = LayoutOptions.Center,
            });
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel
            {
                RoleLabel = "System",
                Content = $"エラー: {ex.Message}",
                BackgroundColor = Color.FromArgb("#FFF0F0"),
                TextColor = Colors.Red,
                RoleColor = Colors.Red,
                Alignment = LayoutOptions.Center,
            });
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
            IsInputEnabled = true;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                InputEntry.Focus();
            });
        }
    }

    /// <summary>送信中の処理をキャンセルする。</summary>
    public void CancelCurrentRequest()
    {
        _currentCts?.Cancel();
    }

    /// <summary>チャット表示をクリアする。</summary>
    public void ClearChat()
    {
        Messages.Clear();
        _chatSession?.ClearHistory();
    }
}
