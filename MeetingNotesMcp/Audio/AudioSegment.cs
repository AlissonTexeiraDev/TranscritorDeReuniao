namespace MeetingNotesMcp.Audio;

/// <summary>Origem do áudio: o que sai das caixas (outros participantes) ou o microfone (você).</summary>
public enum AudioChannel
{
    System,
    Mic
}

/// <summary>Um trecho contínuo de fala, já em 16 kHz mono float, pronto para o Whisper.</summary>
public sealed record AudioSegment(AudioChannel Channel, DateTime StartedAt, float[] Samples)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)AudioCapture.TargetSampleRate);
}
