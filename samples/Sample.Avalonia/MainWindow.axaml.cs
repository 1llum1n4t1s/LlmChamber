using Avalonia.Controls;
using Avalonia.Interactivity;
using LlmChamber;

namespace Sample.Avalonia;

public partial class MainWindow : Window
{
    private ILocalLlm? _llm;
    private bool _closed;
    private CancellationTokenSource? _initializeCts;
    private Task? _initializeTask;
    private Task? _disposeTask;
    private bool _cleanupComplete;

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
            ChatControl.LlmInstance = null;
            var previous = _llm;
            _llm = null;
            if (previous is not null) await DisposeOwnedAsync(previous);
            if (_closed) return;
            // 選択されたモデルとバリアントを取得
            var modelId = ((ComboBoxItem)ModelComboBox.SelectedItem!).Content?.ToString() ?? "gemma4-e2b";
            var variantName = ((ComboBoxItem)VariantComboBox.SelectedItem!).Content?.ToString() ?? "Auto";
            var variant = Enum.Parse<RuntimeVariant>(variantName);

            // LLMインスタンスを作成
            var llm = LlmChamberFactory.Create(options =>
            {
                options.DefaultModel = modelId;
                options.RuntimeVariant = variant;
            });
            _llm = llm;

            // ダウンロード進捗イベントをフック
            llm.RuntimeDownloadProgress += (s, progress) =>
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed && ReferenceEquals(_llm, llm)) DownloadProgress.RuntimeProgress = progress;
                });
            };

            llm.ModelDownloadProgress += (s, progress) =>
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_closed && ReferenceEquals(_llm, llm)) DownloadProgress.ModelProgress = progress;
                });
            };

            // 初期化を実行
            _initializeCts = new CancellationTokenSource();
            _initializeTask = llm.InitializeAsync(_initializeCts.Token);
            await _initializeTask;
            if (_closed) return;

            // チャットコントロールにLLMを接続
            ChatControl.LlmInstance = llm;

            // UIを切り替え
            DownloadProgress.IsVisible = false;
            ChatControl.IsVisible = true;
            StatusBarText.Text = $"接続済み: {modelId} ({variantName})";
        }
        catch (Exception ex)
        {
            if (!_closed) ChatControl.LlmInstance = null;
            var failed = _llm;
            _llm = null;
            if (failed is not null) await DisposeOwnedAsync(failed);
            if (_closed) return;
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
            _initializeCts?.Dispose();
            _initializeCts = null;
            _initializeTask = null;
            if (!_closed) ConnectButton.IsEnabled = true;
        }
    }

    private async Task DisposeOwnedAsync(ILocalLlm llm)
    {
        var task = llm.DisposeAsync().AsTask();
        _disposeTask = task;
        try { await task; }
        finally { _disposeTask = null; }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _cleanupComplete) return;
        e.Cancel = true;
        if (_closed) return;
        _closed = true;
        ChatControl.LlmInstance = null;
        var llm = _llm;
        _llm = null;
        _initializeCts?.Cancel();
        var initializeTask = _initializeTask;
        var disposeTask = _disposeTask;
        await Task.Yield();
        try
        {
            if (initializeTask is not null)
            {
                try { await initializeTask; } catch { /* 接続処理で失敗を処理する。 */ }
            }
            if (disposeTask is not null) await disposeTask;
            if (llm is not null) await llm.DisposeAsync();
        }
        finally
        {
            _cleanupComplete = true;
            Close();
        }
    }
}
