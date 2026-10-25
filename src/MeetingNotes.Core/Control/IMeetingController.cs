namespace MeetingNotes.Control;

/// <summary>
/// Operações de uma reunião em formato de texto pronto para o modelo.
/// Implementado localmente (motor no mesmo processo) ou via canal nomeado (cliente de bandeja).
/// </summary>
public interface IMeetingController
{
    Task<string> StartAsync(string? title, CancellationToken ct);
    Task<string> StopAsync(CancellationToken ct);
    Task<string> StatusAsync(CancellationToken ct);
    Task<string> TranscriptAsync(int sinceSeq, int maxEntries, CancellationToken ct);
    Task<string> AddMarkerAsync(string kind, string text, CancellationToken ct);
    Task<string> SaveMinutesAsync(string markdown, CancellationToken ct);
    Task<string> ListMeetingsAsync(CancellationToken ct);
    Task<string> LoadMeetingAsync(string meetingId, CancellationToken ct);
    Task<string> ListDevicesAsync(CancellationToken ct);
}
