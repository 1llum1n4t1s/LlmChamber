using System.ComponentModel;

namespace LlmChamber.WinForms.Controls;

/// <summary>
/// ランタイムおよびモデルのダウンロード進捗を表示するPanel。
/// </summary>
public class DownloadProgressPanel : Panel
{
    private readonly Label _gpuInfoLabel;
    private readonly Label _runtimeStatusLabel;
    private readonly ProgressBar _runtimeProgressBar;
    private readonly Label _modelStatusLabel;
    private readonly ProgressBar _modelProgressBar;

    private DownloadProgress? _runtimeProgress;
    private DownloadProgress? _modelProgress;
    private string? _detectedGpuInfo;

    /// <summary>Ollamaランタイムのダウンロード進捗。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DownloadProgress? RuntimeProgress
    {
        get => _runtimeProgress;
        set
        {
            _runtimeProgress = value;
            UpdateRuntimeProgress();
        }
    }

    /// <summary>モデルのダウンロード進捗。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DownloadProgress? ModelProgress
    {
        get => _modelProgress;
        set
        {
            _modelProgress = value;
            UpdateModelProgress();
        }
    }

    /// <summary>検出されたGPU情報。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? DetectedGpuInfo
    {
        get => _detectedGpuInfo;
        set
        {
            _detectedGpuInfo = value;
            UpdateGpuInfo();
        }
    }

    public DownloadProgressPanel()
    {
        AutoSize = true;
        Padding = new Padding(8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            AutoSize = true,
        };

        // GPU情報ラベル
        _gpuInfoLabel = new Label
        {
            Text = "GPU: 検出中...",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 8),
        };

        // ランタイムダウンロード
        _runtimeStatusLabel = new Label
        {
            Text = "ランタイム: 待機中",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 0, 2),
        };

        _runtimeProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 20,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0, 0, 0, 8),
        };

        // モデルダウンロード
        _modelStatusLabel = new Label
        {
            Text = "モデル: 待機中",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 0, 2),
        };

        _modelProgressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 20,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
        };

        layout.Controls.Add(_gpuInfoLabel, 0, 0);
        layout.Controls.Add(_runtimeStatusLabel, 0, 1);
        layout.Controls.Add(_runtimeProgressBar, 0, 2);
        layout.Controls.Add(_modelStatusLabel, 0, 3);
        layout.Controls.Add(_modelProgressBar, 0, 4);

        Controls.Add(layout);
    }

    private void UpdateRuntimeProgress()
    {
        void DoUpdate()
        {
            if (_runtimeProgress == null)
            {
                _runtimeStatusLabel.Text = "ランタイム: 待機中";
                _runtimeProgressBar.Value = 0;
                return;
            }

            var progress = _runtimeProgress;
            var percentage = (int)(progress.Percentage ?? 0);
            _runtimeProgressBar.Value = Math.Clamp(percentage, 0, 100);

            if (progress.IsCompleted)
            {
                _runtimeStatusLabel.Text = "ランタイム: ダウンロード完了";
                _runtimeStatusLabel.ForeColor = Color.Green;
            }
            else
            {
                var sizeInfo = FormatBytes(progress.BytesDownloaded, progress.TotalBytes);
                _runtimeStatusLabel.Text = $"ランタイム: {progress.StatusMessage} ({sizeInfo})";
                _runtimeStatusLabel.ForeColor = SystemColors.ControlText;
            }
        }

        if (InvokeRequired)
            Invoke(DoUpdate);
        else
            DoUpdate();
    }

    private void UpdateModelProgress()
    {
        void DoUpdate()
        {
            if (_modelProgress == null)
            {
                _modelStatusLabel.Text = "モデル: 待機中";
                _modelProgressBar.Value = 0;
                return;
            }

            var progress = _modelProgress;
            var percentage = (int)(progress.Percentage ?? 0);
            _modelProgressBar.Value = Math.Clamp(percentage, 0, 100);

            if (progress.IsCompleted)
            {
                _modelStatusLabel.Text = "モデル: ダウンロード完了";
                _modelStatusLabel.ForeColor = Color.Green;
            }
            else
            {
                var sizeInfo = FormatBytes(progress.BytesDownloaded, progress.TotalBytes);
                _modelStatusLabel.Text = $"モデル: {progress.StatusMessage} ({sizeInfo})";
                _modelStatusLabel.ForeColor = SystemColors.ControlText;
            }
        }

        if (InvokeRequired)
            Invoke(DoUpdate);
        else
            DoUpdate();
    }

    private void UpdateGpuInfo()
    {
        void DoUpdate()
        {
            _gpuInfoLabel.Text = string.IsNullOrEmpty(_detectedGpuInfo)
                ? "GPU: 検出中..."
                : $"GPU: {_detectedGpuInfo}";
        }

        if (InvokeRequired)
            Invoke(DoUpdate);
        else
            DoUpdate();
    }

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
}
