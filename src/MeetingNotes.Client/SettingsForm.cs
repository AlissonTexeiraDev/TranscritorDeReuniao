using System.Globalization;
using MeetingNotes.Audio;
using MeetingNotes.Settings;
using NAudio.CoreAudioApi;

namespace MeetingNotes.Client;

/// <summary>Configurações avançadas: modelo, idioma, dispositivos, nomes, detecção.</summary>
internal sealed class SettingsForm : Form
{
    private const string DefaultDevice = "(padrão do Windows)";

    private readonly AppSettings _settings;
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _language = new();
    private readonly ComboBox _speaker = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _mic = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _myName = new();
    private readonly TextBox _othersName = new();
    private readonly NumericUpDown _threads = new() { Minimum = 1, Maximum = 64 };
    private readonly NumericUpDown _vad = new() { DecimalPlaces = 3, Increment = 0.005m, Minimum = 0.001m, Maximum = 0.5m };
    private readonly TextBox _vocabulary = new();
    private readonly TextBox _watched = new();
    private readonly NumericUpDown _autoStop = new() { Minimum = 5, Maximum = 600 };

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Configurações";
        Icon = Icons.Idle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(640, 530);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(12, 12, 12, 0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _model.Items.AddRange(["tiny", "base", "small", "medium", "large-turbo", "large"]);
        Row(grid, "Modelo Whisper:", _model, "small é o melhor custo-benefício para português em CPU; medium ou large só com GPU.");
        Row(grid, "Idioma:", _language, "Código do Whisper: pt, en, es... ou auto para detectar.");

        var speakers = AudioDevices.List().Where(d => d.Flow == DataFlow.Render).Select(d => d.Name).ToArray();
        var mics = AudioDevices.List().Where(d => d.Flow == DataFlow.Capture).Select(d => d.Name).ToArray();
        _speaker.Items.Add(DefaultDevice);
        _speaker.Items.AddRange(speakers);
        _mic.Items.Add(DefaultDevice);
        _mic.Items.Add("(não gravar o microfone)");
        _mic.Items.AddRange(mics);
        Row(grid, "Saída (participantes):", _speaker, "Dispositivo por onde você ouve a reunião. É capturado por loopback.");
        Row(grid, "Microfone (você):", _mic, null);

        Row(grid, "Seu nome na transcrição:", _myName, null);
        Row(grid, "Nome dos outros:", _othersName, "Rótulo das falas vindas da saída de áudio.");
        Row(grid, "Threads do Whisper:", _threads, null);
        Row(grid, "Sensibilidade de voz (RMS):", _vad, "Aumente se gravar ruído; diminua se perder falas baixas. Padrão 0,010.");
        Row(grid, "Vocabulário (nomes, siglas):", _vocabulary, "Separe por vírgula. Ajuda o Whisper a acertar nomes de pessoas, sistemas e termos técnicos.");
        Row(grid, "Apps que indicam reunião:", _watched, "Nomes de processo separados por vírgula. Detecta quando um deles usa o microfone.");
        Row(grid, "Encerrar após (s) sem áudio:", _autoStop, "Só para gravações iniciadas automaticamente.");

        var ok = new Button { Text = "Salvar", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        ok.Click += (_, e) => { if (!Apply()) DialogResult = DialogResult.None; };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12) };
        buttons.Controls.AddRange([cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(grid);
        Controls.Add(buttons);
        LoadValues();
    }

    private static void Row(TableLayoutPanel grid, string label, System.Windows.Forms.Control control, string? hint)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 10, 0) };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 3, 0, 0);
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(l, 0, row);
        if (hint is null)
        {
            grid.Controls.Add(control, 1, row);
            return;
        }
        var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 0, 0);
        panel.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f), Margin = new Padding(0, 2, 0, 4) }, 0, 1);
        grid.Controls.Add(panel, 1, row);
    }

    private void LoadValues()
    {
        _model.SelectedItem = _model.Items.Cast<string>().FirstOrDefault(m => m.Equals(_settings.ModelName, StringComparison.OrdinalIgnoreCase)) ?? "base";
        _language.Text = _settings.Language;
        _speaker.SelectedItem = _settings.SystemDevice is { Length: > 0 } sd && _speaker.Items.Contains(sd) ? sd : DefaultDevice;
        _mic.SelectedIndex = _settings.MicDisabled ? 1 : 0;
        if (!_settings.MicDisabled && _settings.MicDevice is { Length: > 0 } md && _mic.Items.Contains(md)) _mic.SelectedItem = md;
        _myName.Text = _settings.MyName;
        _othersName.Text = _settings.OthersName;
        _threads.Value = Math.Clamp(_settings.Threads, (int)_threads.Minimum, (int)_threads.Maximum);
        _vad.Value = Math.Clamp((decimal)_settings.VadThreshold, _vad.Minimum, _vad.Maximum);
        _vocabulary.Text = _settings.Vocabulary;
        _watched.Text = string.Join(", ", _settings.WatchedProcesses);
        _autoStop.Value = Math.Clamp(_settings.AutoStopAfterSeconds, (int)_autoStop.Minimum, (int)_autoStop.Maximum);
    }

    private bool Apply()
    {
        var language = _language.Text.Trim();
        if (language.Length == 0)
        {
            MessageBox.Show(this, "Informe o idioma (ex.: pt).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _settings.ModelName = (string)_model.SelectedItem!;
        _settings.Language = language;
        _settings.SystemDevice = _speaker.SelectedIndex <= 0 ? null : (string)_speaker.SelectedItem!;
        _settings.MicDisabled = _mic.SelectedIndex == 1;
        _settings.MicDevice = _mic.SelectedIndex <= 1 ? null : (string)_mic.SelectedItem!;
        _settings.MyName = _myName.Text.Trim().Length == 0 ? "Eu" : _myName.Text.Trim();
        _settings.OthersName = _othersName.Text.Trim().Length == 0 ? "Outros" : _othersName.Text.Trim();
        _settings.Threads = (int)_threads.Value;
        _settings.VadThreshold = (float)_vad.Value;
        _settings.Vocabulary = _vocabulary.Text.Trim();
        _settings.WatchedProcesses = _watched.Text
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_settings.WatchedProcesses.Count == 0) _settings.WatchedProcesses = [.. AppSettings.DefaultWatchedProcesses];
        _settings.AutoStopAfterSeconds = (int)_autoStop.Value;
        return true;
    }
}
