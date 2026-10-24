using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.Logger;

namespace MeetingNotesMcp.Transcription;

public sealed record TranscribedSegment(TimeSpan Start, TimeSpan End, string Text, float NoSpeechProbability);

/// <summary>Transcrição local com whisper.cpp via Whisper.net. Uma instância serializa as chamadas (o processador não é thread-safe).</summary>
public sealed class WhisperTranscriber : IDisposable
{
    private const int SampleRate = 16000;
    private const int MinSamples = SampleRate * 12 / 10; // whisper.cpp exige pelo menos ~1 s

    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _log;

    public string ModelPath { get; }
    public string Language { get; }

    private static IDisposable? _nativeLogHook;

    public WhisperTranscriber(string modelPath, string language, int threads, ILogger log)
    {
        ModelPath = modelPath;
        Language = language;
        _log = log;

        // Logs do carregador e do whisper.cpp vão para o ILogger (stderr), nunca para o stdout.
        _nativeLogHook ??= LogProvider.AddLogger((level, message) =>
        {
            var lvl = level switch
            {
                WhisperLogLevel.Error => LogLevel.Error,
                WhisperLogLevel.Warning => LogLevel.Warning,
                WhisperLogLevel.Info => LogLevel.Debug,
                _ => LogLevel.Trace
            };
            log.Log(lvl, "whisper.cpp: {Message}", message?.TrimEnd());
        });

        _factory = WhisperFactory.FromPath(modelPath);
        var builder = _factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(threads)
            .WithNoContext();

        if (language.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            builder = builder.WithPrompt("Transcrição de uma reunião de trabalho em português do Brasil, com pontuação.");

        _processor = builder.Build();
        _log.LogInformation("Whisper carregado: {Model} (idioma={Lang}, threads={Threads})", Path.GetFileName(modelPath), language, threads);
    }

    public static GgmlType ParseModel(string name) => Normalize(name) switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large" or "large-v3" => GgmlType.LargeV3,
        "large-turbo" or "large-v3-turbo" => GgmlType.LargeV3Turbo,
        _ => throw new ArgumentException($"Modelo desconhecido: '{name}'. Use tiny, base, small, medium, large ou large-turbo.")
    };

    public static string ModelFileName(string modelName) => $"ggml-{Normalize(modelName)}.bin";

    private static string Normalize(string n) => n.Trim().ToLowerInvariant();

    /// <summary>Garante que o modelo ggml está em disco, baixando do repositório oficial na primeira vez.</summary>
    public static async Task<string> EnsureModelAsync(string modelsDir, string modelName, ILogger log, CancellationToken ct)
    {
        var type = ParseModel(modelName);
        Directory.CreateDirectory(modelsDir);
        var path = Path.Combine(modelsDir, ModelFileName(modelName));
        if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) return path;

        log.LogInformation("Baixando modelo Whisper '{Model}' para {Path} (primeira execução; pode levar alguns minutos)...", modelName, path);
        var tmp = path + ".part";
        await using (var src = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, cancellationToken: ct))
        await using (var dst = File.Create(tmp))
        {
            await src.CopyToAsync(dst, ct);
        }
        File.Move(tmp, path, overwrite: true);
        log.LogInformation("Modelo baixado: {Size} MB", new FileInfo(path).Length / 1_000_000);
        return path;
    }

    public async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        if (samples.Length < MinSamples)
        {
            var padded = new float[MinSamples];
            Array.Copy(samples, padded, samples.Length);
            samples = padded;
        }

        var result = new List<TranscribedSegment>();
        await _gate.WaitAsync(ct);
        try
        {
            await foreach (var seg in _processor.ProcessAsync(samples, ct))
            {
                var text = seg.Text.Trim();
                if (IsHallucination(text, seg.NoSpeechProbability)) continue;
                result.Add(new TranscribedSegment(seg.Start, seg.End, text, seg.NoSpeechProbability));
            }
        }
        finally
        {
            _gate.Release();
        }
        return result;
    }

    // Frases que o Whisper costuma "inventar" em silêncio ou ruído, em português.
    private static readonly string[] KnownJunk =
    [
        "legendas pela comunidade", "amara.org", "obrigado por assistir", "inscreva-se no canal", "tchau, tchau"
    ];

    private static bool IsHallucination(string text, float noSpeechProb)
    {
        if (text.Length == 0) return true;
        if (noSpeechProb > 0.85f) return true;
        if ((text.StartsWith('[') && text.EndsWith(']')) ||
            (text.StartsWith('(') && text.EndsWith(')')) ||
            (text.StartsWith('*') && text.EndsWith('*')))
            return true;
        var lower = text.ToLowerInvariant();
        return KnownJunk.Any(lower.Contains);
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
        _gate.Dispose();
    }
}
