using NAudio.Wave;

namespace MeetingNotes.Audio;

/// <summary>Converte qualquer quantidade de canais para mono fazendo a média.</summary>
internal sealed class DownmixToMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _tmp = [];

    public WaveFormat WaveFormat { get; }

    public DownmixToMonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public int Read(Span<float> buffer)
    {
        var need = buffer.Length * _channels;
        if (_tmp.Length < need) _tmp = new float[need];
        var got = _source.Read(_tmp.AsSpan(0, need));
        var frames = got / _channels;
        for (var i = 0; i < frames; i++)
        {
            float acc = 0;
            for (var c = 0; c < _channels; c++) acc += _tmp[i * _channels + c];
            buffer[i] = acc / _channels;
        }
        return frames;
    }
}
