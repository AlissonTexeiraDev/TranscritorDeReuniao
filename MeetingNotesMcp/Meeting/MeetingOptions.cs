namespace MeetingNotesMcp.Meeting;

/// <summary>Configuração lida de variáveis de ambiente (definidas no cliente MCP ou no shell).</summary>
public sealed class MeetingOptions
{
    /// <summary>tiny | base | small | medium | large | large-turbo (MEETING_NOTES_MODEL)</summary>
    public string ModelName { get; init; } = "base";

    /// <summary>Código de idioma do Whisper, ex.: pt, en, auto (MEETING_NOTES_LANGUAGE)</summary>
    public string Language { get; init; } = "pt";

    /// <summary>Pasta onde cada reunião ganha uma subpasta (MEETING_NOTES_OUTPUT)</summary>
    public string OutputRoot { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingNotes");

    /// <summary>Pasta dos modelos ggml (MEETING_NOTES_MODELS_DIR)</summary>
    public string ModelsDir { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingNotesMcp", "models");

    /// <summary>Trecho do nome do microfone; vazio = padrão de comunicação; "none" desliga (MEETING_NOTES_MIC)</summary>
    public string? MicDevice { get; init; }

    public bool MicDisabled { get; init; }

    /// <summary>Trecho do nome do dispositivo de saída a capturar por loopback; vazio = padrão (MEETING_NOTES_SPEAKER)</summary>
    public string? SystemDevice { get; init; }

    /// <summary>Rótulo das falas do microfone (MEETING_NOTES_MY_NAME)</summary>
    public string MyName { get; init; } = "Eu";

    /// <summary>Rótulo das falas vindas do loopback (MEETING_NOTES_OTHERS_NAME)</summary>
    public string OthersName { get; init; } = "Outros";

    /// <summary>Threads do whisper.cpp (MEETING_NOTES_THREADS)</summary>
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    /// <summary>RMS mínimo para considerar fala, 0.0 a 1.0 (MEETING_NOTES_VAD_THRESHOLD)</summary>
    public float VadThreshold { get; init; } = 0.01f;

    public static MeetingOptions FromEnvironment()
    {
        var d = new MeetingOptions();
        static string? E(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v.Trim() : null;

        var mic = E("MEETING_NOTES_MIC");
        return new MeetingOptions
        {
            ModelName = E("MEETING_NOTES_MODEL") ?? d.ModelName,
            Language = E("MEETING_NOTES_LANGUAGE") ?? d.Language,
            OutputRoot = E("MEETING_NOTES_OUTPUT") ?? d.OutputRoot,
            ModelsDir = E("MEETING_NOTES_MODELS_DIR") ?? d.ModelsDir,
            MicDevice = string.Equals(mic, "none", StringComparison.OrdinalIgnoreCase) ? null : mic,
            MicDisabled = string.Equals(mic, "none", StringComparison.OrdinalIgnoreCase),
            SystemDevice = E("MEETING_NOTES_SPEAKER"),
            MyName = E("MEETING_NOTES_MY_NAME") ?? d.MyName,
            OthersName = E("MEETING_NOTES_OTHERS_NAME") ?? d.OthersName,
            Threads = int.TryParse(E("MEETING_NOTES_THREADS"), out var t) && t > 0 ? t : d.Threads,
            VadThreshold = float.TryParse(E("MEETING_NOTES_VAD_THRESHOLD"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : d.VadThreshold
        };
    }
}
