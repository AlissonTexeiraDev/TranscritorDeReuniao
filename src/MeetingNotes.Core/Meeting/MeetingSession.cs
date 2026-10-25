using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingNotes.Meeting;

public enum EntryKind
{
    Fala,
    Decisao,
    Acao,
    Topico,
    Nota
}

public sealed record TranscriptEntry(int Seq, DateTime Timestamp, string Speaker, string Text, EntryKind Kind = EntryKind.Fala)
{
    public string Render()
    {
        var ts = Timestamp.ToString("HH:mm:ss");
        return Kind == EntryKind.Fala
            ? $"[{Seq}] {ts} {Speaker}: {Text}"
            : $"[{Seq}] {ts} {KindLabel(Kind)}: {Text}";
    }

    public static string KindLabel(EntryKind kind) => kind switch
    {
        EntryKind.Decisao => "DECISÃO",
        EntryKind.Acao => "AÇÃO",
        EntryKind.Topico => "TÓPICO",
        EntryKind.Nota => "NOTA",
        _ => "FALA"
    };
}

/// <summary>Uma reunião: transcrição em memória + persistência incremental em JSONL na pasta da sessão.</summary>
public sealed class MeetingSession : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly List<TranscriptEntry> _entries = new();
    private readonly object _lock = new();
    private readonly StreamWriter? _jsonl;
    private int _seq;

    public string Id { get; }
    public string Title { get; }
    public DateTime StartedAt { get; }
    public DateTime? EndedAt { get; private set; }
    public string Folder { get; }

    public string TranscriptJsonlPath => Path.Combine(Folder, "transcript.jsonl");
    public string TranscriptMarkdownPath => Path.Combine(Folder, "transcript.md");
    public string MinutesPath => Path.Combine(Folder, "ata.md");
    public string MetadataPath => Path.Combine(Folder, "meeting.json");

    public MeetingSession(string outputRoot, string? title)
    {
        StartedAt = DateTime.Now;
        Title = string.IsNullOrWhiteSpace(title) ? "Reunião" : title.Trim();
        Id = StartedAt.ToString("yyyy-MM-dd_HH-mm") + "_" + Slug(Title);
        Folder = Path.Combine(outputRoot, Id);
        Directory.CreateDirectory(Folder);
        _jsonl = new StreamWriter(TranscriptJsonlPath, append: true, Encoding.UTF8) { AutoFlush = true };
        SaveMetadata();
    }

    /// <summary>Reabre uma reunião já gravada (somente leitura).</summary>
    private MeetingSession(string folder, string id, string title, DateTime startedAt, DateTime? endedAt, List<TranscriptEntry> entries)
    {
        Folder = folder;
        Id = id;
        Title = title;
        StartedAt = startedAt;
        EndedAt = endedAt;
        _entries = entries;
        _seq = entries.Count > 0 ? entries[^1].Seq : 0;
    }

    public bool IsClosed => EndedAt is not null;

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public int LastSeq
    {
        get { lock (_lock) return _seq; }
    }

    public TranscriptEntry Add(string speaker, string text, EntryKind kind = EntryKind.Fala, DateTime? timestamp = null)
    {
        lock (_lock)
        {
            if (IsClosed || _jsonl is null) throw new InvalidOperationException("A reunião já foi encerrada.");
            var entry = new TranscriptEntry(++_seq, timestamp ?? DateTime.Now, speaker, text, kind);
            _entries.Add(entry);
            _jsonl.WriteLine(JsonSerializer.Serialize(entry, JsonOpts));
            return entry;
        }
    }

    public IReadOnlyList<TranscriptEntry> GetSince(int afterSeq, int max)
    {
        lock (_lock) return _entries.Where(e => e.Seq > afterSeq).Take(max).ToList();
    }

    public IReadOnlyList<TranscriptEntry> All()
    {
        lock (_lock) return _entries.ToList();
    }

    public string RenderMarkdown()
    {
        var entries = All();
        var sb = new StringBuilder();
        sb.AppendLine($"# {Title}");
        sb.AppendLine();
        sb.AppendLine($"- Início: {StartedAt:dd/MM/yyyy HH:mm}");
        if (EndedAt is { } end) sb.AppendLine($"- Fim: {end:dd/MM/yyyy HH:mm} (duração {(end - StartedAt):hh\\:mm\\:ss})");
        sb.AppendLine();

        var markers = entries.Where(e => e.Kind is EntryKind.Decisao or EntryKind.Acao or EntryKind.Topico).ToList();
        if (markers.Count > 0)
        {
            sb.AppendLine("## Marcadores");
            sb.AppendLine();
            foreach (var m in markers)
                sb.AppendLine($"- **{TranscriptEntry.KindLabel(m.Kind)}** ({m.Timestamp:HH:mm}): {m.Text}");
            sb.AppendLine();
        }

        sb.AppendLine("## Transcrição");
        sb.AppendLine();
        foreach (var e in entries.OrderBy(e => e.Timestamp).ThenBy(e => e.Seq))
        {
            sb.AppendLine(e.Kind == EntryKind.Fala
                ? $"**{e.Timestamp:HH:mm:ss} {e.Speaker}:** {e.Text}"
                : $"> {TranscriptEntry.KindLabel(e.Kind)} ({e.Timestamp:HH:mm:ss}): {e.Text}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public void SaveTranscriptMarkdown() => File.WriteAllText(TranscriptMarkdownPath, RenderMarkdown(), Encoding.UTF8);

    public string SaveMinutes(string markdown)
    {
        File.WriteAllText(MinutesPath, markdown, Encoding.UTF8);
        return MinutesPath;
    }

    public void End()
    {
        lock (_lock)
        {
            if (EndedAt is not null) return;
            EndedAt = DateTime.Now;
        }
        SaveMetadata();
        SaveTranscriptMarkdown();
    }

    private void SaveMetadata()
    {
        var meta = new MeetingMetadata(Id, Title, StartedAt, EndedAt, Folder);
        File.WriteAllText(MetadataPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static MeetingSession Load(string folder)
    {
        var metaPath = Path.Combine(folder, "meeting.json");
        var meta = File.Exists(metaPath)
            ? JsonSerializer.Deserialize<MeetingMetadata>(File.ReadAllText(metaPath))
            : null;
        var entries = new List<TranscriptEntry>();
        var jsonl = Path.Combine(folder, "transcript.jsonl");
        if (File.Exists(jsonl))
        {
            foreach (var line in File.ReadLines(jsonl))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var e = JsonSerializer.Deserialize<TranscriptEntry>(line, JsonOpts);
                if (e is not null) entries.Add(e);
            }
        }
        var id = meta?.Id ?? Path.GetFileName(folder);
        return new MeetingSession(folder, id, meta?.Title ?? id, meta?.StartedAt ?? Directory.GetCreationTime(folder), meta?.EndedAt ?? DateTime.MinValue, entries);
    }

    public static IReadOnlyList<MeetingMetadata> ListAll(string outputRoot)
    {
        if (!Directory.Exists(outputRoot)) return [];
        var list = new List<MeetingMetadata>();
        foreach (var dir in Directory.GetDirectories(outputRoot).OrderByDescending(d => d))
        {
            var metaPath = Path.Combine(dir, "meeting.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var meta = JsonSerializer.Deserialize<MeetingMetadata>(File.ReadAllText(metaPath));
                if (meta is not null) list.Add(meta with { Folder = dir });
            }
            catch
            {
                // pasta corrompida: ignora
            }
        }
        return list;
    }

    private static string Slug(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }
        var slug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : (slug.Length == 0 ? "reuniao" : slug);
    }

    public void Dispose() => _jsonl?.Dispose();
}

public sealed record MeetingMetadata(string Id, string Title, DateTime StartedAt, DateTime? EndedAt, string Folder);
