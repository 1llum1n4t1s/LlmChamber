namespace LlmChamber.Maui.Controls;

/// <summary>
/// ランタイムおよびモデルのダウンロード進捗を表示するMAUI ContentView。
/// </summary>
public partial class DownloadProgressView : ContentView
{
    #region BindableProperties

    /// <summary>ランタイムダウンロード進捗のBindableProperty。</summary>
    public static readonly BindableProperty RuntimeProgressProperty =
        BindableProperty.Create(
            nameof(RuntimeProgress),
            typeof(DownloadProgress),
            typeof(DownloadProgressView),
            defaultValue: null,
            propertyChanged: OnRuntimeProgressChanged);

    /// <summary>モデルダウンロード進捗のBindableProperty。</summary>
    public static readonly BindableProperty ModelProgressProperty =
        BindableProperty.Create(
            nameof(ModelProgress),
            typeof(DownloadProgress),
            typeof(DownloadProgressView),
            defaultValue: null,
            propertyChanged: OnModelProgressChanged);

    /// <summary>検出されたGPU情報のBindableProperty。</summary>
    public static readonly BindableProperty DetectedGpuInfoProperty =
        BindableProperty.Create(
            nameof(DetectedGpuInfo),
            typeof(string),
            typeof(DownloadProgressView),
            defaultValue: null,
            propertyChanged: OnDetectedGpuInfoChanged);

    // 表示用プロパティ（XAMLバインディング向け）
    public static readonly BindableProperty RuntimeProgressValueProperty =
        BindableProperty.Create(nameof(RuntimeProgressValue), typeof(double), typeof(DownloadProgressView), 0.0);

    public static readonly BindableProperty ModelProgressValueProperty =
        BindableProperty.Create(nameof(ModelProgressValue), typeof(double), typeof(DownloadProgressView), 0.0);

    public static readonly BindableProperty RuntimeStatusTextProperty =
        BindableProperty.Create(nameof(RuntimeStatusText), typeof(string), typeof(DownloadProgressView), "ランタイム: 待機中");

    public static readonly BindableProperty ModelStatusTextProperty =
        BindableProperty.Create(nameof(ModelStatusText), typeof(string), typeof(DownloadProgressView), "モデル: 待機中");

    public static readonly BindableProperty GpuDisplayTextProperty =
        BindableProperty.Create(nameof(GpuDisplayText), typeof(string), typeof(DownloadProgressView), "GPU: 検出中...");

    #endregion

    #region Properties

    /// <summary>Ollamaランタイムのダウンロード進捗。</summary>
    public DownloadProgress? RuntimeProgress
    {
        get => (DownloadProgress?)GetValue(RuntimeProgressProperty);
        set => SetValue(RuntimeProgressProperty, value);
    }

    /// <summary>モデルのダウンロード進捗。</summary>
    public DownloadProgress? ModelProgress
    {
        get => (DownloadProgress?)GetValue(ModelProgressProperty);
        set => SetValue(ModelProgressProperty, value);
    }

    /// <summary>検出されたGPU情報。</summary>
    public string? DetectedGpuInfo
    {
        get => (string?)GetValue(DetectedGpuInfoProperty);
        set => SetValue(DetectedGpuInfoProperty, value);
    }

    /// <summary>ランタイム進捗値 (0.0〜1.0)。</summary>
    public double RuntimeProgressValue
    {
        get => (double)GetValue(RuntimeProgressValueProperty);
        set => SetValue(RuntimeProgressValueProperty, value);
    }

    /// <summary>モデル進捗値 (0.0〜1.0)。</summary>
    public double ModelProgressValue
    {
        get => (double)GetValue(ModelProgressValueProperty);
        set => SetValue(ModelProgressValueProperty, value);
    }

    /// <summary>ランタイムステータステキスト。</summary>
    public string RuntimeStatusText
    {
        get => (string)GetValue(RuntimeStatusTextProperty);
        set => SetValue(RuntimeStatusTextProperty, value);
    }

    /// <summary>モデルステータステキスト。</summary>
    public string ModelStatusText
    {
        get => (string)GetValue(ModelStatusTextProperty);
        set => SetValue(ModelStatusTextProperty, value);
    }

    /// <summary>GPU表示テキスト。</summary>
    public string GpuDisplayText
    {
        get => (string)GetValue(GpuDisplayTextProperty);
        set => SetValue(GpuDisplayTextProperty, value);
    }

    #endregion

    public DownloadProgressView()
    {
        InitializeComponent();
    }

    #region PropertyChanged Handlers

    private static void OnRuntimeProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not DownloadProgressView view) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (newValue is not DownloadProgress progress)
            {
                view.RuntimeStatusText = "ランタイム: 待機中";
                view.RuntimeProgressValue = 0;
                return;
            }

            view.RuntimeProgressValue = (progress.Percentage ?? 0) / 100.0;

            if (progress.IsCompleted)
            {
                view.RuntimeStatusText = "ランタイム: ダウンロード完了";
            }
            else
            {
                var sizeInfo = FormatBytes(progress.BytesDownloaded, progress.TotalBytes);
                view.RuntimeStatusText = $"ランタイム: {progress.StatusMessage} ({sizeInfo})";
            }
        });
    }

    private static void OnModelProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not DownloadProgressView view) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (newValue is not DownloadProgress progress)
            {
                view.ModelStatusText = "モデル: 待機中";
                view.ModelProgressValue = 0;
                return;
            }

            view.ModelProgressValue = (progress.Percentage ?? 0) / 100.0;

            if (progress.IsCompleted)
            {
                view.ModelStatusText = "モデル: ダウンロード完了";
            }
            else
            {
                var sizeInfo = FormatBytes(progress.BytesDownloaded, progress.TotalBytes);
                view.ModelStatusText = $"モデル: {progress.StatusMessage} ({sizeInfo})";
            }
        });
    }

    private static void OnDetectedGpuInfoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not DownloadProgressView view) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            view.GpuDisplayText = string.IsNullOrEmpty(newValue as string)
                ? "GPU: 検出中..."
                : $"GPU: {newValue}";
        });
    }

    #endregion

    #region Helpers

    private static string FormatBytes(long bytes, long? totalBytes)
    {
        var downloaded = FormatByteSize(bytes);
        if (totalBytes.HasValue)
        {
            var total = FormatByteSize(totalBytes.Value);
            return $"{downloaded} / {total}";
        }
        return downloaded;
    }

    private static string FormatByteSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        var index = 0;
        var size = (double)bytes;
        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }
        return $"{size:F1} {suffixes[index]}";
    }

    #endregion
}
