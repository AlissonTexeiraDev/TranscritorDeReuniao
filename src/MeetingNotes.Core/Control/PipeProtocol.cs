using System.Text.Encodings.Web;
using System.Text.Json;

namespace MeetingNotes.Control;

/// <summary>
/// Protocolo do canal nomeado entre o servidor MCP e o cliente de bandeja:
/// uma linha JSON de requisição, uma linha JSON de resposta.
/// </summary>
public static class PipeProtocol
{
    public const string PipeName = "TranscritorDeReuniao.Control";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

public sealed record PipeRequest(
    string Cmd,
    string? Title = null,
    string? Kind = null,
    string? Text = null,
    int SinceSeq = 0,
    int MaxEntries = 300,
    string? Markdown = null,
    string? MeetingId = null);

public sealed record PipeResponse(bool Ok, string Text);
