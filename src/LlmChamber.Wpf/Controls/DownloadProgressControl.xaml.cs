using System.Windows;
using System.Windows.Controls;

namespace LlmChamber.Wpf.Controls;

/// <summary>
/// ランタイム・モデルのダウンロード進捗を表示するコントロール。
/// </summary>
public partial class DownloadProgressControl : UserControl
{
    // ── DependencyProperties ──

    /// <summary>ランタイムダウンロードの進捗。</summary>
    public static readonly DependencyProperty RuntimeProgressProperty =
        DependencyProperty.Register(
            nameof(RuntimeProgress),
            typeof(DownloadProgress),
            typeof(DownloadProgressControl),
            new PropertyMetadata(null, OnRuntimeProgressChanged));

    /// <summary>モデルダウンロードの進捗。</summary>
    public static readonly DependencyProperty ModelProgressProperty =
        DependencyProperty.Register(
            nameof(ModelProgress),
            typeof(DownloadProgress),
            typeof(DownloadProgressControl),
            new PropertyMetadata(null, OnModelProgressChanged));

    /// <summary>検出されたGPU情報の文字列。</summary>
    public static readonly DependencyProperty DetectedGpuInfoProperty =
        DependencyProperty.Register(
            nameof(DetectedGpuInfo),
            typeof(string),
            typeof(DownloadProgressControl),
            new PropertyMetadata("-", OnDetectedGpuInfoChanged));

    /// <inheritdoc cref="RuntimeProgressProperty"/>
    public DownloadProgress? RuntimeProgress
    {
        get => (DownloadProgress?)GetValue(RuntimeProgressProperty);
        set => SetValue(RuntimeProgressProperty, value);
    }

    /// <inheritdoc cref="ModelProgressProperty"/>
    public DownloadProgress? ModelProgress
    {
        get => (DownloadProgress?)GetValue(ModelProgressProperty);
        set => SetValue(ModelProgressProperty, value);
    }

    /// <inheritdoc cref="DetectedGpuInfoProperty"/>
    public string? DetectedGpuInfo
    {
        get => (string?)GetValue(DetectedGpuInfoProperty);
        set => SetValue(DetectedGpuInfoProperty, value);
    }

    // ── コンストラクター ──

    /// <summary>インスタンスを初期化する。</summary>
    public DownloadProgressControl()
    {
        InitializeComponent();
    }

    // ── PropertyChanged コールバック ──

    private static void OnRuntimeProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DownloadProgressControl self) return;
        self.UpdateRuntimeUI();
    }

    private static void OnModelProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DownloadProgressControl self) return;
        self.UpdateModelUI();
    }

    private static void OnDetectedGpuInfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DownloadProgressControl self) return;
        self.GpuInfoRun.Text = e.NewValue as string ?? "-";
    }

    // ── UI更新 ──

    private void UpdateRuntimeUI()
    {
        var progress = RuntimeProgress;
        if (progress is null)
        {
            RuntimeProgressBar.IsIndeterminate = false;
            RuntimeProgressBar.Value = 0;
            RuntimeStatusText.Text = "待機中...";
            return;
        }

        if (progress.Percentage.HasValue)
        {
            RuntimeProgressBar.IsIndeterminate = false;
            RuntimeProgressBar.Value = progress.Percentage.Value;
        }
        else
        {
            RuntimeProgressBar.IsIndeterminate = true;
        }

        RuntimeStatusText.Text = FormatStatus(progress);
    }

    private void UpdateModelUI()
    {
        var progress = ModelProgress;
        if (progress is null)
        {
            ModelProgressBar.IsIndeterminate = false;
            ModelProgressBar.Value = 0;
            ModelStatusText.Text = "待機中...";
            return;
        }

        if (progress.Percentage.HasValue)
        {
            ModelProgressBar.IsIndeterminate = false;
            ModelProgressBar.Value = progress.Percentage.Value;
        }
        else
        {
            ModelProgressBar.IsIndeterminate = true;
        }

        ModelStatusText.Text = FormatStatus(progress);
    }

    private static string FormatStatus(DownloadProgress progress)
    {
        if (progress.IsCompleted)
            return "完了";

        var downloaded = FormatBytes(progress.BytesDownloaded);

        if (progress.TotalBytes.HasValue)
        {
            var total = FormatBytes(progress.TotalBytes.Value);
            var pct = progress.Percentage?.ToString("F1") ?? "?";
            return $"{progress.StatusMessage} ({downloaded} / {total}, {pct}%)";
        }

        return $"{progress.StatusMessage} ({downloaded})";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
        };
    }
}
