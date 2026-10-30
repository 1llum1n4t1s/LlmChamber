using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace LlmChamber.Avalonia.Controls;

/// <summary>
/// LLMとのチャットインターフェースを提供するコントロール。
/// メッセージ表示・入力・ストリーミング応答を統合する。
/// </summary>
public partial class ChatControl : UserControl
{
    /// <summary>接続するLLMインスタンス。</summary>
    public static readonly StyledProperty<ILocalLlm?> LlmInstanceProperty =
        AvaloniaProperty.Register<ChatControl, ILocalLlm?>(nameof(LlmInstance));

    /// <summary>接続するLLMインスタンス。</summary>
    public ILocalLlm? LlmInstance
    {
        get => GetValue(LlmInstanceProperty);
        set => SetValue(LlmInstanceProperty, value);
    }

    private IChatSession? _chatSession;
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private bool _isSending;
    private CancellationTokenSource? _sendCts;
    private int _sessionVersion;

    // UI要素
    private ScrollViewer? _messageScrollViewer;
    private ItemsControl? _messageList;
    private TextBox? _inputTextBox;
    private Button? _sendButton;
    private TextBlock? _statusLabel;
    private TextBlock? _modelLabel;
    private TextBlock? _emptyHint;
    private Border? _statusDot;

    public ChatControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _messageScrollViewer = this.FindControl<ScrollViewer>("MessageScrollViewer");
        _messageList = this.FindControl<ItemsControl>("MessageList");
        _inputTextBox = this.FindControl<TextBox>("InputTextBox");
        _sendButton = this.FindControl<Button>("SendButton");
        _statusLabel = this.FindControl<TextBlock>("StatusLabel");
        _modelLabel = this.FindControl<TextBlock>("ModelLabel");
        _emptyHint = this.FindControl<TextBlock>("EmptyHint");
        _statusDot = this.FindControl<Border>("StatusDot");

        if (_messageList != null)
            _messageList.ItemsSource = _messages;

        if (_sendButton != null)
            _sendButton.Click += OnSendButtonClick;

        if (_inputTextBox != null)
            _inputTextBox.KeyDown += OnInputKeyDown;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LlmInstanceProperty)
        {
            OnLlmInstanceChanged(change.GetNewValue<ILocalLlm?>());
        }
    }

    private void OnLlmInstanceChanged(ILocalLlm? llm)
    {
        _sessionVersion++;
        _sendCts?.Cancel();
        _chatSession = llm?.CreateChatSession();

        if (_statusLabel != null && _statusDot != null)
        {
            if (llm != null)
            {
                _statusLabel.Text = llm.IsReady ? "準備完了" : "初期化中...";
                _statusDot.Background = llm.IsReady
                    ? global::Avalonia.Media.Brushes.LimeGreen
                    : global::Avalonia.Media.Brushes.Orange;
            }
            else
            {
                _statusLabel.Text = "未接続";
                _statusDot.Background = global::Avalonia.Media.Brushes.Gray;
            }
        }

        if (_modelLabel != null)
        {
            _modelLabel.Text = llm != null ? "LLM Chat" : "LLM Chat (未接続)";
        }

        // 履歴をクリア
        _messages.Clear();
        UpdateEmptyHint();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isSending)
        {
            e.Handled = true;
            _ = SendMessageAsync();
        }
    }

    private async void OnSendButtonClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        if (_inputTextBox == null || _chatSession == null || _isSending)
            return;

        var text = _inputTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        var session = _chatSession;
        var sessionVersion = _sessionVersion;
        _isSending = true;
        _inputTextBox.Text = string.Empty;
        UpdateSendButtonState();

        _sendCts = new CancellationTokenSource();
        var assistantIndex = -1;
        try
        {
            // ユーザーメッセージを追加
            var userMessage = ChatMessage.FromUser(text);
            _messages.Add(userMessage);
            UpdateEmptyHint();
            await ScrollToBottomAsync();
            if (sessionVersion != _sessionVersion) return;

            // アシスタント応答用のプレースホルダーを追加
            var assistantMessage = ChatMessage.FromAssistant(string.Empty);
            _messages.Add(assistantMessage);
            assistantIndex = _messages.Count - 1;

            // ステータス更新
            UpdateStatus("応答中...", global::Avalonia.Media.Brushes.Orange, sessionVersion);

            var responseBuilder = new System.Text.StringBuilder();
            await foreach (var token in session.SendAsync(text, _sendCts.Token))
            {
                responseBuilder.Append(token);
                var currentText = responseBuilder.ToString();

                // UIスレッドでメッセージを更新
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (sessionVersion != _sessionVersion) return;
                    _messages[assistantIndex] = ChatMessage.FromAssistant(currentText);
                });
                await ScrollToBottomAsync();
            }

            UpdateStatus("準備完了", global::Avalonia.Media.Brushes.LimeGreen, sessionVersion);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("キャンセル", global::Avalonia.Media.Brushes.Gray, sessionVersion);
        }
        catch (Exception ex)
        {
            // エラーメッセージを表示
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (sessionVersion != _sessionVersion || assistantIndex < 0) return;
                _messages[assistantIndex] = ChatMessage.FromAssistant($"[エラー] {ex.Message}");
            });
            UpdateStatus("エラー", global::Avalonia.Media.Brushes.Red, sessionVersion);
        }
        finally
        {
            _isSending = false;
            _sendCts?.Dispose();
            _sendCts = null;
            UpdateSendButtonState();
        }
    }

    private void UpdateSendButtonState()
    {
        if (_sendButton != null)
            _sendButton.IsEnabled = !_isSending;
        if (_inputTextBox != null)
            _inputTextBox.IsEnabled = !_isSending;
    }

    private void UpdateStatus(string text, global::Avalonia.Media.IBrush dotColor, int sessionVersion)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (sessionVersion != _sessionVersion) return;
            if (_statusLabel != null) _statusLabel.Text = text;
            if (_statusDot != null) _statusDot.Background = dotColor;
        });
    }

    private void UpdateEmptyHint()
    {
        if (_emptyHint != null)
            _emptyHint.IsVisible = _messages.Count == 0;
    }

    private async Task ScrollToBottomAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _messageScrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}
