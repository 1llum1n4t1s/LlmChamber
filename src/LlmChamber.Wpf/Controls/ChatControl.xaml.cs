using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LlmChamber.Wpf.Controls;

/// <summary>
/// LLMとのチャットUIを提供するコントロール。
/// <see cref="LlmInstance"/> または <see cref="ChatSession"/> を設定して使用する。
/// </summary>
public partial class ChatControl : UserControl
{
    private readonly ObservableCollection<ChatMessage> _messages = [];
    private CancellationTokenSource? _streamCts;
    private bool _isSending;

    // ── DependencyProperties ──

    /// <summary>LLMインスタンス。設定するとチャットセッションが自動生成される。</summary>
    public static readonly DependencyProperty LlmInstanceProperty =
        DependencyProperty.Register(
            nameof(LlmInstance),
            typeof(ILocalLlm),
            typeof(ChatControl),
            new PropertyMetadata(null, OnLlmInstanceChanged));

    /// <summary>チャットセッション。直接設定するか、LlmInstanceから自動生成される。</summary>
    public static readonly DependencyProperty ChatSessionProperty =
        DependencyProperty.Register(
            nameof(ChatSession),
            typeof(IChatSession),
            typeof(ChatControl),
            new PropertyMetadata(null, OnChatSessionChanged));

    /// <summary>入力が有効かどうか（送信中はfalse）。</summary>
    public static readonly DependencyProperty IsInputEnabledProperty =
        DependencyProperty.Register(
            nameof(IsInputEnabled),
            typeof(bool),
            typeof(ChatControl),
            new PropertyMetadata(true));

    /// <inheritdoc cref="LlmInstanceProperty"/>
    public ILocalLlm? LlmInstance
    {
        get => (ILocalLlm?)GetValue(LlmInstanceProperty);
        set => SetValue(LlmInstanceProperty, value);
    }

    /// <inheritdoc cref="ChatSessionProperty"/>
    public IChatSession? ChatSession
    {
        get => (IChatSession?)GetValue(ChatSessionProperty);
        set => SetValue(ChatSessionProperty, value);
    }

    /// <inheritdoc cref="IsInputEnabledProperty"/>
    public bool IsInputEnabled
    {
        get => (bool)GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    // ── コンストラクター ──

    /// <summary>インスタンスを初期化する。</summary>
    public ChatControl()
    {
        InitializeComponent();
        MessageList.ItemsSource = _messages;
    }

    // ── PropertyChanged コールバック ──

    private static void OnLlmInstanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatControl self) return;

        if (e.NewValue is ILocalLlm llm)
        {
            // LlmInstance変更時は常に新しいChatSessionを作成
            self.ChatSession = llm.CreateChatSession();
        }
        else
        {
            self.ChatSession = null;
        }
    }

    private static void OnChatSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatControl self) return;
        self.SyncHistory();
    }

    // ── 送信処理 ──

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (_isSending) return;

        var text = InputTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var session = ChatSession;
        if (session is null)
        {
            UpdateStatus("エラー: ChatSessionが未設定です");
            return;
        }

        _isSending = true;
        IsInputEnabled = false;
        InputTextBox.Text = string.Empty;

        try
        {
            // ユーザーメッセージを表示
            _messages.Add(ChatMessage.FromUser(text));
            ScrollToBottom();

            UpdateStatus("応答を生成中...");

            // ストリーミングでアシスタント応答を受信
            _streamCts = new CancellationTokenSource();
            var responseBuilder = new System.Text.StringBuilder();
            var assistantMessage = ChatMessage.FromAssistant(string.Empty);
            _messages.Add(assistantMessage);

            await foreach (var chunk in session.SendAsync(text, _streamCts.Token))
            {
                responseBuilder.Append(chunk);
                var currentText = responseBuilder.ToString();

                // UIスレッドでメッセージを更新
                await Dispatcher.InvokeAsync(() =>
                {
                    var index = _messages.Count - 1;
                    _messages[index] = assistantMessage with { Content = currentText };
                    ScrollToBottom();
                });
            }

            UpdateStatus("準備完了");
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("応答がキャンセルされました");
        }
        catch (Exception ex)
        {
            UpdateStatus($"エラー: {ex.Message}");
        }
        finally
        {
            _streamCts?.Dispose();
            _streamCts = null;
            _isSending = false;
            IsInputEnabled = true;
            InputTextBox.Focus();
        }
    }

    // ── ヘルパー ──

    /// <summary>チャット履歴をメッセージ一覧に同期する。</summary>
    private void SyncHistory()
    {
        _messages.Clear();

        if (ChatSession is not { } session) return;

        foreach (var msg in session.History)
        {
            _messages.Add(msg);
        }

        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() =>
        {
            MessageScrollViewer.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateStatus(string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = text;
        });
    }

    /// <summary>
    /// モデル名をステータスバーに表示する。
    /// </summary>
    public void SetModelName(string modelName)
    {
        ModelNameText.Text = modelName;
    }

    /// <summary>
    /// 進行中のストリーミングをキャンセルする。
    /// </summary>
    public void CancelStreaming()
    {
        _streamCts?.Cancel();
    }
}
