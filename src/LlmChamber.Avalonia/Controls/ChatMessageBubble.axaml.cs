using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LlmChamber.Avalonia.Controls;

/// <summary>
/// 単一のチャットメッセージを表示するバブルコントロール。
/// ロールに応じて背景色・配置を切り替える。
/// </summary>
public partial class ChatMessageBubble : UserControl
{
    /// <summary>表示するチャットメッセージ。</summary>
    public static readonly StyledProperty<ChatMessage?> MessageProperty =
        AvaloniaProperty.Register<ChatMessageBubble, ChatMessage?>(nameof(Message));

    /// <summary>表示するチャットメッセージ。</summary>
    public ChatMessage? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ChatMessageBubble()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MessageProperty)
        {
            UpdateVisuals();
        }
    }

    private void UpdateVisuals()
    {
        var message = Message;
        var border = this.FindControl<Border>("BubbleBorder");
        var roleLabel = this.FindControl<TextBlock>("RoleLabel");
        var messageText = this.FindControl<TextBlock>("MessageText");

        if (border == null || roleLabel == null || messageText == null)
            return;

        if (message == null)
        {
            roleLabel.Text = string.Empty;
            messageText.Text = string.Empty;
            return;
        }

        messageText.Text = message.Content;

        // ロールに応じてスタイルクラスとラベルを切り替える
        border.Classes.Clear();
        switch (message.Role)
        {
            case ChatRole.User:
                border.Classes.Add("user-bubble");
                roleLabel.Text = "You";
                break;
            case ChatRole.Assistant:
                border.Classes.Add("assistant-bubble");
                roleLabel.Text = "Assistant";
                break;
            case ChatRole.System:
                border.Classes.Add("system-bubble");
                roleLabel.Text = "System";
                break;
        }
    }
}
