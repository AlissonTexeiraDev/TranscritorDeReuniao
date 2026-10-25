using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace MeetingNotes.Transcription;

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

    /// <summary>Runtime nativo escolhido pelo Whisper.net (Cuda, Vulkan, Cpu...).</summary>
    public string Runtime { get; }

    private static IDisposable? _nativeLogHook;

    public WhisperTranscriber(string modelPath, string language, int threads, string vocabulary, ILogger log)
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

        // O prompt inicial orienta idioma, pontuação e vocabulário (nomes próprios e termos técnicos).
        var prompt = language.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? "Transcrição de uma reunião de trabalho em português do Brasil, com pontuação."
            : "Transcript of a work meeting, with punctuation.";
        var vocab = vocabulary.Trim();
        if (vocab.Length > 0) prompt += $" {vocab}.";
        builder = builder.WithPrompt(prompt);

        _processor = builder.Build();
        Runtime = RuntimeOptions.LoadedLibrary?.ToString() ?? "desconhecido";
        _log.LogInformation("Whisper carregado: {Model} (idioma={Lang}, threads={Threads}, runtime={Runtime})", Path.GetFileName(modelPath), language, threads, Runtime);
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

    /// <summary>
    /// Roda uma inferência curta para aquecer o runtime. Na GPU (Vulkan/CUDA) a primeira chamada compila
    /// os shaders e pode levar mais de 10 s; feito aqui, a primeira fala da reunião não paga esse custo.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var silence = new float[SampleRate * 3 / 2];
        await _gate.WaitAsync(ct);
        try
        {
            await foreach (var _ in _processor.ProcessAsync(silence, ct)) { }
        }
        finally
        {
            _gate.Release();
        }
        _log.LogInformation("Whisper aquecido em {Ms} ms (runtime={Runtime})", sw.ElapsedMilliseconds, Runtime);
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
