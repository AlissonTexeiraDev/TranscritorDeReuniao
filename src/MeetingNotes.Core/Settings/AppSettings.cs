using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingNotes.Meeting;

namespace MeetingNotes.Settings;

/// <summary>
/// Configuração persistida em %APPDATA%\TranscritorDeReuniao\settings.json, editada pelo cliente.
/// Variáveis de ambiente MEETING_NOTES_* têm precedência (útil no servidor MCP configurado pelo Claude).
/// </summary>
public sealed class AppSettings
{
    public const string AppName = "TranscritorDeReuniao";

    public static readonly string[] DefaultWatchedProcesses =
        ["ms-teams", "Teams", "msteams", "chrome", "msedge", "firefox", "brave", "Zoom"];

    public string OutputRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingNotes");

    public string ModelsDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingNotesMcp", "models");

    public string ModelName { get; set; } = "base";
    public string Language { get; set; } = "pt";
    public string? MicDevice { get; set; }
    public bool MicDisabled { get; set; }
    public string? SystemDevice { get; set; }
    public string MyName { get; set; } = "Eu";
    public string OthersName { get; set; } = "Outros";
    public int Threads { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    public float VadThreshold { get; set; } = 0.01f;

    /// <summary>Iniciar a gravação sozinho quando um dos processos monitorados usar o microfone.</summary>
    public bool AutoDetect { get; set; } = true;

    /// <summary>Nomes de processo (sem .exe) que indicam reunião: Teams, navegadores (Meet), Zoom.</summary>
    public List<string> WatchedProcesses { get; set; } = [.. DefaultWatchedProcesses];

    /// <summary>Segundos sem uso do microfone pelo app para encerrar uma gravação iniciada automaticamente.</summary>
    public int AutoStopAfterSeconds { get; set; } = 20;

    [JsonIgnore]
    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    [JsonIgnore] public static string FilePath => Path.Combine(SettingsDirectory, "settings.json");
    [JsonIgnore] public static string LogPath => Path.Combine(SettingsDirectory, "app.log");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            settings = new AppSettings();
        }

        if (settings.WatchedProcesses.Count == 0) settings.WatchedProcesses = [.. DefaultWatchedProcesses];
        settings.ApplyEnvironmentOverrides();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    public void ApplyEnvironmentOverrides()
    {
        static string? E(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v.Trim() : null;

        if (E("MEETING_NOTES_MODEL") is { } model) ModelName = model;
        if (E("MEETING_NOTES_LANGUAGE") is { } lang) Language = lang;
        if (E("MEETING_NOTES_OUTPUT") is { } output) OutputRoot = output;
        if (E("MEETING_NOTES_MODELS_DIR") is { } models) ModelsDir = models;
        if (E("MEETING_NOTES_MIC") is { } mic)
        {
            MicDisabled = string.Equals(mic, "none", StringComparison.OrdinalIgnoreCase);
            MicDevice = MicDisabled ? null : mic;
        }
        if (E("MEETING_NOTES_SPEAKER") is { } speaker) SystemDevice = speaker;
        if (E("MEETING_NOTES_MY_NAME") is { } me) MyName = me;
        if (E("MEETING_NOTES_OTHERS_NAME") is { } others) OthersName = others;
        if (int.TryParse(E("MEETING_NOTES_THREADS"), out var t) && t > 0) Threads = t;
        if (float.TryParse(E("MEETING_NOTES_VAD_THRESHOLD"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0) VadThreshold = v;
    }

    public MeetingOptions ToMeetingOptions() => new()
    {
        ModelName = ModelName,
        Language = Language,
        OutputRoot = OutputRoot,
        ModelsDir = ModelsDir,
        MicDevice = MicDevice,
        MicDisabled = MicDisabled,
        SystemDevice = SystemDevice,
        MyName = MyName,
        OthersName = OthersName,
        Threads = Threads,
        VadThreshold = VadThreshold
    };
}
