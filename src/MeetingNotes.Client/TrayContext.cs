using System.Diagnostics;
using MeetingNotes.Control;
using MeetingNotes.Detection;
using MeetingNotes.Logging;
using MeetingNotes.Meeting;
using MeetingNotes.Settings;
using Microsoft.Extensions.Logging;

namespace MeetingNotes.Client;

/// <summary>
/// Dono do ícone de bandeja e do ciclo de vida: motor de gravação, canal nomeado para o servidor MCP
/// e detector automático de reuniões. A janela principal é só uma visão sobre este contexto.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    public const string AppTitle = "Transcritor de Reunião";

    private readonly ILoggerFactory _loggerFactory;
    private readonly System.Windows.Forms.Control _ui = new();
    private readonly LocalMeetingController _controller;
    private readonly PipeServer _pipe;
    private readonly MeetingDetector _detector;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _miStart;
    private readonly ToolStripMenuItem _miStop;
    private MainForm? _main;
    private bool _startedByDetector;
    private bool _busy;

    public AppSettings Settings { get; }
    public MeetingService Service { get; }
    public ILogger Log { get; }
    public bool Exiting { get; private set; }

    /// <summary>Disparado na thread da interface sempre que o estado (gravando/parado/configurações) muda.</summary>
    public event Action? StateChanged;

    public TrayContext(bool startMinimized)
    {
        _ui.CreateControl();

        Settings = AppSettings.Load();
        _loggerFactory = LoggerFactory.Create(b => b
            .AddProvider(new FileLoggerProvider(AppSettings.LogPath))
            .SetMinimumLevel(LogLevel.Information));
        Log = _loggerFactory.CreateLogger<TrayContext>();
        Log.LogInformation("{App} iniciando (minimizado={Min})", AppTitle, startMinimized);

        Service = new MeetingService(Settings.ToMeetingOptions(), _loggerFactory);
        Service.MeetingStarted += _ => Post(Refresh);
        Service.MeetingStopped += _ => Post(Refresh);

        _controller = new LocalMeetingController(Service, "cliente");
        _pipe = new PipeServer(_controller, _loggerFactory.CreateLogger<PipeServer>());
        _pipe.Start();

        _detector = new MeetingDetector(
            () => new DetectorOptions(Settings.AutoDetect, Settings.WatchedProcesses, Settings.AutoStopAfterSeconds),
            _loggerFactory.CreateLogger<MeetingDetector>());
        _detector.MeetingStarted += d => Post(() => OnMeetingDetected(d));
        _detector.MeetingEnded += d => Post(() => OnDetectedMeetingEnded(d));
        _detector.Start();

        _miStart = new ToolStripMenuItem("Iniciar gravação...", null, (_, _) => StartInteractive());
        _miStop = new ToolStripMenuItem("Parar gravação", null, (_, _) => _ = StopAsync());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Abrir", null, (_, _) => ShowMain()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miStart);
        menu.Items.Add(_miStop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Abrir pasta das gravações", null, (_, _) => OpenOutputFolder()));
        menu.Items.Add(new ToolStripMenuItem("Configurações...", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => ExitApp()));

        _tray = new NotifyIcon { Icon = Icons.Idle, Text = AppTitle, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => ShowMain();
        _tray.BalloonTipClicked += (_, _) => ShowMain();

        Refresh();
        if (startMinimized)
        {
            _tray.ShowBalloonTip(4000, AppTitle,
                Settings.AutoDetect
                    ? "Rodando na bandeja. A gravação começa sozinha quando uma reunião for detectada."
                    : "Rodando na bandeja. Clique com o botão direito para iniciar uma gravação.",
                ToolTipIcon.Info);
        }
        else
        {
            ShowMain();
        }
    }

    private void Post(Action action)
    {
        if (Exiting) return;
        if (_ui.IsHandleCreated) _ui.BeginInvoke(action);
    }

    public bool IsRecording => Service.IsRecording;
    public bool StartedByDetector => _startedByDetector;

    public void StartInteractive()
    {
        if (IsRecording) { ShowMain(); return; }
        var owner = _main is { Visible: true } ? _main : null;
        var title = TitleDialog.Ask(owner, $"Reunião {DateTime.Now:dd/MM HH:mm}");
        if (title is null) return;
        _ = StartAsync(title, byDetector: false);
    }

    public async Task StartAsync(string? title, bool byDetector)
    {
        if (IsRecording || _busy) return;
        _busy = true;
        try
        {
            _tray.Text = Clip($"{AppTitle}: iniciando...");
            var session = await Service.StartAsync(title, CancellationToken.None);
            _startedByDetector = byDetector;
            _tray.ShowBalloonTip(3000, byDetector ? "Reunião detectada" : "Gravação iniciada", session.Title, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Falha ao iniciar gravação");
            _tray.ShowBalloonTip(5000, "Não foi possível iniciar", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    public async Task StopAsync()
    {
        if (Service.Current is null || _busy) return;
        _busy = true;
        try
        {
            _tray.Text = Clip($"{AppTitle}: encerrando e transcrevendo...");
            Refresh();
            var session = await Service.StopAsync(CancellationToken.None);
            _startedByDetector = false;
            if (session is not null)
                _tray.ShowBalloonTip(4000, "Gravação salva", $"{session.Title}\n{session.Folder}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Falha ao encerrar gravação");
            _tray.ShowBalloonTip(5000, "Falha ao encerrar", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void OnMeetingDetected(DetectedMeeting d)
    {
        if (!Settings.AutoDetect || IsRecording) return;
        _ = StartAsync(d.Title, byDetector: true);
    }

    private void OnDetectedMeetingEnded(DetectedMeeting d)
    {
        if (IsRecording && _startedByDetector) _ = StopAsync();
    }

    public void SaveSettings()
    {
        try
        {
            Settings.Save();
            Service.UpdateOptions(Settings.ToMeetingOptions());
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Falha ao salvar configurações");
            MessageBox.Show($"Não foi possível salvar as configurações:\n{ex.Message}", AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Refresh();
    }

    private void Refresh()
    {
        var recording = IsRecording;
        var state = Service.State;
        _tray.Icon = recording || state == "encerrando" ? Icons.Recording : Icons.Idle;
        _miStart.Enabled = !recording && state == "parado";
        _miStop.Enabled = recording;
        if (!_busy)
        {
            _tray.Text = Clip(recording
                ? $"{AppTitle}: gravando \"{Service.Current!.Title}\""
                : $"{AppTitle}: {(Settings.AutoDetect ? "aguardando reunião" : "parado")}");
        }
        StateChanged?.Invoke();
    }

    private static string Clip(string s) => s.Length <= 63 ? s : s[..60] + "...";

    public void ShowMain()
    {
        if (_main is null || _main.IsDisposed) _main = new MainForm(this);
        _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    public void ShowSettings()
    {
        using var form = new SettingsForm(Settings);
        var owner = _main is { Visible: true } ? _main : null;
        if (form.ShowDialog(owner) == DialogResult.OK) SaveSettings();
    }

    public void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.OutputRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.OutputRoot}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Falha ao abrir a pasta");
        }
    }

    public async void ExitApp()
    {
        if (Exiting) return;
        Exiting = true;
        Log.LogInformation("Encerrando");
        _detector.Stop();
        _pipe.Dispose();
        _tray.Text = Clip($"{AppTitle}: encerrando...");
        try
        {
            if (Service.Current is not null) await Service.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Falha ao encerrar a gravação na saída");
        }
        _tray.Visible = false;
        _tray.Dispose();
        _detector.Dispose();
        Service.Dispose();
        _loggerFactory.Dispose();
        ExitThread();
    }
}
