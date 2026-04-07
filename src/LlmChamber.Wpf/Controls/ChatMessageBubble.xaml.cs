using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace LlmChamber.Wpf.Controls;

/// <summary>
/// 単一のチャットメッセージを吹き出し形式で表示するコントロール。
/// </summary>
public partial class ChatMessageBubble : UserControl
{
    /// <summary>表示する <see cref="ChatMessage"/>。</summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(ChatMessage),
            typeof(ChatMessageBubble),
            new PropertyMetadata(null));

    /// <inheritdoc cref="MessageProperty"/>
    public ChatMessage? Message
    {
        get => (ChatMessage?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>インスタンスを初期化する。</summary>
    public ChatMessageBubble()
    {
        InitializeComponent();
    }
}

// ── コンバーター群 ──

/// <summary>ChatRole → 背景色ブラシ</summary>
internal sealed class ChatRoleToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush UserBrush = new(Color.FromRgb(0xE3, 0xF2, 0xFD));   // #E3F2FD
    private static readonly SolidColorBrush AssistantBrush = new(Color.FromRgb(0xF5, 0xF5, 0xF5)); // #F5F5F5
    private static readonly SolidColorBrush SystemBrush = new(Color.FromRgb(0xFF, 0xF9, 0xC4));    // #FFF9C4

    static ChatRoleToBackgroundConverter()
    {
        UserBrush.Freeze();
        AssistantBrush.Freeze();
        SystemBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ChatRole role
            ? role switch
            {
                ChatRole.User => UserBrush,
                ChatRole.Assistant => AssistantBrush,
                ChatRole.System => SystemBrush,
                _ => AssistantBrush,
            }
            : AssistantBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>ChatRole → HorizontalAlignment（Userは右寄せ、それ以外は左寄せ）</summary>
internal sealed class ChatRoleToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ChatRole role && role == ChatRole.User
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>ChatRole → FontStyle（Systemはイタリック）</summary>
internal sealed class ChatRoleToFontStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ChatRole role && role == ChatRole.System
            ? FontStyles.Italic
            : FontStyles.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>ChatRole → ラベル文字列</summary>
internal sealed class ChatRoleToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ChatRole role
            ? role switch
            {
                ChatRole.User => "You",
                ChatRole.Assistant => "Assistant",
                ChatRole.System => "System",
                _ => string.Empty,
            }
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
