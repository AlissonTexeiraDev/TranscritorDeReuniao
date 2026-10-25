namespace MeetingNotes.Audio;

/// <summary>
/// Detector de atividade de voz por energia (RMS) com piso de ruído adaptativo.
/// Recebe amostras 16 kHz mono e emite segmentos de fala fechados por silêncio ou por tamanho máximo.
/// Não é thread-safe: deve ser alimentado por uma única thread.
/// </summary>
public sealed class Segmenter
{
    private const int Rate = AudioCapture.TargetSampleRate;
    private const int FrameMs = 20;
    private const int Frame = Rate * FrameMs / 1000; // 320 amostras

    private readonly AudioChannel _channel;
    private readonly Action<AudioSegment> _emit;
    private readonly float _threshold;

    private readonly int _preRollFrames = 300 / FrameMs;     // 300 ms antes da fala
    private readonly int _hangoverFrames = 900 / FrameMs;    // 900 ms de silêncio fecha o segmento (pausas curtas não fragmentam a frase)
    private readonly int _maxFrames = 20_000 / FrameMs;      // 20 s no máximo por segmento
    private readonly int _minSpeechFrames = 300 / FrameMs;   // menos de 300 ms de fala é descartado

    private readonly Queue<float[]> _preRoll = new();
    private readonly List<float> _active = new();
    private readonly float[] _frameBuf = new float[Frame];
    private int _frameFill;

    private bool _inSpeech;
    private int _silenceRun;
    private int _speechFrames;
    private DateTime _segmentStart;
    private float _noiseFloor = 0.002f;

    public Segmenter(AudioChannel channel, float threshold, Action<AudioSegment> emit)
    {
        _channel = channel;
        _threshold = threshold;
        _emit = emit;
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            _frameBuf[_frameFill++] = s;
            if (_frameFill == Frame)
            {
                _frameFill = 0;
                ProcessFrame((float[])_frameBuf.Clone());
            }
        }
    }

    private void ProcessFrame(float[] frame)
    {
        double sum = 0;
        foreach (var s in frame) sum += s * s;
        var rms = (float)Math.Sqrt(sum / frame.Length);

        // Piso de ruído: desce rápido, sobe devagar.
        _noiseFloor = rms < _noiseFloor
            ? _noiseFloor * 0.9f + rms * 0.1f
            : _noiseFloor * 0.999f + rms * 0.001f;

        var speech = rms > Math.Max(_threshold, _noiseFloor * 4f);
        var now = DateTime.Now;

        if (!_inSpeech)
        {
            if (speech)
            {
                _inSpeech = true;
                _active.Clear();
                foreach (var p in _preRoll) _active.AddRange(p);
                _active.AddRange(frame);
                _segmentStart = now - TimeSpan.FromMilliseconds(FrameMs * (_preRoll.Count + 1));
                _preRoll.Clear();
                _speechFrames = 1;
                _silenceRun = 0;
            }
            else
            {
                _preRoll.Enqueue(frame);
                if (_preRoll.Count > _preRollFrames) _preRoll.Dequeue();
            }
            return;
        }

        _active.AddRange(frame);
        if (speech) { _speechFrames++; _silenceRun = 0; }
        else _silenceRun++;

        var frames = _active.Count / Frame;
        if (_silenceRun >= _hangoverFrames)
        {
            Flush();
            _inSpeech = false;
        }
        else if (frames >= _maxFrames)
        {
            // Fala longa: corta e continua em um novo segmento sem perder áudio.
            Flush();
            _segmentStart = now;
            _speechFrames = 1;
            _silenceRun = 0;
        }
    }

    private void Flush()
    {
        if (_speechFrames >= _minSpeechFrames && _active.Count > 0)
            _emit(new AudioSegment(_channel, _segmentStart, _active.ToArray()));
        _active.Clear();
    }

    /// <summary>Fecha o segmento em andamento (chamado ao parar a captura).</summary>
    public void Finish()
    {
        if (_inSpeech) Flush();
        _inSpeech = false;
        _preRoll.Clear();
    }
}
