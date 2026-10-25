using System.Text;
using MeetingNotes.Audio;
using MeetingNotes.Meeting;
using NAudio.CoreAudioApi;

namespace MeetingNotes.Control;

/// <summary>Controlador sobre um <see cref="MeetingService"/> no mesmo processo.</summary>
public sealed class LocalMeetingController : IMeetingController, IDisposable
{
    private readonly MeetingService _service;
    private readonly string _controlLabel;

    /// <param name="controlLabel">Aparece no status como controle=..., para o modelo saber quem está gravando.</param>
    public LocalMeetingController(MeetingService service, string controlLabel = "local")
    {
        _service = service;
        _controlLabel = controlLabel;
    }

    public MeetingService Service => _service;

    public async Task<string> StartAsync(string? title, CancellationToken ct)
    {
        try
        {
            var s = await _service.StartAsync(title, ct);
            return $"Reunião iniciada.\nid={s.Id}\ntitulo={s.Title}\npasta={s.Folder}\n\n{Status()}\n\nUse get_transcript (com since_seq) para acompanhar e add_marker para registrar decisões e ações.";
        }
        catch (Exception ex)
        {
            return $"ERRO ao iniciar: {ex.Message}";
        }
    }

    public async Task<string> StopAsync(CancellationToken ct)
    {
        try
        {
            var s = await _service.StopAsync(ct);
            if (s is null) return "Nenhuma reunião em andamento.";
            var entries = s.All();
            var falas = entries.Count(e => e.Kind == EntryKind.Fala);
            var marcadores = entries.Count(e => e.Kind is EntryKind.Decisao or EntryKind.Acao or EntryKind.Topico);
            return $"Reunião encerrada.\nid={s.Id}\nduracao={(s.EndedAt!.Value - s.StartedAt):hh\\:mm\\:ss}\nfalas={falas} marcadores={marcadores}\ntranscricao_md={s.TranscriptMarkdownPath}\ntranscricao_jsonl={s.TranscriptJsonlPath}\n\nAgora você pode chamar get_transcript para ler tudo e save_minutes para gravar a ata.";
        }
        catch (Exception ex)
        {
            return $"ERRO ao encerrar: {ex.Message}";
        }
    }

    public Task<string> StatusAsync(CancellationToken ct) => Task.FromResult(Status());

    private string Status() => $"controle={_controlLabel}\n{_service.DescribeStatus()}";

    public Task<string> TranscriptAsync(int sinceSeq, int maxEntries, CancellationToken ct)
    {
        var s = _service.CurrentOrLast;
        if (s is null) return Task.FromResult("Nenhuma reunião em andamento nem encerrada neste processo. Use start_meeting ou load_meeting.");
        var entries = s.GetSince(sinceSeq, Math.Clamp(maxEntries, 1, 5000));
        var sb = new StringBuilder();
        sb.AppendLine($"reuniao={s.Id} gravando={_service.IsRecording} ultimo_seq={s.LastSeq} retornadas={entries.Count} fila={_service.PendingSegments}");
        foreach (var e in entries) sb.AppendLine(e.Render());
        if (entries.Count == 0) sb.AppendLine("(sem entradas novas)");
        return Task.FromResult(Lf(sb));
    }

    public Task<string> AddMarkerAsync(string kind, string text, CancellationToken ct)
    {
        var s = _service.Current;
        if (s is null) return Task.FromResult("ERRO: nenhuma reunião em andamento.");
        var k = ParseKind(kind);
        if (k is null) return Task.FromResult("ERRO: tipo inválido. Use decisao, acao, topico ou nota.");
        var e = s.Add("assistente", text.Trim(), k.Value);
        return Task.FromResult($"Registrado: {e.Render()}");
    }

    public static EntryKind? ParseKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "decisao" or "decisão" or "decision" => EntryKind.Decisao,
        "acao" or "ação" or "action" => EntryKind.Acao,
        "topico" or "tópico" or "topic" => EntryKind.Topico,
        "nota" or "note" => EntryKind.Nota,
        _ => null
    };

    public Task<string> SaveMinutesAsync(string markdown, CancellationToken ct)
    {
        var s = _service.CurrentOrLast;
        if (s is null) return Task.FromResult("ERRO: nenhuma reunião em andamento nem encerrada neste processo.");
        var path = s.SaveMinutes(markdown);
        return Task.FromResult($"Ata gravada em {path}");
    }

    public Task<string> ListMeetingsAsync(CancellationToken ct)
    {
        var all = MeetingSession.ListAll(_service.Options.OutputRoot);
        if (all.Count == 0) return Task.FromResult($"Nenhuma reunião em {_service.Options.OutputRoot}.");
        var sb = new StringBuilder();
        foreach (var m in all)
            sb.AppendLine($"{m.Id} | {m.Title} | {m.StartedAt:dd/MM/yyyy HH:mm} | {(m.EndedAt is null ? "em andamento?" : "encerrada")} | {m.Folder}");
        return Task.FromResult(Lf(sb));
    }

    public Task<string> LoadMeetingAsync(string meetingId, CancellationToken ct)
    {
        var folder = Path.Combine(_service.Options.OutputRoot, meetingId);
        if (!Directory.Exists(folder)) return Task.FromResult($"ERRO: pasta não encontrada: {folder}");
        var s = MeetingSession.Load(folder);
        var sb = new StringBuilder();
        sb.AppendLine($"reuniao={s.Id} titulo={s.Title} inicio={s.StartedAt:dd/MM/yyyy HH:mm} entradas={s.Count}");
        foreach (var e in s.All()) sb.AppendLine(e.Render());
        var ata = Path.Combine(folder, "ata.md");
        if (File.Exists(ata)) sb.AppendLine($"\n(ata existente em {ata})");
        return Task.FromResult(Lf(sb));
    }

    public Task<string> ListDevicesAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var d in AudioDevices.List())
            sb.AppendLine($"{(d.Flow == DataFlow.Render ? "SAÍDA " : "ENTRADA")} | {d.Name}{(d.IsDefault ? " (padrão)" : "")}");
        sb.AppendLine("\nPara escolher: nas configurações do cliente, ou MEETING_NOTES_SPEAKER / MEETING_NOTES_MIC no ambiente do servidor MCP.");
        return Task.FromResult(Lf(sb));
    }

    // StringBuilder.AppendLine usa \r\n no Windows; o modelo lê melhor com \n puro.
    private static string Lf(StringBuilder sb) => sb.ToString().Replace("\r\n", "\n").TrimEnd();

    public void Dispose() => _service.Dispose();
}
