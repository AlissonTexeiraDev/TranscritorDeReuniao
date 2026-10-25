namespace MeetingNotes.Meeting;

/// <summary>Opções do motor de gravação/transcrição. Construídas a partir de <see cref="Settings.AppSettings"/>.</summary>
public sealed class MeetingOptions
{
    /// <summary>tiny | base | small | medium | large | large-turbo</summary>
    public string ModelName { get; init; } = "base";

    /// <summary>Código de idioma do Whisper, ex.: pt, en, auto</summary>
    public string Language { get; init; } = "pt";

    /// <summary>Pasta onde cada reunião ganha uma subpasta</summary>
    public string OutputRoot { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeetingNotes");

    /// <summary>Pasta dos modelos ggml</summary>
    public string ModelsDir { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingNotesMcp", "models");

    /// <summary>Trecho do nome do microfone; vazio = padrão de comunicação</summary>
    public string? MicDevice { get; init; }

    public bool MicDisabled { get; init; }

    /// <summary>Trecho do nome do dispositivo de saída a capturar por loopback; vazio = padrão</summary>
    public string? SystemDevice { get; init; }

    /// <summary>Rótulo das falas do microfone</summary>
    public string MyName { get; init; } = "Eu";

    /// <summary>Rótulo das falas vindas do loopback</summary>
    public string OthersName { get; init; } = "Outros";

    /// <summary>Threads do whisper.cpp</summary>
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    /// <summary>RMS mínimo para considerar fala, 0.0 a 1.0</summary>
    public float VadThreshold { get; init; } = 0.01f;

    /// <summary>Nomes, siglas e termos (separados por vírgula) que o Whisper deve reconhecer, ex.: Jira, deploy, sprint.</summary>
    public string Vocabulary { get; init; } = "";

    /// <summary>Verdadeiro quando a troca exige recarregar o modelo Whisper.</summary>
    public bool RequiresNewTranscriber(MeetingOptions other) =>
        !string.Equals(ModelName, other.ModelName, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(Language, other.Language, StringComparison.OrdinalIgnoreCase) ||
        Threads != other.Threads ||
        !string.Equals(ModelsDir, other.ModelsDir, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(Vocabulary, other.Vocabulary, StringComparison.Ordinal);
}
