using System.Text;
using CSharpQuest.Server.Execution;

namespace CSharpQuest.Server.Curriculum;

/// <summary>
/// Leest een .quest-bestand. Formaat: secties die beginnen met een regel "#! naam attr=waarde attr="met spaties"".
/// Zie curriculum/README.md voor de volledige beschrijving.
/// </summary>
public static class QuestFileParser
{
    sealed record Section(string Name, Dictionary<string, string> Attrs, string Body, int Line);

    public static Lesson Parse(string path, string text, string levelId, int order, int levelNumber, List<string> problems)
    {
        var sections = Split(text.Replace("\r\n", "\n"));
        var lesson = new Lesson { LevelId = levelId, Order = order, FilePath = path };
        Exercise? exercise = null;
        TestCase? test = null;
        string? pendingSolutionFile = null;

        void Problem(Section s, string msg) => problems.Add($"{Path.GetFileName(path)}:{s.Line}: {msg}");

        foreach (var s in sections)
        {
            switch (s.Name)
            {
                case "lesson":
                case "les":
                case "boss":
                case "project":
                case "uitdagingen":
                case "verdieping":
                    lesson.Kind = s.Name switch { "les" => "lesson", "uitdagingen" => "challenges", _ => s.Name };
                    foreach (var line in s.Body.Split('\n'))
                    {
                        int idx = line.IndexOf(':');
                        if (idx <= 0) continue;
                        string key = line[..idx].Trim().ToLowerInvariant();
                        string value = line[(idx + 1)..].Trim();
                        switch (key)
                        {
                            case "id": lesson.Id = value; break;
                            case "titel": case "title": lesson.Title = value; break;
                            case "samenvatting": lesson.Summary = value; break;
                            case "skills": lesson.Skills = List(value); break;
                            case "bron": lesson.Source = value; break;
                            case "aanvulling": lesson.Supplement = value; break;
                            case "duur": lesson.Minutes = int.TryParse(value, out var m) ? m : 20; break;
                            case "verhaal": lesson.Story = value; break;
                            case "vereist": lesson.RequiresLevels = List(value); break;
                        }
                    }
                    if (string.IsNullOrEmpty(lesson.Id)) Problem(s, "les zonder id");
                    break;
                case "begrijpen":
                    lesson.Understand = s.Body;
                    break;
                case "voorbeeld":
                    lesson.Example = s.Body;
                    break;
                case "voorspel":
                    lesson.Predictions.Add(ParsePrediction(s, lesson.Predictions.Count, Problem));
                    break;
                case "oefening":
                    exercise = new Exercise
                    {
                        Id = Attr(s, "id") ?? $"{lesson.Id}-{lesson.Exercises.Count + 1}",
                        LessonId = lesson.Id,
                        LevelId = levelId,
                        LevelNumber = levelNumber,
                        Role = Attr(s, "rol") ?? "oefenen",
                        Title = Attr(s, "titel") ?? "Oefening",
                        Difficulty = int.TryParse(Attr(s, "moeilijkheid"), out var d) ? d : 1,
                        Xp = int.TryParse(Attr(s, "xp"), out var xp) ? xp : 50,
                        Skills = List(Attr(s, "skills") ?? string.Join(",", lesson.Skills)),
                        Source = Attr(s, "bron") ?? "",
                        MainType = Attr(s, "main"),
                        Interactive = Attr(s, "interactief") == "ja",
                        StarterMayPass = Attr(s, "starter-mag-slagen") == "ja",
                        Phase = Attr(s, "fase"),
                        Description = s.Body,
                    };
                    lesson.Exercises.Add(exercise);
                    test = null;
                    break;
                case "starter":
                    if (exercise == null) { Problem(s, "starter zonder oefening"); break; }
                    exercise.Starter.Add(new SourceFile(Attr(s, "bestand") ?? "Program.cs", s.Body + "\n"));
                    break;
                case "oplossing":
                    if (exercise == null) { Problem(s, "oplossing zonder oefening"); break; }
                    pendingSolutionFile = Attr(s, "bestand") ?? "Program.cs";
                    exercise.Solution.Add(new SourceFile(pendingSolutionFile, s.Body + "\n"));
                    break;
                case "uitleg":
                    if (exercise == null) { Problem(s, "uitleg zonder oefening"); break; }
                    exercise.SolutionExplanation = s.Body;
                    break;
                case "hint":
                    if (exercise == null) { Problem(s, "hint zonder oefening"); break; }
                    exercise.Hints.Add(s.Body);
                    break;
                case "regel":
                    if (exercise == null) { Problem(s, "regel zonder oefening"); break; }
                    exercise.Rules.Add(new ExerciseRule
                    {
                        Type = Attr(s, "type") ?? "",
                        Target = Attr(s, "doel") ?? "",
                        Message = Attr(s, "bericht") ?? s.Body,
                        Count = int.TryParse(Attr(s, "aantal"), out var c) ? c : null,
                        Returns = Attr(s, "geeft"),
                        Params = int.TryParse(Attr(s, "parameters"), out var p) ? p : null,
                    });
                    break;
                case "bestand":
                    if (exercise == null) { Problem(s, "bestand zonder oefening"); break; }
                    exercise.Files[Attr(s, "naam") ?? "data.txt"] = FileBody(s.Body);
                    break;
                case "test":
                    if (exercise == null) { Problem(s, "test zonder oefening"); break; }
                    test = new TestCase
                    {
                        Name = Attr(s, "naam") ?? $"Test {exercise.Tests.Count + 1}",
                        Kind = Attr(s, "soort") ?? "io",
                        Mode = Attr(s, "mode") ?? "lenient",
                        Seed = int.TryParse(Attr(s, "seed"), out var seed) ? seed : 1,
                        Hidden = Attr(s, "verborgen") == "ja",
                        CompareWithReference = Attr(s, "referentie") == "ja",
                    };
                    if (test.Kind == "code") test.Code = s.Body;
                    exercise.Tests.Add(test);
                    break;
                case "invoer":
                    if (test == null) { Problem(s, "invoer zonder test"); break; }
                    // Een regel "<leeg>" staat voor een lege invoerregel (lege regels achteraan worden anders weggeknipt).
                    test.Input = s.Body.Length > 0
                        ? string.Join("\n", s.Body.Split('\n').Select(l => l.Trim() == "<leeg>" ? "" : l)) + "\n"
                        : "";
                    break;
                case "verwacht":
                    if (test == null) { Problem(s, "verwacht zonder test"); break; }
                    test.Expected = s.Body;
                    break;
                case "test-bestand":
                    if (test == null) { Problem(s, "test-bestand zonder test"); break; }
                    test.Files[Attr(s, "naam") ?? "data.txt"] = FileBody(s.Body);
                    break;
                case "zonder-bestand":
                    if (test == null) { Problem(s, "zonder-bestand zonder test"); break; }
                    test.RemovedFiles.Add(Attr(s, "naam") ?? "");
                    break;
                case "verwacht-bestand":
                    if (test == null) { Problem(s, "verwacht-bestand zonder test"); break; }
                    test.ExpectedFiles[Attr(s, "naam") ?? "data.txt"] = s.Body;
                    break;
                default:
                    Problem(s, $"onbekende sectie '#! {s.Name}'");
                    break;
            }
        }

        foreach (var ex in lesson.Exercises)
        {
            if (ex.Starter.Count == 0) ex.Starter.Add(new SourceFile("Program.cs", DefaultStarter));
            if (ex.Solution.Count == 0) problems.Add($"{Path.GetFileName(path)}: oefening {ex.Id} heeft geen oplossing");
            if (ex.Tests.Count == 0 && ex.Rules.Count == 0) problems.Add($"{Path.GetFileName(path)}: oefening {ex.Id} heeft geen tests");
            if (ex.Hints.Count < 3) problems.Add($"{Path.GetFileName(path)}: oefening {ex.Id} heeft {ex.Hints.Count} hints (3 verwacht)");
            foreach (var t in ex.Tests)
                if (t.Kind == "io" && t.Expected == null && !t.CompareWithReference && t.ExpectedFiles.Count == 0)
                    problems.Add($"{Path.GetFileName(path)}: test '{t.Name}' van {ex.Id} heeft geen verwachte output");
        }
        return lesson;
    }

    public const string DefaultStarter = """
        using System;

        namespace CSharpQuest {
            class Program {
                static void Main(string[] args) {

                }
            }
        }

        """;

    static Prediction ParsePrediction(Section s, int index, Action<Section, string> problem)
    {
        var p = new Prediction
        {
            Id = Attr(s, "id") ?? $"v{index + 1}",
            Type = Attr(s, "type") ?? "choice",
            Stdin = Attr(s, "invoer")?.Replace("\\n", "\n"),
        };
        var answer = Attr(s, "antwoord");
        if (answer != null) p.Answers.AddRange(answer.Split('|').Select(a => a.Trim()));

        var question = new StringBuilder();
        var explanation = new StringBuilder();
        bool inFence = false, inExplanation = false;
        foreach (var raw in s.Body.Split('\n'))
        {
            var line = raw;
            if (inExplanation) { explanation.AppendLine(line); continue; }
            if (line.TrimStart().StartsWith("```")) inFence = !inFence;
            if (!inFence)
            {
                if (line.StartsWith("? ")) { inExplanation = true; explanation.AppendLine(line[2..]); continue; }
                if (p.Type == "choice" && (line.StartsWith("- ") || line.StartsWith("+ ")))
                {
                    if (line.StartsWith("+ ")) p.Correct.Add(p.Options.Count);
                    p.Options.Add(line[2..].Trim());
                    continue;
                }
            }
            question.AppendLine(line);
        }
        p.Question = question.ToString().Trim();
        p.Explanation = explanation.ToString().Trim();

        // De eerste csharp-codeblock is de code waarover de vraag gaat.
        int start = p.Question.IndexOf("```csharp", StringComparison.Ordinal);
        if (start >= 0)
        {
            int codeStart = p.Question.IndexOf('\n', start) + 1;
            int end = p.Question.IndexOf("```", codeStart, StringComparison.Ordinal);
            if (codeStart > 0 && end > codeStart) p.Code = p.Question[codeStart..end].TrimEnd();
        }
        if (p.Type == "choice" && p.Correct.Count == 0) problem(s, $"voorspelling {p.Id} heeft geen juist antwoord (+)");
        if (p.Type == "text" && p.Answers.Count == 0) problem(s, $"voorspelling {p.Id} heeft geen antwoord=");
        if (p.Type == "output" && p.Code == null) problem(s, $"voorspelling {p.Id} (output) heeft geen ```csharp code");
        return p;
    }

    static List<Section> Split(string text)
    {
        var result = new List<Section>();
        string? name = null;
        Dictionary<string, string> attrs = new();
        var body = new StringBuilder();
        int line = 0, startLine = 0;

        void Flush()
        {
            if (name == null) return;
            result.Add(new Section(name, attrs, TrimBody(body.ToString()), startLine));
        }

        foreach (var l in text.Split('\n'))
        {
            line++;
            if (l.StartsWith("#! "))
            {
                Flush();
                (name, attrs) = ParseHeader(l[3..]);
                body.Clear();
                startLine = line;
            }
            else if (name != null) body.Append(l).Append('\n');
        }
        Flush();
        return result;
    }

    static string TrimBody(string s)
    {
        // Lege regels aan het begin en einde weg, inspringing behouden.
        var lines = s.Split('\n').ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    static (string, Dictionary<string, string>) ParseHeader(string header)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        string Next()
        {
            while (i < header.Length && header[i] == ' ') i++;
            var sb = new StringBuilder();
            while (i < header.Length && header[i] != ' ')
            {
                if (header[i] == '"')
                {
                    i++;
                    while (i < header.Length && header[i] != '"') sb.Append(header[i++]);
                    i++;
                }
                else sb.Append(header[i++]);
            }
            return sb.ToString();
        }
        string name = Next().Trim();
        while (i < header.Length)
        {
            var token = Next();
            if (token.Length == 0) continue;
            int eq = token.IndexOf('=');
            if (eq > 0) attrs[token[..eq]] = token[(eq + 1)..];
            else attrs[token] = "ja";
        }
        return (name, attrs);
    }

    /// <summary>Bestandsinhoud; een regel "<leeg>" staat voor een lege regel.</summary>
    static string FileBody(string body) =>
        body.Length == 0 ? "" : string.Join("\n", body.Split('\n').Select(l => l.Trim() == "<leeg>" ? "" : l)) + "\n";

    static string? Attr(Section s, string key) => s.Attrs.TryGetValue(key, out var v) ? v : null;

    static List<string> List(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
