using System.Windows;
using System.Windows.Controls;
using LlmChamber;

namespace Sample.Wpf;

public partial class MainWindow : Window
{
    private ILocalLlm? _llm;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        StatusBarText.Text = "初期化中...";
        DownloadProgress.Visibility = Visibility.Visible;
        ChatControl.Visibility = Visibility.Collapsed;

        try
        {
            // 選択されたモデルとバリアントを取得
            var modelId = ((ComboBoxItem)ModelComboBox.SelectedItem).Content.ToString()!;
            var variantName = ((ComboBoxItem)VariantComboBox.SelectedItem).Content.ToString()!;
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
                Dispatcher.Invoke(() =>
                {
                    DownloadProgress.RuntimeProgress = progress;
                });
            };

            _llm.ModelDownloadProgress += (s, progress) =>
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadProgress.ModelProgress = progress;
                });
            };

            // 初期化を実行
            await _llm.InitializeAsync();

            // チャットコントロールにLLMを接続
            ChatControl.LlmInstance = _llm;
            ChatControl.SetModelName(modelId);

            // UIを切り替え
            DownloadProgress.Visibility = Visibility.Collapsed;
            ChatControl.Visibility = Visibility.Visible;
            StatusBarText.Text = $"接続済み: {modelId} ({variantName})";
        }
        catch (Exception ex)
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
            StatusBarText.Text = "接続失敗";
            MessageBox.Show(
                $"初期化に失敗しました:\n{ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
