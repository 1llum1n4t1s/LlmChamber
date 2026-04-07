using System.ComponentModel;

namespace LlmChamber.WinForms.Controls;

/// <summary>
/// チャットインターフェースを提供するPanel。
/// メッセージ表示、入力、ストリーミング応答をサポートする。
/// </summary>
public class ChatPanel : Panel
{
    private readonly RichTextBox _chatDisplay;
    private readonly TextBox _inputBox;
    private readonly Button _sendButton;
    private readonly Panel _inputPanel;

    private ILocalLlm? _llmInstance;
    private IChatSession? _chatSession;
    private CancellationTokenSource? _currentCts;
    private bool _isSending;

    /// <summary>LLMインスタンス。設定時にChatSessionが自動作成される。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ILocalLlm? LlmInstance
    {
        get => _llmInstance;
        set
        {
            _llmInstance = value;
            // LlmInstance変更時は常に新しいChatSessionを作成
            _chatSession = _llmInstance?.CreateChatSession();
        }
    }

    /// <summary>チャットセッション。nullの場合、LlmInstance設定時に自動作成される。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IChatSession? ChatSession
    {
        get => _chatSession;
        set => _chatSession = value;
    }

    public ChatPanel()
    {
        // チャット表示エリア
        _chatDisplay = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 10f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        // 入力パネル（下部に配置）
        _inputPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(0, 4, 0, 0),
        };

        // ユーザー入力ボックス
        _inputBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f),
            PlaceholderText = "メッセージを入力...",
        };
        _inputBox.KeyDown += InputBox_KeyDown;

        // 送信ボタン
        _sendButton = new Button
        {
            Dock = DockStyle.Right,
            Text = "送信",
            Width = 70,
            Font = new Font("Segoe UI", 9f),
            FlatStyle = FlatStyle.System,
        };
        _sendButton.Click += SendButton_Click;

        // 入力パネルにコントロールを追加
        _inputPanel.Controls.Add(_inputBox);
        _inputPanel.Controls.Add(_sendButton);

        // メインパネルに追加
        Controls.Add(_chatDisplay);
        Controls.Add(_inputPanel);
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = SendMessageAsync();
        }
    }

    private void SendButton_Click(object? sender, EventArgs e)
    {
        _ = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        if (_isSending) return;

        var message = _inputBox.Text.Trim();
        if (string.IsNullOrEmpty(message)) return;

        if (_chatSession == null)
        {
            AppendMessage("System", "LLMインスタンスが設定されていません。", Color.Red);
            return;
        }

        _isSending = true;
        _inputBox.Clear();
        _inputBox.Enabled = false;
        _sendButton.Enabled = false;

        try
        {
            // ユーザーメッセージを表示
            AppendMessage("User", message, Color.DarkBlue);

            // アシスタントのヘッダーを追加
            AppendAssistantHeader();

            _currentCts = new CancellationTokenSource();

            // ストリーミング応答を処理
            await foreach (var token in _chatSession.SendAsync(message, _currentCts.Token))
            {
                if (InvokeRequired)
                {
                    Invoke(() => AppendStreamToken(token));
                }
                else
                {
                    AppendStreamToken(token);
                }
            }

            // 応答の末尾に改行を追加
            AppendStreamEnd();
        }
        catch (OperationCanceledException)
        {
            AppendMessage("System", "応答がキャンセルされました。", Color.Gray);
        }
        catch (Exception ex)
        {
            AppendMessage("System", $"エラー: {ex.Message}", Color.Red);
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
            _isSending = false;

            if (InvokeRequired)
            {
                Invoke(() =>
                {
                    _inputBox.Enabled = true;
                    _sendButton.Enabled = true;
                    _inputBox.Focus();
                });
            }
            else
            {
                _inputBox.Enabled = true;
                _sendButton.Enabled = true;
                _inputBox.Focus();
            }
        }
    }

    /// <summary>送信中の処理をキャンセルする。</summary>
    public void CancelCurrentRequest()
    {
        _currentCts?.Cancel();
    }

    private void AppendMessage(string role, string text, Color color)
    {
        void DoAppend()
        {
            var startPos = _chatDisplay.TextLength;
            _chatDisplay.AppendText($"{role}: ");
            _chatDisplay.Select(startPos, role.Length + 2);
            _chatDisplay.SelectionColor = color;
            _chatDisplay.SelectionFont = new Font(_chatDisplay.Font, FontStyle.Bold);

            _chatDisplay.Select(_chatDisplay.TextLength, 0);
            _chatDisplay.SelectionFont = _chatDisplay.Font;
            _chatDisplay.SelectionColor = _chatDisplay.ForeColor;
            _chatDisplay.AppendText(text);
            _chatDisplay.AppendText(Environment.NewLine + Environment.NewLine);

            _chatDisplay.ScrollToCaret();
        }

        if (InvokeRequired)
            Invoke(DoAppend);
        else
            DoAppend();
    }

    private void AppendAssistantHeader()
    {
        void DoAppend()
        {
            var startPos = _chatDisplay.TextLength;
            _chatDisplay.AppendText("Assistant: ");
            _chatDisplay.Select(startPos, "Assistant: ".Length);
            _chatDisplay.SelectionColor = Color.DarkGreen;
            _chatDisplay.SelectionFont = new Font(_chatDisplay.Font, FontStyle.Bold);
            _chatDisplay.Select(_chatDisplay.TextLength, 0);
            _chatDisplay.SelectionFont = _chatDisplay.Font;
            _chatDisplay.SelectionColor = _chatDisplay.ForeColor;
        }

        if (InvokeRequired)
            Invoke(DoAppend);
        else
            DoAppend();
    }

    private void AppendStreamToken(string token)
    {
        _chatDisplay.AppendText(token);
        _chatDisplay.ScrollToCaret();
    }

    private void AppendStreamEnd()
    {
        void DoAppend()
        {
            _chatDisplay.AppendText(Environment.NewLine + Environment.NewLine);
            _chatDisplay.ScrollToCaret();
        }

        if (InvokeRequired)
            Invoke(DoAppend);
        else
            DoAppend();
    }

    /// <summary>チャット表示をクリアする。</summary>
    public void ClearChat()
    {
        _chatDisplay.Clear();
        _chatSession?.ClearHistory();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _currentCts?.Cancel();
            _currentCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
