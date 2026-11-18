using System.Text.Json;

namespace CSharpQuest.Server.Curriculum;

/// <summary>
/// Laadt de leerinhoud uit de map 'curriculum'. Wijzigingen in de bestanden worden automatisch
/// opgepikt (handig bij het toevoegen van nieuwe hoofdstukken).
/// </summary>
public sealed class CurriculumRepository
{
    readonly string _root;
    readonly ILogger<CurriculumRepository> _log;
    readonly object _gate = new();
    CurriculumSnapshot? _snapshot;
    DateTime _stamp;
    DateTime _lastCheck;

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public CurriculumRepository(IConfiguration config, IWebHostEnvironment env, ILogger<CurriculumRepository> log)
    {
        _log = log;
        var configured = config.GetValue<string>("Paths:Curriculum") ?? "../../curriculum";
        _root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured));
    }

    public string Root => _root;

    public CurriculumSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                if (_snapshot == null || (DateTime.UtcNow - _lastCheck).TotalSeconds > 2)
                {
                    _lastCheck = DateTime.UtcNow;
                    var stamp = LatestWrite();
                    if (_snapshot == null || stamp != _stamp)
                    {
                        _snapshot = Load();
                        _stamp = stamp;
                    }
                }
                return _snapshot;
            }
        }
    }

    DateTime LatestWrite()
    {
        if (!Directory.Exists(_root)) return DateTime.MinValue;
        var latest = DateTime.MinValue;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var t = File.GetLastWriteTimeUtc(f);
            if (t > latest) latest = t;
        }
        return latest;
    }

    CurriculumSnapshot Load()
    {
        var problems = new List<string>();
        var manifestPath = Path.Combine(_root, "curriculum.json");
        if (!File.Exists(manifestPath))
        {
            _log.LogError("curriculum.json niet gevonden in {Root}", _root);
            return new CurriculumSnapshot { Problems = { $"curriculum.json niet gevonden in {_root}" } };
        }
        CurriculumManifest manifest;
        try { manifest = JsonSerializer.Deserialize<CurriculumManifest>(File.ReadAllText(manifestPath), Json) ?? new(); }
        catch (Exception ex)
        {
            return new CurriculumSnapshot { Problems = { "curriculum.json: " + ex.Message } };
        }

        var levels = new List<Level>();
        var lessons = new Dictionary<string, Lesson>();
        var exercises = new Dictionary<string, Exercise>();
        foreach (var meta in manifest.Levels)
        {
            var level = new Level { Meta = meta };
            var folder = Path.Combine(_root, meta.Folder);
            if (Directory.Exists(folder))
            {
                int order = 0;
                foreach (var file in Directory.GetFiles(folder, "*.quest").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
                {
                    try
                    {
                        var lesson = QuestFileParser.Parse(file, File.ReadAllText(file), meta.Id, order++, meta.Number, problems);
                        if (lessons.ContainsKey(lesson.Id)) { problems.Add($"dubbele les-id {lesson.Id}"); continue; }
                        lessons[lesson.Id] = lesson;
                        level.Lessons.Add(lesson);
                        foreach (var ex in lesson.Exercises)
                        {
                            if (!exercises.TryAdd(ex.Id, ex)) problems.Add($"dubbele oefening-id {ex.Id} in {Path.GetFileName(file)}");
                        }
                    }
                    catch (Exception ex) { problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
                }
            }
            levels.Add(level);
        }

        var knowledge = new List<KnowledgeEntry>();
        var kbDir = Path.Combine(_root, "knowledge");
        if (Directory.Exists(kbDir))
        {
            foreach (var file in Directory.GetFiles(kbDir, "*.md").OrderBy(f => f, StringComparer.Ordinal))
                knowledge.AddRange(ParseKnowledge(file, problems));
        }

        foreach (var p in problems) _log.LogWarning("Curriculum: {Problem}", p);
        _log.LogInformation("Curriculum geladen: {Levels} levels, {Lessons} lessen, {Exercises} oefeningen, {Kb} kennisbank-items",
            levels.Count, lessons.Count, exercises.Count, knowledge.Count);
        return new CurriculumSnapshot { Manifest = manifest, Levels = levels, Lessons = lessons, Exercises = exercises, Knowledge = knowledge, Problems = problems };
    }

    /// <summary>
    /// Kennisbankbestand: meerdere items, elk beginnend met "## Titel" en een regel
    /// "<!-- categorie: X | tags: a, b | level: L01 -->".
    /// </summary>
    static IEnumerable<KnowledgeEntry> ParseKnowledge(string file, List<string> problems)
    {
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        var defaultCategory = Path.GetFileNameWithoutExtension(file);
        int dash = defaultCategory.IndexOf('-');
        if (dash >= 0) defaultCategory = defaultCategory[(dash + 1)..];
        var parts = text.Split("\n## ");
        int n = 0;
        foreach (var part in parts.Skip(text.StartsWith("## ") ? 0 : 1))
        {
            var chunk = part.StartsWith("## ") ? part[3..] : part;
            int nl = chunk.IndexOf('\n');
            if (nl < 0) continue;
            var title = chunk[..nl].Trim();
            var body = chunk[(nl + 1)..];
            var entry = new KnowledgeEntry { Id = Path.GetFileNameWithoutExtension(file) + "-" + n++, Title = title, Category = defaultCategory };
            var metaStart = body.IndexOf("<!--", StringComparison.Ordinal);
            if (metaStart >= 0 && metaStart < 5)
            {
                var metaEnd = body.IndexOf("-->", metaStart, StringComparison.Ordinal);
                var meta = body[(metaStart + 4)..metaEnd];
                foreach (var kv in meta.Split('|'))
                {
                    var idx = kv.IndexOf(':');
                    if (idx < 0) continue;
                    var key = kv[..idx].Trim().ToLowerInvariant();
                    var val = kv[(idx + 1)..].Trim();
                    if (key == "categorie") entry.Category = val;
                    else if (key == "tags") entry.Tags = val.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    else if (key == "level") entry.Level = val;
                }
                body = body[(metaEnd + 3)..];
            }
            entry.Body = body.Trim();
            yield return entry;
        }
    }
}
