using System.ComponentModel;
using MeetingNotes.Control;
using ModelContextProtocol.Server;

namespace MeetingNotes.Mcp;

[McpServerToolType]
public static class MeetingTools
{
    [McpServerTool(Name = "start_meeting"),
     Description("Inicia a gravação e a transcrição em tempo real de uma reunião. Captura o áudio que sai das caixas (outros participantes) e o microfone (você). Se o cliente Transcritor de Reunião estiver aberto, a gravação é feita por ele. Retorna o id da reunião e a pasta onde os arquivos são gravados.")]
    public static Task<string> StartMeeting(
        IMeetingController controller,
        [Description("Título da reunião, usado no nome da pasta e da ata. Opcional.")] string? title = null,
        CancellationToken cancellationToken = default)
        => controller.StartAsync(title, cancellationToken);

    [McpServerTool(Name = "stop_meeting"),
     Description("Encerra a gravação, transcreve o que ainda está na fila e grava transcript.md na pasta da reunião. Retorna um resumo e os caminhos dos arquivos.")]
    public static Task<string> StopMeeting(IMeetingController controller, CancellationToken cancellationToken = default)
        => controller.StopAsync(cancellationToken);

    [McpServerTool(Name = "get_status"),
     Description("Mostra se há reunião em andamento, duração, quantidade de entradas, tamanho da fila de transcrição, dispositivos em uso e quem controla a gravação (cliente de bandeja ou este processo).")]
    public static Task<string> GetStatus(IMeetingController controller, CancellationToken cancellationToken = default)
        => controller.StatusAsync(cancellationToken);

    [McpServerTool(Name = "get_transcript"),
     Description("Lê a transcrição da reunião atual (ou da última encerrada). Passe since_seq com o último seq já lido para receber apenas o que é novo. Cada linha tem o formato [seq] HH:mm:ss Falante: texto. Marcadores aparecem como DECISÃO, AÇÃO, TÓPICO ou NOTA.")]
    public static Task<string> GetTranscript(
        IMeetingController controller,
        [Description("Retorna apenas entradas com seq maior que este valor. 0 = desde o início.")] int since_seq = 0,
        [Description("Máximo de entradas a retornar (padrão 300).")] int max_entries = 300,
        CancellationToken cancellationToken = default)
        => controller.TranscriptAsync(since_seq, max_entries, cancellationToken);

    [McpServerTool(Name = "add_marker"),
     Description("Registra um marcador com horário na transcrição: uma decisão tomada, uma ação (com responsável e prazo, se houver), um tópico/mudança de assunto ou uma nota livre.")]
    public static Task<string> AddMarker(
        IMeetingController controller,
        [Description("Tipo: decisao | acao | topico | nota")] string kind,
        [Description("Texto do marcador.")] string text,
        CancellationToken cancellationToken = default)
        => controller.AddMarkerAsync(kind, text, cancellationToken);

    [McpServerTool(Name = "save_minutes"),
     Description("Grava a ata da reunião (Markdown) como ata.md na pasta da reunião atual ou da última encerrada. Sobrescreve se já existir.")]
    public static Task<string> SaveMinutes(
        IMeetingController controller,
        [Description("Conteúdo completo da ata em Markdown.")] string markdown,
        CancellationToken cancellationToken = default)
        => controller.SaveMinutesAsync(markdown, cancellationToken);

    [McpServerTool(Name = "list_meetings"),
     Description("Lista as reuniões gravadas na pasta de saída (mais recentes primeiro).")]
    public static Task<string> ListMeetings(IMeetingController controller, CancellationToken cancellationToken = default)
        => controller.ListMeetingsAsync(cancellationToken);

    [McpServerTool(Name = "load_meeting"),
     Description("Carrega a transcrição completa de uma reunião já gravada, pelo id retornado por list_meetings.")]
    public static Task<string> LoadMeeting(
        IMeetingController controller,
        [Description("Id da reunião (nome da pasta).")] string meeting_id,
        CancellationToken cancellationToken = default)
        => controller.LoadMeetingAsync(meeting_id, cancellationToken);

    [McpServerTool(Name = "list_devices"),
     Description("Lista os dispositivos de áudio ativos (saídas para loopback e entradas/microfones), marcando os padrões do Windows.")]
    public static Task<string> ListDevices(IMeetingController controller, CancellationToken cancellationToken = default)
        => controller.ListDevicesAsync(cancellationToken);
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
