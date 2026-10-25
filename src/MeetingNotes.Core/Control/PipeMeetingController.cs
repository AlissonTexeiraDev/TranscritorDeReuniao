using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MeetingNotes.Control;

/// <summary>Lado cliente do canal nomeado: usado pelo servidor MCP quando o cliente de bandeja está aberto.</summary>
public sealed class PipeMeetingController : IMeetingController
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Verdadeiro se o cliente de bandeja está rodando e atendendo o canal.</summary>
    public static bool IsAvailable(int timeoutMs = 300)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut);
            pipe.Connect(timeoutMs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> SendAsync(PipeRequest request, CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, ct);
        using var reader = new StreamReader(pipe, Utf8);
        using var writer = new StreamWriter(pipe, Utf8) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, PipeProtocol.Json));
        var line = await reader.ReadLineAsync(ct)
                   ?? throw new IOException("o cliente fechou a conexão sem responder");
        var response = JsonSerializer.Deserialize<PipeResponse>(line, PipeProtocol.Json)
                       ?? throw new IOException("resposta inválida do cliente");
        return response.Ok ? response.Text : $"ERRO (cliente): {response.Text}";
    }

    public Task<string> StartAsync(string? title, CancellationToken ct) => SendAsync(new PipeRequest("start", Title: title), ct);
    public Task<string> StopAsync(CancellationToken ct) => SendAsync(new PipeRequest("stop"), ct);
    public Task<string> StatusAsync(CancellationToken ct) => SendAsync(new PipeRequest("status"), ct);
    public Task<string> TranscriptAsync(int sinceSeq, int maxEntries, CancellationToken ct) => SendAsync(new PipeRequest("transcript", SinceSeq: sinceSeq, MaxEntries: maxEntries), ct);
    public Task<string> AddMarkerAsync(string kind, string text, CancellationToken ct) => SendAsync(new PipeRequest("marker", Kind: kind, Text: text), ct);
    public Task<string> SaveMinutesAsync(string markdown, CancellationToken ct) => SendAsync(new PipeRequest("minutes", Markdown: markdown), ct);
    public Task<string> ListMeetingsAsync(CancellationToken ct) => SendAsync(new PipeRequest("list"), ct);
    public Task<string> LoadMeetingAsync(string meetingId, CancellationToken ct) => SendAsync(new PipeRequest("load", MeetingId: meetingId), ct);
    public Task<string> ListDevicesAsync(CancellationToken ct) => SendAsync(new PipeRequest("devices"), ct);
}
