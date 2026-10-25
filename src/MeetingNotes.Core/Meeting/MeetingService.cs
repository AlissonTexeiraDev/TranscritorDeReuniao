using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using MeetingNotes.Audio;
using MeetingNotes.Transcription;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace MeetingNotes.Meeting;

/// <summary>
/// Orquestra uma reunião: abre os dispositivos, roda as capturas, enfileira segmentos de fala
/// e os transcreve em uma única fila (mantém o uso de CPU previsível).
/// </summary>
public sealed class MeetingService : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MeetingService> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private MeetingOptions _opt;
    private WhisperTranscriber? _transcriber;
    private MeetingSession? _current;
    private MeetingSession? _last;
    private List<AudioCapture> _captures = new();
    private Channel<AudioSegment>? _queue;
    private Task? _consumer;
    private CancellationTokenSource? _cts;
    private int _pending;
    private int _processed;
    private string _state = "parado";

    public MeetingService(MeetingOptions options, ILoggerFactory loggerFactory)
    {
        _opt = options;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<MeetingService>();
    }

    public MeetingOptions Options => _opt;
    public MeetingSession? Current => _current;

    /// <summary>A reunião em andamento ou, se nenhuma, a última encerrada neste processo.</summary>
    public MeetingSession? CurrentOrLast => _current ?? _last;

    public bool IsRecording => _current is not null && _state == "gravando";
    public string State => _state;
    public int PendingSegments => Volatile.Read(ref _pending);

    /// <summary>Disparado (em thread de fundo) quando uma reunião começa ou termina.</summary>
    public event Action<MeetingSession>? MeetingStarted;
    public event Action<MeetingSession>? MeetingStopped;

    /// <summary>Troca as opções. O modelo é recarregado na próxima gravação se necessário.</summary>
    public void UpdateOptions(MeetingOptions options)
    {
        var reload = _opt.RequiresNewTranscriber(options);
        _opt = options;
        if (reload && _current is null && _transcriber is not null)
        {
            _transcriber.Dispose();
            _transcriber = null;
        }
        else if (reload)
        {
            _transcriberStale = true;
        }
    }

    private bool _transcriberStale;
    private volatile bool _preloading;

    /// <summary>Verdadeiro enquanto o modelo está sendo baixado, carregado ou aquecido em segundo plano.</summary>
    public bool IsPreloading => _preloading;

    /// <summary>
    /// Baixa o modelo, carrega o Whisper e aquece o runtime, para a gravação começar sem atraso.
    /// Não faz nada se já há um transcritor pronto ou uma reunião em andamento.
    /// </summary>
    public async Task PreloadAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_current is not null) return;
            if (_transcriberStale && _transcriber is not null)
            {
                _transcriber.Dispose();
                _transcriber = null;
                _transcriberStale = false;
            }
            if (_transcriber is not null) return;

            _preloading = true;
            var modelPath = await WhisperTranscriber.EnsureModelAsync(_opt.ModelsDir, _opt.ModelName, _log, ct);
            var transcriber = new WhisperTranscriber(modelPath, _opt.Language, _opt.Threads, _opt.Vocabulary, _loggerFactory.CreateLogger<WhisperTranscriber>());
            await transcriber.WarmUpAsync(ct);
            _transcriber = transcriber;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha ao pré-carregar o modelo; ele será carregado na primeira gravação");
        }
        finally
        {
            _preloading = false;
            _lock.Release();
        }
    }

    public async Task<MeetingSession> StartAsync(string? title, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        var captures = new List<AudioCapture>();
        try
        {
            if (_current is not null)
                throw new InvalidOperationException($"Já existe uma reunião em andamento ('{_current.Title}'). Encerre-a antes de iniciar outra.");

            _state = "iniciando";
            if (_transcriberStale && _transcriber is not null)
            {
                _transcriber.Dispose();
                _transcriber = null;
                _transcriberStale = false;
            }

            if (_transcriber is null)
            {
                var modelPath = await WhisperTranscriber.EnsureModelAsync(_opt.ModelsDir, _opt.ModelName, _log, ct);
                _transcriber = new WhisperTranscriber(modelPath, _opt.Language, _opt.Threads, _opt.Vocabulary, _loggerFactory.CreateLogger<WhisperTranscriber>());
                await _transcriber.WarmUpAsync(ct);
            }

            var render = AudioDevices.Resolve(DataFlow.Render, _opt.SystemDevice)
                ?? throw new InvalidOperationException("Nenhum dispositivo de saída de áudio encontrado para capturar os participantes (loopback).");
            captures.Add(new AudioCapture(AudioChannel.System, render,
                new Segmenter(AudioChannel.System, _opt.VadThreshold, Enqueue), _loggerFactory.CreateLogger<AudioCapture>()));

            if (!_opt.MicDisabled)
            {
                var mic = AudioDevices.Resolve(DataFlow.Capture, _opt.MicDevice);
                if (mic is null)
                    _log.LogWarning("Microfone não encontrado (filtro: '{Filter}'). Gravando apenas os participantes.", _opt.MicDevice ?? "padrão");
                else
                    captures.Add(new AudioCapture(AudioChannel.Mic, mic,
                        new Segmenter(AudioChannel.Mic, _opt.VadThreshold, Enqueue), _loggerFactory.CreateLogger<AudioCapture>()));
            }

            var session = new MeetingSession(_opt.OutputRoot, title);
            _queue = Channel.CreateUnbounded<AudioSegment>(new UnboundedChannelOptions { SingleReader = true });
            _cts = new CancellationTokenSource();
            _pending = 0;
            _processed = 0;

            foreach (var c in captures) c.Start();
            _captures = captures;
            _current = session;
            _consumer = Task.Run(() => ConsumeAsync(session, _queue.Reader, _cts.Token));
            _state = "gravando";

            session.Add("sistema",
                $"Gravação iniciada. Fontes: {string.Join("; ", captures.Select(c => $"{Label(c.Channel)} = {c.DeviceName}"))}. Modelo Whisper: {_opt.ModelName} ({_opt.Language}).",
                EntryKind.Nota);
            _log.LogInformation("Reunião '{Title}' iniciada em {Folder}", session.Title, session.Folder);
            MeetingStarted?.Invoke(session);
            return session;
        }
        catch
        {
            _state = "parado";
            foreach (var c in captures) c.Dispose();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Enqueue(AudioSegment segment)
    {
        var queue = _queue;
        if (queue is null) return;
        Interlocked.Increment(ref _pending);
        if (!queue.Writer.TryWrite(segment)) Interlocked.Decrement(ref _pending);
    }

    private async Task ConsumeAsync(MeetingSession session, ChannelReader<AudioSegment> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var seg in reader.ReadAllAsync(ct))
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    var parts = await _transcriber!.TranscribeAsync(seg.Samples, ct);
                    var text = string.Join(" ", parts.Select(p => p.Text)).Trim();
                    if (text.Length > 0)
                    {
                        session.Add(Label(seg.Channel), text, EntryKind.Fala, seg.StartedAt);
                        _log.LogInformation("[{Channel}] {Dur:F1}s em {Ms} ms: {Text}", seg.Channel, seg.Duration.TotalSeconds, sw.ElapsedMilliseconds, text);
                    }
                    else
                    {
                        _log.LogDebug("[{Channel}] {Dur:F1}s sem texto útil", seg.Channel, seg.Duration.TotalSeconds);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Falha ao transcrever segmento de {Channel}", seg.Channel);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                    Interlocked.Increment(ref _processed);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // encerramento forçado
        }
    }

    public async Task<MeetingSession?> StopAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_current is null) return null;
            var session = _current;
            _state = "encerrando";

            foreach (var c in _captures)
            {
                try { await c.StopAsync(); }
                catch (Exception ex) { _log.LogError(ex, "Falha ao parar captura {Channel}", c.Channel); }
            }

            _queue?.Writer.TryComplete();
            if (_consumer is not null)
            {
                var pending = PendingSegments;
                if (pending > 0) _log.LogInformation("Transcrevendo {Pending} segmento(s) restante(s)...", pending);
                await Task.WhenAny(_consumer, Task.Delay(TimeSpan.FromMinutes(3), ct));
                if (!_consumer.IsCompleted)
                {
                    _log.LogWarning("Tempo esgotado aguardando a fila; cancelando o restante.");
                    _cts?.Cancel();
                }
            }

            foreach (var c in _captures) c.Dispose();
            _captures.Clear();
            _queue = null;
            _consumer = null;
            _cts?.Dispose();
            _cts = null;

            session.Add("sistema", "Gravação encerrada.", EntryKind.Nota);
            session.End();
            session.Dispose();
            _last = session;
            _current = null;
            _state = "parado";
            _log.LogInformation("Reunião '{Title}' encerrada: {Count} entradas em {Folder}", session.Title, session.Count, session.Folder);
            MeetingStopped?.Invoke(session);
            return session;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string DescribeStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"estado={_state}");
        var s = _current;
        if (s is not null)
        {
            sb.AppendLine($"reuniao={s.Id}");
            sb.AppendLine($"titulo={s.Title}");
            sb.AppendLine($"inicio={s.StartedAt:HH:mm:ss}");
            sb.AppendLine($"duracao={(DateTime.Now - s.StartedAt):hh\\:mm\\:ss}");
            sb.AppendLine($"entradas={s.Count} ultimo_seq={s.LastSeq}");
            sb.AppendLine($"segmentos_na_fila={PendingSegments} segmentos_transcritos={Volatile.Read(ref _processed)}");
            sb.AppendLine($"fontes={string.Join("; ", _captures.Select(c => $"{Label(c.Channel)} = {c.DeviceName}"))}");
            sb.AppendLine($"pasta={s.Folder}");
        }
        else if (_last is not null)
        {
            sb.AppendLine($"ultima_reuniao={_last.Id} entradas={_last.Count} pasta={_last.Folder}");
        }
        sb.AppendLine($"modelo={_opt.ModelName} idioma={_opt.Language} threads={_opt.Threads}");
        sb.AppendLine($"saida={_opt.OutputRoot}");
        return sb.ToString().Replace("\r\n", "\n").TrimEnd();
    }

    private string Label(AudioChannel channel) => channel == AudioChannel.Mic ? _opt.MyName : _opt.OthersName;

    public void Dispose()
    {
        if (_current is not null)
        {
            try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { _log.LogError(ex, "Falha ao encerrar a reunião no desligamento"); }
        }
        _transcriber?.Dispose();
        _lock.Dispose();
    }
}
