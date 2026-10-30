using LlmChamber;
using LlmChamber.WinForms.Controls;

namespace Sample.WinForms;

public class MainForm : Form
{
    private readonly ToolStripComboBox _modelCombo;
    private readonly ToolStripComboBox _variantCombo;
    private readonly ToolStripButton _connectButton;
    private readonly DownloadProgressPanel _downloadProgress;
    private readonly ChatPanel _chatPanel;
    private readonly ToolStripStatusLabel _statusLabel;
    private ILocalLlm? _llm;
    private bool _closed;
    private CancellationTokenSource? _initializeCts;
    private Task? _initializeTask;
    private Task? _disposeTask;
    private bool _cleanupComplete;

    public MainForm()
    {
        Text = "LlmChamber - WinForms Sample";
        Size = new System.Drawing.Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;

        // ── ToolStrip ──
        var toolStrip = new ToolStrip();

        toolStrip.Items.Add(new ToolStripLabel("モデル:"));
        _modelCombo = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _modelCombo.Items.AddRange(["gemma4-e2b", "gemma4-e4b", "qwen3.5-2b", "phi4-mini"]);
        _modelCombo.SelectedIndex = 0;
        toolStrip.Items.Add(_modelCombo);

        toolStrip.Items.Add(new ToolStripSeparator());

        toolStrip.Items.Add(new ToolStripLabel("バリアント:"));
        _variantCombo = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _variantCombo.Items.AddRange(["Auto", "Full", "Rocm", "CpuOnly"]);
        _variantCombo.SelectedIndex = 0;
        toolStrip.Items.Add(_variantCombo);

        toolStrip.Items.Add(new ToolStripSeparator());

        _connectButton = new ToolStripButton("接続");
        _connectButton.Click += ConnectButton_Click;
        toolStrip.Items.Add(_connectButton);

        // ── StatusStrip ──
        var statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("未接続");
        statusStrip.Items.Add(_statusLabel);

        // ── DownloadProgressPanel ──
        _downloadProgress = new DownloadProgressPanel
        {
            Dock = DockStyle.Top,
            Visible = false,
            Height = 120,
        };

        // ── ChatPanel ──
        _chatPanel = new ChatPanel
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };

        // コントロール配置（追加順序に注意: Dock=Fillは最後）
        Controls.Add(_chatPanel);
        Controls.Add(_downloadProgress);
        Controls.Add(statusStrip);
        Controls.Add(toolStrip);
    }

    private async void ConnectButton_Click(object? sender, EventArgs e)
    {
        _connectButton.Enabled = false;
        _statusLabel.Text = "初期化中...";
        _downloadProgress.Visible = true;
        _chatPanel.Visible = false;

        try
        {
            _chatPanel.LlmInstance = null;
            var previous = _llm;
            _llm = null;
            if (previous is not null) await DisposeOwnedAsync(previous);
            if (_closed) return;
            // 選択されたモデルとバリアントを取得
            var modelId = _modelCombo.SelectedItem?.ToString() ?? "gemma4-e2b";
            var variantName = _variantCombo.SelectedItem?.ToString() ?? "Auto";
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
                if (InvokeRequired)
                    Invoke(() => { if (!_closed && ReferenceEquals(_llm, llm)) _downloadProgress.RuntimeProgress = progress; });
                else
                    if (!_closed && ReferenceEquals(_llm, llm)) _downloadProgress.RuntimeProgress = progress;
            };

            llm.ModelDownloadProgress += (s, progress) =>
            {
                if (InvokeRequired)
                    Invoke(() => { if (!_closed && ReferenceEquals(_llm, llm)) _downloadProgress.ModelProgress = progress; });
                else
                    if (!_closed && ReferenceEquals(_llm, llm)) _downloadProgress.ModelProgress = progress;
            };

            // 初期化を実行
            _initializeCts = new CancellationTokenSource();
            _initializeTask = llm.InitializeAsync(_initializeCts.Token);
            await _initializeTask;
            if (_closed) return;

            // チャットパネルにLLMを接続
            _chatPanel.LlmInstance = llm;

            // UIを切り替え
            _downloadProgress.Visible = false;
            _chatPanel.Visible = true;
            _statusLabel.Text = $"接続済み: {modelId} ({variantName})";
        }
        catch (Exception ex)
        {
            if (!_closed) _chatPanel.LlmInstance = null;
            var failed = _llm;
            _llm = null;
            if (failed is not null) await DisposeOwnedAsync(failed);
            if (_closed) return;
            _downloadProgress.Visible = false;
            _statusLabel.Text = "接続失敗";
            MessageBox.Show(
                $"初期化に失敗しました:\n{ex.Message}",
                "エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _initializeCts?.Dispose();
            _initializeCts = null;
            _initializeTask = null;
            if (!_closed) _connectButton.Enabled = true;
        }
    }

    private async Task DisposeOwnedAsync(ILocalLlm llm)
    {
        var task = llm.DisposeAsync().AsTask();
        _disposeTask = task;
        try { await task; }
        finally { _disposeTask = null; }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _cleanupComplete) return;
        e.Cancel = true;
        if (_closed) return;
        _closed = true;
        _chatPanel.LlmInstance = null;
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
