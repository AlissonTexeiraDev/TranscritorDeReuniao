using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// NAudio 3.x marca WasapiCapture/WasapiOut como obsoletos em favor de WasapiRecorder/WasapiPlayer.
// As classes antigas continuam funcionando; a migração (e o loopback por processo) fica como evolução.
#pragma warning disable CS0618

namespace MeetingNotes.Audio;

/// <summary>
/// Captura um dispositivo WASAPI (loopback das caixas ou microfone), converte para 16 kHz mono float
/// e alimenta um <see cref="Segmenter"/>. Assume a posse do <see cref="MMDevice"/> recebido.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int TargetSampleRate = 16000;

    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _pipeline;
    private readonly Segmenter _segmenter;
    private readonly ILogger _log;
    private readonly WasapiOut? _keepAlive;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _cts;
    private Task? _pump;

    public AudioChannel Channel { get; }
    public string DeviceName { get; }
    public WaveFormat SourceFormat => _capture.WaveFormat;

    public AudioCapture(AudioChannel channel, MMDevice device, Segmenter segmenter, ILogger log)
    {
        Channel = channel;
        _device = device;
        DeviceName = device.FriendlyName;
        _segmenter = segmenter;
        _log = log;

        if (channel == AudioChannel.System)
        {
            _capture = new WasapiLoopbackCapture(device);
            // O loopback só entrega dados enquanto algo toca. Tocar silêncio mantém o fluxo contínuo.
            _keepAlive = new WasapiOut(device, AudioClientShareMode.Shared, true, 200);
            _keepAlive.Init(new SilenceProvider(_capture.WaveFormat));
        }
        else
        {
            _capture = new WasapiCapture(device);
        }

        var fmt = _capture.WaveFormat;
        _buffer = new BufferedWaveProvider(fmt, TimeSpan.FromSeconds(60))
        {
            DiscardOnBufferOverflow = true,
            ReadFully = false
        };

        ISampleProvider sp = _buffer.ToSampleProvider();
        if (fmt.Channels > 1) sp = new DownmixToMonoSampleProvider(sp);
        if (fmt.SampleRate != TargetSampleRate) sp = new WdlResamplingSampleProvider(sp, TargetSampleRate);
        _pipeline = sp;

        _capture.DataAvailable += (_, e) => _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) _log.LogError(e.Exception, "Captura {Channel} parou com erro", Channel);
            _stopped.TrySetResult();
        };
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _keepAlive?.Play();
        _capture.StartRecording();
        _pump = Task.Run(() => PumpAsync(_cts.Token));
        _log.LogInformation("Captura {Channel} iniciada em '{Device}' ({Rate} Hz, {Ch} canais)",
            Channel, DeviceName, SourceFormat.SampleRate, SourceFormat.Channels);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var buf = new float[TargetSampleRate / 10]; // 100 ms
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var n = _pipeline.Read(buf.AsSpan());
                if (n == 0)
                {
                    await Task.Delay(40, ct);
                    continue;
                }
                _segmenter.Push(buf.AsSpan(0, n));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha no pipeline de áudio {Channel}", Channel);
        }
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _capture.StopRecording();
        _keepAlive?.Stop();
        await Task.WhenAny(_stopped.Task, Task.Delay(2000));

        // Drena o que ainda está no buffer antes de encerrar o segmentador.
        await Task.Delay(150);
        _cts.Cancel();
        if (_pump is not null) await _pump;
        var tail = new float[TargetSampleRate / 10];
        int n;
        while ((n = _pipeline.Read(tail.AsSpan())) > 0) _segmenter.Push(tail.AsSpan(0, n));
        _segmenter.Finish();
        _log.LogInformation("Captura {Channel} encerrada", Channel);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _keepAlive?.Dispose();
        _capture.Dispose();
        _device.Dispose();
    }
}
