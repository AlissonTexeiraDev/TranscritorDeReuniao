using Microsoft.Extensions.Logging;

namespace MeetingNotes.Control;

/// <summary>
/// Usa o cliente de bandeja (canal nomeado) quando ele está aberto; caso contrário, grava no próprio processo.
/// A escolha é feita a cada chamada, então abrir o cliente depois também funciona.
/// </summary>
public sealed class CompositeMeetingController : IMeetingController, IDisposable
{
    private readonly Func<LocalMeetingController> _localFactory;
    private readonly ILogger _log;
    private readonly PipeMeetingController _pipe = new();
    private LocalMeetingController? _local;
    private bool? _lastMode;

    public CompositeMeetingController(Func<LocalMeetingController> localFactory, ILogger log)
    {
        _localFactory = localFactory;
        _log = log;
    }

    private IMeetingController Pick()
    {
        // Se já há uma gravação local em andamento, continua nela até encerrar.
        if (_local?.Service.Current is not null) return _local;

        var viaClient = PipeMeetingController.IsAvailable();
        if (_lastMode != viaClient)
        {
            _log.LogInformation(viaClient
                ? "Cliente de bandeja encontrado: a gravação será controlada por ele."
                : "Cliente de bandeja não encontrado: gravando neste processo.");
            _lastMode = viaClient;
        }
        if (viaClient) return _pipe;
        return _local ??= _localFactory();
    }

    public Task<string> StartAsync(string? title, CancellationToken ct) => Pick().StartAsync(title, ct);
    public Task<string> StopAsync(CancellationToken ct) => Pick().StopAsync(ct);
    public Task<string> StatusAsync(CancellationToken ct) => Pick().StatusAsync(ct);
    public Task<string> TranscriptAsync(int sinceSeq, int maxEntries, CancellationToken ct) => Pick().TranscriptAsync(sinceSeq, maxEntries, ct);
    public Task<string> AddMarkerAsync(string kind, string text, CancellationToken ct) => Pick().AddMarkerAsync(kind, text, ct);
    public Task<string> SaveMinutesAsync(string markdown, CancellationToken ct) => Pick().SaveMinutesAsync(markdown, ct);
    public Task<string> ListMeetingsAsync(CancellationToken ct) => Pick().ListMeetingsAsync(ct);
    public Task<string> LoadMeetingAsync(string meetingId, CancellationToken ct) => Pick().LoadMeetingAsync(meetingId, ct);
    public Task<string> ListDevicesAsync(CancellationToken ct) => Pick().ListDevicesAsync(ct);

    public void Dispose() => _local?.Dispose();
}
