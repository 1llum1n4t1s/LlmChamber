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
            // 選択されたモデルとバリアントを取得
            var modelId = _modelCombo.SelectedItem?.ToString() ?? "gemma4-e2b";
            var variantName = _variantCombo.SelectedItem?.ToString() ?? "Auto";
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
                if (InvokeRequired)
                    Invoke(() => _downloadProgress.RuntimeProgress = progress);
                else
                    _downloadProgress.RuntimeProgress = progress;
            };

            _llm.ModelDownloadProgress += (s, progress) =>
            {
                if (InvokeRequired)
                    Invoke(() => _downloadProgress.ModelProgress = progress);
                else
                    _downloadProgress.ModelProgress = progress;
            };

            // 初期化を実行
            await _llm.InitializeAsync();

            // チャットパネルにLLMを接続
            _chatPanel.LlmInstance = _llm;

            // UIを切り替え
            _downloadProgress.Visible = false;
            _chatPanel.Visible = true;
            _statusLabel.Text = $"接続済み: {modelId} ({variantName})";
        }
        catch (Exception ex)
        {
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
            _connectButton.Enabled = true;
        }
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        if (_llm is not null)
        {
            await _llm.DisposeAsync();
        }
    }
}
