using Avalonia.Controls;
using Avalonia.Interactivity;
using LlmChamber;

namespace Sample.Avalonia;

public partial class MainWindow : Window
{
    private ILocalLlm? _llm;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        StatusBarText.Text = "初期化中...";
        DownloadProgress.IsVisible = true;
        ChatControl.IsVisible = false;

        try
        {
            // 選択されたモデルとバリアントを取得
            var modelId = ((ComboBoxItem)ModelComboBox.SelectedItem!).Content?.ToString() ?? "gemma4-e2b";
            var variantName = ((ComboBoxItem)VariantComboBox.SelectedItem!).Content?.ToString() ?? "Auto";
            var variant = Enum.Parse<RuntimeVariant>(variantName);

            // LLMインスタンスを作成
            _llm = LlmChamberFactory.Create(options =>
            {
                options.DefaultModel = modelId;
                options.RuntimeVariant = variant;
            });

            // ダウンロード進捗イベントをフック
            _llm.RuntimeDownloadProgress += (s, progress) =>
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    DownloadProgress.RuntimeProgress = progress;
                });
            };

            _llm.ModelDownloadProgress += (s, progress) =>
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    DownloadProgress.ModelProgress = progress;
                });
            };

            // 初期化を実行
            await _llm.InitializeAsync();

            // チャットコントロールにLLMを接続
            ChatControl.LlmInstance = _llm;

            // UIを切り替え
            DownloadProgress.IsVisible = false;
            ChatControl.IsVisible = true;
            StatusBarText.Text = $"接続済み: {modelId} ({variantName})";
        }
        catch (Exception ex)
        {
            DownloadProgress.IsVisible = false;
            StatusBarText.Text = "接続失敗";

            // Avaloniaのダイアログでエラー表示
            var dialog = new Window
            {
                Title = "エラー",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBlock
                {
                    Text = $"初期化に失敗しました:\n{ex.Message}",
                    TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                    Margin = new global::Avalonia.Thickness(16),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                }
            };
            await dialog.ShowDialog(this);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_llm is not null)
        {
            await _llm.DisposeAsync();
        }
    }
}
