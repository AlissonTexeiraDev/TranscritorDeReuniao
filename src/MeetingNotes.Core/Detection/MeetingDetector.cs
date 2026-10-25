using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingNotes.Detection;

public sealed record DetectedMeeting(string App, int ProcessId, string Title);

public sealed record DetectorOptions(bool Enabled, IReadOnlyList<string> WatchedProcesses, int StopAfterSeconds);

/// <summary>
/// Detecta reuniões observando quem está usando o microfone: quando um processo monitorado
/// (Teams, navegador com Meet, Zoom) mantém uma sessão de captura ativa, a reunião começou;
/// quando a sessão some por alguns segundos, terminou. Não depende de APIs do Teams ou do Google.
/// </summary>
public sealed class MeetingDetector : IDisposable
{
    private readonly Func<DetectorOptions> _options;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Quantas verificações seguidas com o microfone em uso antes de considerar que há reunião.</summary>
    public int StartAfterPolls { get; init; } = 2;

    public DetectedMeeting? Current { get; private set; }

    public event Action<DetectedMeeting>? MeetingStarted;
    public event Action<DetectedMeeting>? MeetingEnded;

    public MeetingDetector(Func<DetectorOptions> options, ILogger log)
    {
        _options = options;
        _log = log;
    }

    public void Start() => _loop ??= Task.Run(LoopAsync);

    public void Stop() => _cts.Cancel();

    private async Task LoopAsync()
    {
        var hits = 0;
        DateTime? lastSeen = null;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var opt = _options();
                if (!opt.Enabled)
                {
                    hits = 0;
                    Current = null;
                }
                else
                {
                    var detected = Scan(opt.WatchedProcesses);
                    if (detected is not null)
                    {
                        lastSeen = DateTime.UtcNow;
                        if (Current is null)
                        {
                            hits++;
                            if (hits >= StartAfterPolls)
                            {
                                hits = 0;
                                Current = detected;
                                _log.LogInformation("Reunião detectada: {App} (pid {Pid}) '{Title}'", detected.App, detected.ProcessId, detected.Title);
                                MeetingStarted?.Invoke(detected);
                            }
                        }
                    }
                    else
                    {
                        hits = 0;
                        if (Current is not null && lastSeen is { } seen &&
                            (DateTime.UtcNow - seen).TotalSeconds >= Math.Max(5, opt.StopAfterSeconds))
                        {
                            var ended = Current;
                            Current = null;
                            _log.LogInformation("Reunião encerrada: {App} '{Title}'", ended.App, ended.Title);
                            MeetingEnded?.Invoke(ended);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha no detector de reuniões");
            }

            try { await Task.Delay(PollInterval, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Procura, entre os processos que estão capturando microfone, um que pertença a um app monitorado.</summary>
    public DetectedMeeting? Scan(IReadOnlyList<string> watched)
    {
        var pids = ActiveCapturePids();
        if (pids.Count == 0) return null;

        var tree = ProcessTree.Snapshot();
        foreach (var pid in pids)
        {
            if (pid <= 0 || pid == Environment.ProcessId) continue;
            var owner = FindWatchedAncestor(tree, pid, watched);
            if (owner is null) continue;
            var (ownerPid, name) = owner.Value;
            var title = WindowTitles.BestTitle(name, WindowTitles.ForProcess(ownerPid));
            return new DetectedMeeting(name, ownerPid, title);
        }
        return null;
    }

    private static (int Pid, string Name)? FindWatchedAncestor(Dictionary<int, ProcessTree.Entry> tree, int pid, IReadOnlyList<string> watched)
    {
        var cur = pid;
        for (var depth = 0; depth < 6 && tree.TryGetValue(cur, out var p); depth++)
        {
            if (watched.Any(w => string.Equals(w.Trim(), p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                // Sobe enquanto o pai tiver o mesmo nome (processos utilitários do navegador) para chegar à janela principal.
                var top = cur;
                while (tree.TryGetValue(top, out var q) && q.Parent != top &&
                       tree.TryGetValue(q.Parent, out var parent) &&
                       string.Equals(parent.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                {
                    top = q.Parent;
                }
                return (top, p.Name);
            }
            if (p.Parent == cur || p.Parent <= 0) break;
            cur = p.Parent;
        }
        return null;
    }

    private HashSet<int> ActiveCapturePids()
    {
        var result = new HashSet<int>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        var manager = device.AudioSessionManager;
                        manager.RefreshSessions();
                        var sessions = manager.Sessions;
                        if (sessions is null) continue;
                        for (var i = 0; i < sessions.Count; i++)
                        {
                            var session = sessions[i];
                            if (session.State == AudioSessionState.AudioSessionStateActive)
                                result.Add((int)session.GetProcessID);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Falha lendo sessões de '{Device}'", device.FriendlyName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha ao enumerar dispositivos de captura");
        }
        return result;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { /* encerrando */ }
        _cts.Dispose();
    }
}
