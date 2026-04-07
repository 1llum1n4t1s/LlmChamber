using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LlmChamber.Avalonia.Controls;

/// <summary>
/// ランタイムおよびモデルのダウンロード進捗を表示するコントロール。
/// </summary>
public partial class DownloadProgressControl : UserControl
{
    /// <summary>ランタイムのダウンロード進捗。</summary>
    public static readonly StyledProperty<DownloadProgress?> RuntimeProgressProperty =
        AvaloniaProperty.Register<DownloadProgressControl, DownloadProgress?>(nameof(RuntimeProgress));

    /// <summary>モデルのダウンロード進捗。</summary>
    public static readonly StyledProperty<DownloadProgress?> ModelProgressProperty =
        AvaloniaProperty.Register<DownloadProgressControl, DownloadProgress?>(nameof(ModelProgress));

    /// <summary>検出されたGPU情報。</summary>
    public static readonly StyledProperty<string?> DetectedGpuInfoProperty =
        AvaloniaProperty.Register<DownloadProgressControl, string?>(nameof(DetectedGpuInfo));

    /// <summary>ランタイムのダウンロード進捗。</summary>
    public DownloadProgress? RuntimeProgress
    {
        get => GetValue(RuntimeProgressProperty);
        set => SetValue(RuntimeProgressProperty, value);
    }

    /// <summary>モデルのダウンロード進捗。</summary>
    public DownloadProgress? ModelProgress
    {
        get => GetValue(ModelProgressProperty);
        set => SetValue(ModelProgressProperty, value);
    }

    /// <summary>検出されたGPU情報。</summary>
    public string? DetectedGpuInfo
    {
        get => GetValue(DetectedGpuInfoProperty);
        set => SetValue(DetectedGpuInfoProperty, value);
    }

    // UI要素
    private StackPanel? _gpuInfoPanel;
    private TextBlock? _gpuInfoText;
    private StackPanel? _runtimeSection;
    private ProgressBar? _runtimeProgressBar;
    private TextBlock? _runtimeStatusText;
    private StackPanel? _modelSection;
    private ProgressBar? _modelProgressBar;
    private TextBlock? _modelStatusText;
    private TextBlock? _idleText;

    public DownloadProgressControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _gpuInfoPanel = this.FindControl<StackPanel>("GpuInfoPanel");
        _gpuInfoText = this.FindControl<TextBlock>("GpuInfoText");
        _runtimeSection = this.FindControl<StackPanel>("RuntimeSection");
        _runtimeProgressBar = this.FindControl<ProgressBar>("RuntimeProgressBar");
        _runtimeStatusText = this.FindControl<TextBlock>("RuntimeStatusText");
        _modelSection = this.FindControl<StackPanel>("ModelSection");
        _modelProgressBar = this.FindControl<ProgressBar>("ModelProgressBar");
        _modelStatusText = this.FindControl<TextBlock>("ModelStatusText");
        _idleText = this.FindControl<TextBlock>("IdleText");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RuntimeProgressProperty)
        {
            UpdateRuntimeProgress(change.GetNewValue<DownloadProgress?>());
        }
        else if (change.Property == ModelProgressProperty)
        {
            UpdateModelProgress(change.GetNewValue<DownloadProgress?>());
        }
        else if (change.Property == DetectedGpuInfoProperty)
        {
            UpdateGpuInfo(change.GetNewValue<string?>());
        }
    }

    private void UpdateRuntimeProgress(DownloadProgress? progress)
    {
        if (_runtimeSection == null || _runtimeProgressBar == null || _runtimeStatusText == null)
            return;

        if (progress == null)
        {
            _runtimeSection.IsVisible = false;
            UpdateIdleVisibility();
            return;
        }

        _runtimeSection.IsVisible = true;
        _runtimeProgressBar.Value = progress.Percentage ?? 0;
        _runtimeProgressBar.IsIndeterminate = !progress.Percentage.HasValue;
        _runtimeStatusText.Text = FormatStatusText(progress);
        UpdateIdleVisibility();
    }

    private void UpdateModelProgress(DownloadProgress? progress)
    {
        if (_modelSection == null || _modelProgressBar == null || _modelStatusText == null)
            return;

        if (progress == null)
        {
            _modelSection.IsVisible = false;
            UpdateIdleVisibility();
            return;
        }

        _modelSection.IsVisible = true;
        _modelProgressBar.Value = progress.Percentage ?? 0;
        _modelProgressBar.IsIndeterminate = !progress.Percentage.HasValue;
        _modelStatusText.Text = FormatStatusText(progress);
        UpdateIdleVisibility();
    }

    private void UpdateGpuInfo(string? gpuInfo)
    {
        if (_gpuInfoPanel == null || _gpuInfoText == null)
            return;

        if (string.IsNullOrEmpty(gpuInfo))
        {
            _gpuInfoPanel.IsVisible = false;
            return;
        }

        _gpuInfoPanel.IsVisible = true;
        _gpuInfoText.Text = gpuInfo;
        UpdateIdleVisibility();
    }

    private void UpdateIdleVisibility()
    {
        if (_idleText == null) return;

        var hasContent = (_runtimeSection?.IsVisible ?? false)
                      || (_modelSection?.IsVisible ?? false)
                      || (_gpuInfoPanel?.IsVisible ?? false);
        _idleText.IsVisible = !hasContent;
    }

    private static string FormatStatusText(DownloadProgress progress)
    {
        var downloaded = FormatBytes(progress.BytesDownloaded);
        if (progress.TotalBytes.HasValue)
        {
            var total = FormatBytes(progress.TotalBytes.Value);
            var pct = progress.Percentage?.ToString("F1") ?? "?";
            return $"{progress.StatusMessage} - {downloaded} / {total} ({pct}%)";
        }
        return $"{progress.StatusMessage} - {downloaded}";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B",
        };
    }
}
