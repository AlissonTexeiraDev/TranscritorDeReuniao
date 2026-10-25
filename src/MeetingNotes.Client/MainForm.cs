using MeetingNotes.Startup;
using Microsoft.Extensions.Logging;

namespace MeetingNotes.Client;

/// <summary>Janela principal: estado, transcrição ao vivo, pasta das gravações e opções rápidas.</summary>
internal sealed class MainForm : Form
{
    private readonly TrayContext _ctx;
    private readonly Label _status = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnOpenFolder = new();
    private readonly Button _btnSettings = new();
    private readonly TextBox _transcript = new();
    private readonly TextBox _folder = new();
    private readonly Button _btnChangeFolder = new();
    private readonly CheckBox _chkAuto = new();
    private readonly CheckBox _chkStartup = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private string? _sessionId;
    private int _lastSeq;
    private bool _suppressEvents;

    public MainForm(TrayContext ctx)
    {
        _ctx = ctx;
        Text = TrayContext.AppTitle;
        Icon = Icons.Idle;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(560, 400);
        Font = new Font("Segoe UI", 9.5f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI Semibold", 12f);
        _status.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(_status, 0, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
        Setup(_btnStart, "Iniciar gravação...", (_, _) => _ctx.StartInteractive());
        Setup(_btnStop, "Parar gravação", (_, _) => _ = _ctx.StopAsync());
        Setup(_btnOpenFolder, "Abrir pasta das gravações", (_, _) => _ctx.OpenOutputFolder());
        Setup(_btnSettings, "Configurações...", (_, _) => _ctx.ShowSettings());
        buttons.Controls.AddRange([_btnStart, _btnStop, _btnOpenFolder, _btnSettings]);
        root.Controls.Add(buttons, 0, 1);

        _transcript.Multiline = true;
        _transcript.ReadOnly = true;
        _transcript.ScrollBars = ScrollBars.Vertical;
        _transcript.Dock = DockStyle.Fill;
        _transcript.Font = new Font("Consolas", 10f);
        _transcript.BackColor = Color.White;
        _transcript.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_transcript, 0, 2);

        var folderRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Margin = new Padding(0, 0, 0, 6) };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var folderLabel = new Label { Text = "Pasta das gravações:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
        _folder.ReadOnly = true;
        _folder.Dock = DockStyle.Fill;
        _folder.Margin = new Padding(0, 2, 8, 0);
        Setup(_btnChangeFolder, "Alterar...", (_, _) => ChangeFolder());
        _btnChangeFolder.Margin = new Padding(0);
        folderRow.Controls.Add(folderLabel, 0, 0);
        folderRow.Controls.Add(_folder, 1, 0);
        folderRow.Controls.Add(_btnChangeFolder, 2, 0);
        root.Controls.Add(folderRow, 0, 3);

        var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight };
        _chkAuto.Text = "Detectar reuniões automaticamente (Teams, Meet, Zoom)";
        _chkAuto.AutoSize = true;
        _chkAuto.Margin = new Padding(0, 0, 24, 0);
        _chkAuto.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _ctx.Settings.AutoDetect = _chkAuto.Checked;
            _ctx.SaveSettings();
        };
        _chkStartup.Text = "Iniciar com o Windows";
        _chkStartup.AutoSize = true;
        _chkStartup.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            try
            {
                StartupRegistration.Set(_chkStartup.Checked, Application.ExecutablePath);
                _ctx.Log.LogInformation("Iniciar com o Windows alterado pelo usuário: {Enabled} ({Path})", _chkStartup.Checked, Application.ExecutablePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível alterar a inicialização com o Windows:\n{ex.Message}", TrayContext.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        checks.Controls.AddRange([_chkAuto, _chkStartup]);
        root.Controls.Add(checks, 0, 4);

        Controls.Add(root);

        _ctx.StateChanged += OnStateChanged;
        _timer.Interval = 1000;
        _timer.Tick += (_, _) => { UpdateStatus(); AppendNewEntries(); };
        _timer.Start();

        FormClosing += (_, e) =>
        {
            if (!_ctx.Exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        FormClosed += (_, _) => { _timer.Stop(); _ctx.StateChanged -= OnStateChanged; };

        LoadFromSettings();
        UpdateStatus();
        AppendNewEntries();
    }

    private static void Setup(Button b, string text, EventHandler onClick)
    {
        b.Text = text;
        b.AutoSize = true;
        b.Padding = new Padding(6, 2, 6, 2);
        b.Margin = new Padding(0, 0, 8, 0);
        b.Click += onClick;
    }

    private void LoadFromSettings()
    {
        _suppressEvents = true;
        _folder.Text = _ctx.Settings.OutputRoot;
        _chkAuto.Checked = _ctx.Settings.AutoDetect;
        try { _chkStartup.Checked = StartupRegistration.IsEnabled(); } catch { _chkStartup.Checked = false; }
        _suppressEvents = false;
    }

    private void OnStateChanged()
    {
        LoadFromSettings();
        UpdateStatus();
        Icon = _ctx.IsRecording ? Icons.Recording : Icons.Idle;
    }

    private void UpdateStatus()
    {
        var s = _ctx.Service.Current;
        var state = _ctx.Service.State;
        if (s is not null && state == "gravando")
        {
            var dur = DateTime.Now - s.StartedAt;
            var fila = _ctx.Service.PendingSegments;
            _status.ForeColor = Color.FromArgb(200, 30, 30);
            _status.Text = $"● Gravando: {s.Title}   {dur:hh\\:mm\\:ss}   ({s.Count} entradas{(fila > 0 ? $", {fila} na fila" : "")}){(_ctx.StartedByDetector ? "   [automático]" : "")}";
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
        }
        else if (state is "iniciando" or "encerrando")
        {
            _status.ForeColor = Color.FromArgb(180, 120, 0);
            _status.Text = state == "iniciando" ? "● Iniciando (carregando modelo e dispositivos)..." : "● Encerrando e transcrevendo o restante...";
            _btnStart.Enabled = false;
            _btnStop.Enabled = false;
        }
        else if (_ctx.Service.IsPreloading)
        {
            _status.ForeColor = Color.FromArgb(180, 120, 0);
            _status.Text = $"● Preparando o modelo de transcrição ({_ctx.Settings.ModelName})... Pode iniciar; a gravação começa assim que ele estiver pronto.";
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
        }
        else
        {
            _status.ForeColor = Color.FromArgb(90, 90, 90);
            _status.Text = _ctx.Settings.AutoDetect
                ? "● Parado. Aguardando uma reunião no Teams, Meet ou Zoom..."
                : "● Parado. Detecção automática desligada.";
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
        }
    }

    private void AppendNewEntries()
    {
        var s = _ctx.Service.CurrentOrLast;
        if (s is null) return;
        if (_sessionId != s.Id)
        {
            _sessionId = s.Id;
            _lastSeq = 0;
            _transcript.Clear();
        }
        var entries = s.GetSince(_lastSeq, 500);
        if (entries.Count == 0) return;
        var text = string.Join(Environment.NewLine, entries.Select(e => e.Render())) + Environment.NewLine;
        _transcript.AppendText(text);
        _lastSeq = entries[^1].Seq;
    }

    private void ChangeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Escolha a pasta onde as gravações e atas serão salvas",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_ctx.Settings.OutputRoot) ? _ctx.Settings.OutputRoot : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _ctx.Settings.OutputRoot = dialog.SelectedPath;
        _ctx.SaveSettings();
    }
}
