using LlmChamber;

namespace Sample.Maui;

public partial class MainPage : ContentPage
{
    private ILocalLlm? _llm;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Clicked(object? sender, EventArgs e)
    {
        ConnectButton.IsEnabled = false;
        StatusLabel.Text = "初期化中...";
        DownloadProgress.IsVisible = true;
        ChatView.IsVisible = false;

        try
        {
            // 選択されたモデルとバリアントを取得
            var modelId = ModelPicker.SelectedItem?.ToString() ?? "gemma4-e2b";
            var variantName = VariantPicker.SelectedItem?.ToString() ?? "Auto";
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
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DownloadProgress.RuntimeProgress = progress;
                });
            };

            _llm.ModelDownloadProgress += (s, progress) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DownloadProgress.ModelProgress = progress;
                });
            };

            // 初期化を実行
            await _llm.InitializeAsync();

            // チャットビューにLLMを接続
            ChatView.LlmInstance = _llm;

            // UIを切り替え
            DownloadProgress.IsVisible = false;
            ChatView.IsVisible = true;
            StatusLabel.Text = $"接続済み: {modelId} ({variantName})";
        }
        catch (Exception ex)
        {
            DownloadProgress.IsVisible = false;
            StatusLabel.Text = "接続失敗";
            await DisplayAlert("エラー", $"初期化に失敗しました:\n{ex.Message}", "OK");
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }
}
