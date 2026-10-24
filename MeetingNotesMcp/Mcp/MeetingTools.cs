using System.ComponentModel;
using System.Text;
using MeetingNotesMcp.Audio;
using MeetingNotesMcp.Meeting;
using ModelContextProtocol.Server;
using NAudio.CoreAudioApi;

namespace MeetingNotesMcp.Mcp;

[McpServerToolType]
public static class MeetingTools
{
    [McpServerTool(Name = "start_meeting"),
     Description("Inicia a gravação e a transcrição em tempo real de uma reunião. Captura o áudio que sai das caixas (outros participantes) e o microfone (você). Na primeira execução baixa o modelo Whisper. Retorna o id da reunião e a pasta onde os arquivos são gravados.")]
    public static async Task<string> StartMeeting(
        MeetingService service,
        [Description("Título da reunião, usado no nome da pasta e da ata. Opcional.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var s = await service.StartAsync(title, cancellationToken);
            return $"Reunião iniciada.\nid={s.Id}\ntitulo={s.Title}\npasta={s.Folder}\n\n{service.DescribeStatus()}\n\nUse get_transcript (com since_seq) para acompanhar e add_marker para registrar decisões e ações.";
        }
        catch (Exception ex)
        {
            return $"ERRO ao iniciar: {ex.Message}";
        }
    }

    [McpServerTool(Name = "stop_meeting"),
     Description("Encerra a gravação, transcreve o que ainda está na fila e grava transcript.md na pasta da reunião. Retorna um resumo e os caminhos dos arquivos.")]
    public static async Task<string> StopMeeting(MeetingService service, CancellationToken cancellationToken = default)
    {
        try
        {
            var s = await service.StopAsync(cancellationToken);
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

    [McpServerTool(Name = "get_status"),
     Description("Mostra se há reunião em andamento, duração, quantidade de entradas, tamanho da fila de transcrição e dispositivos em uso.")]
    public static string GetStatus(MeetingService service) => service.DescribeStatus();

    [McpServerTool(Name = "get_transcript"),
     Description("Lê a transcrição da reunião atual (ou da última encerrada neste processo). Passe since_seq com o último seq já lido para receber apenas o que é novo. Cada linha tem o formato [seq] HH:mm:ss Falante: texto. Marcadores aparecem como DECISÃO, AÇÃO, TÓPICO ou NOTA.")]
    public static string GetTranscript(
        MeetingService service,
        [Description("Retorna apenas entradas com seq maior que este valor. 0 = desde o início.")] int since_seq = 0,
        [Description("Máximo de entradas a retornar (padrão 300).")] int max_entries = 300)
    {
        var s = service.CurrentOrLast;
        if (s is null) return "Nenhuma reunião em andamento nem encerrada neste processo. Use start_meeting ou load_meeting.";
        var entries = s.GetSince(since_seq, Math.Clamp(max_entries, 1, 5000));
        var sb = new StringBuilder();
        sb.AppendLine($"reuniao={s.Id} gravando={service.IsRecording} ultimo_seq={s.LastSeq} retornadas={entries.Count} fila={service.PendingSegments}");
        foreach (var e in entries) sb.AppendLine(e.Render());
        if (entries.Count == 0) sb.AppendLine("(sem entradas novas)");
        return Lf(sb);
    }

    // StringBuilder.AppendLine usa \r\n no Windows; o modelo lê melhor com \n puro.
    private static string Lf(StringBuilder sb) => sb.ToString().Replace("\r\n", "\n").TrimEnd();

    [McpServerTool(Name = "add_marker"),
     Description("Registra um marcador com horário na transcrição: uma decisão tomada, uma ação (com responsável e prazo, se houver), um tópico/mudança de assunto ou uma nota livre.")]
    public static string AddMarker(
        MeetingService service,
        [Description("Tipo: decisao | acao | topico | nota")] string kind,
        [Description("Texto do marcador.")] string text)
    {
        var s = service.Current;
        if (s is null) return "ERRO: nenhuma reunião em andamento.";
        var k = kind.Trim().ToLowerInvariant() switch
        {
            "decisao" or "decisão" or "decision" => EntryKind.Decisao,
            "acao" or "ação" or "action" => EntryKind.Acao,
            "topico" or "tópico" or "topic" => EntryKind.Topico,
            "nota" or "note" => EntryKind.Nota,
            _ => (EntryKind?)null
        };
        if (k is null) return "ERRO: tipo inválido. Use decisao, acao, topico ou nota.";
        var e = s.Add("assistente", text.Trim(), k.Value);
        return $"Registrado: {e.Render()}";
    }

    [McpServerTool(Name = "save_minutes"),
     Description("Grava a ata da reunião (Markdown) como ata.md na pasta da reunião atual ou da última encerrada. Sobrescreve se já existir.")]
    public static string SaveMinutes(
        MeetingService service,
        [Description("Conteúdo completo da ata em Markdown.")] string markdown)
    {
        var s = service.CurrentOrLast;
        if (s is null) return "ERRO: nenhuma reunião em andamento nem encerrada neste processo.";
        var path = s.SaveMinutes(markdown);
        return $"Ata gravada em {path}";
    }

    [McpServerTool(Name = "list_meetings"),
     Description("Lista as reuniões gravadas na pasta de saída (mais recentes primeiro).")]
    public static string ListMeetings(MeetingService service)
    {
        var all = MeetingSession.ListAll(service.Options.OutputRoot);
        if (all.Count == 0) return $"Nenhuma reunião em {service.Options.OutputRoot}.";
        var sb = new StringBuilder();
        foreach (var m in all)
            sb.AppendLine($"{m.Id} | {m.Title} | {m.StartedAt:dd/MM/yyyy HH:mm} | {(m.EndedAt is null ? "em andamento?" : "encerrada")} | {m.Folder}");
        return Lf(sb);
    }

    [McpServerTool(Name = "load_meeting"),
     Description("Carrega a transcrição completa de uma reunião já gravada, pelo id retornado por list_meetings.")]
    public static string LoadMeeting(
        MeetingService service,
        [Description("Id da reunião (nome da pasta).")] string meeting_id)
    {
        var folder = Path.Combine(service.Options.OutputRoot, meeting_id);
        if (!Directory.Exists(folder)) return $"ERRO: pasta não encontrada: {folder}";
        var s = MeetingSession.Load(folder);
        var sb = new StringBuilder();
        sb.AppendLine($"reuniao={s.Id} titulo={s.Title} inicio={s.StartedAt:dd/MM/yyyy HH:mm} entradas={s.Count}");
        foreach (var e in s.All()) sb.AppendLine(e.Render());
        var ata = Path.Combine(folder, "ata.md");
        if (File.Exists(ata)) sb.AppendLine($"\n(ata existente em {ata})");
        return Lf(sb);
    }

    [McpServerTool(Name = "list_devices"),
     Description("Lista os dispositivos de áudio ativos (saídas para loopback e entradas/microfones), marcando os padrões do Windows.")]
    public static string ListDevices()
    {
        var sb = new StringBuilder();
        foreach (var d in AudioDevices.List())
            sb.AppendLine($"{(d.Flow == DataFlow.Render ? "SAÍDA " : "ENTRADA")} | {d.Name}{(d.IsDefault ? " (padrão)" : "")}");
        sb.AppendLine("\nPara escolher: MEETING_NOTES_SPEAKER=<trecho do nome> e MEETING_NOTES_MIC=<trecho do nome|none> no ambiente do servidor MCP.");
        return Lf(sb);
    }
}

[McpServerPromptType]
public static class MeetingPrompts
{
    [McpServerPrompt(Name = "gerar_ata"),
     Description("Instruções para gerar a ata da reunião atual ou da última a partir da transcrição.")]
    public static string GerarAta() =>
        """
        Você é o tomador de notas desta reunião. Siga estes passos:
        1. Chame get_transcript com since_seq=0 (repita com o último seq até não haver entradas novas).
        2. Produza uma ata em Markdown em português com as seções: Resumo (3 a 5 linhas), Participantes/fontes, Pontos discutidos, Decisões, Ações (responsável e prazo quando citados), Pendências e Próximos passos. Cite o horário [HH:mm] das decisões.
        3. Não invente conteúdo. Se algo estiver ambíguo na transcrição, marque como "(a confirmar)".
        4. Grave o resultado com save_minutes.
        """;
}
