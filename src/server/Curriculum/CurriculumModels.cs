using CSharpQuest.Server.Execution;

namespace CSharpQuest.Server.Curriculum;

public sealed class CurriculumManifest
{
    public string Title { get; set; } = "C# Quest";
    public List<LevelManifest> Levels { get; set; } = new();
    public List<SkillNode> Skills { get; set; } = new();
    public List<Rank> Ranks { get; set; } = new();
}

public sealed class LevelManifest
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Source { get; set; } = "";          // bv. "Deel 03, 04 en 05"
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "level";       // level | project | verdieping
    public string Folder { get; set; } = "";
    public string Color { get; set; } = "#5b8cff";
    public string Icon { get; set; } = "code";
    public List<string> Requires { get; set; } = new();
    public string? Note { get; set; }                  // bv. "Basisuitleg aanvullend geschreven"
}

public sealed class SkillNode
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Level { get; set; } = "";
    public List<string> Requires { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public string? Description { get; set; }
}

public sealed class Rank
{
    public int MinLevel { get; set; }
    public string Title { get; set; } = "";
}

public sealed class Level
{
    public LevelManifest Meta { get; init; } = new();
    public List<Lesson> Lessons { get; } = new();
}

public sealed class Lesson
{
    public string Id { get; set; } = "";
    public string LevelId { get; set; } = "";
    public int Order { get; set; }
    public string Kind { get; set; } = "lesson";       // lesson | boss | project | challenges | verdieping
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Skills { get; set; } = new();
    public string Source { get; set; } = "";
    public string? Supplement { get; set; }            // vermelding van aanvullende (zelf geschreven) uitleg
    public int Minutes { get; set; } = 20;
    public string? Story { get; set; }                 // boss fights / projecten
    public List<string> RequiresLevels { get; set; } = new(); // extra vereiste levels (bv. PA3 vereist L09)
    public string Understand { get; set; } = "";
    public string Example { get; set; } = "";
    public List<Prediction> Predictions { get; } = new();
    public List<Exercise> Exercises { get; } = new();
    public string FilePath { get; set; } = "";
}

public sealed class Prediction
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "choice";      // choice | output | text
    public string Question { get; set; } = "";
    public List<string> Options { get; } = new();
    public HashSet<int> Correct { get; } = new();
    public List<string> Answers { get; } = new();
    public string? Code { get; set; }                  // voor type=output: wordt echt uitgevoerd
    public string? Stdin { get; set; }
    public string Explanation { get; set; } = "";
}

public sealed class Exercise
{
    public string Id { get; set; } = "";
    public string LessonId { get; set; } = "";
    public string LevelId { get; set; } = "";
    public string Role { get; set; } = "oefenen";     // oefenen | meesterschap | boss | uitdaging | project
    public string Title { get; set; } = "";
    public int Difficulty { get; set; } = 1;          // 1..5
    public int Xp { get; set; } = 50;
    public List<string> Skills { get; set; } = new();
    public string Source { get; set; } = "";
    public string Description { get; set; } = "";
    public List<SourceFile> Starter { get; } = new();
    public List<SourceFile> Solution { get; } = new();
    public string SolutionExplanation { get; set; } = "";
    public List<string> Hints { get; } = new();
    public List<ExerciseRule> Rules { get; } = new();
    public List<TestCase> Tests { get; } = new();
    public Dictionary<string, string> Files { get; } = new();
    public string? MainType { get; set; }
    public bool Interactive { get; set; }              // bv. games: ook vrij spelen in de terminal
    public bool StarterMayPass { get; set; }
    public string? Phase { get; set; }                 // boss fase-titel
    public int LevelNumber { get; set; }
}

public sealed class ExerciseRule
{
    public string Type { get; set; } = "";
    public string Target { get; set; } = "";
    public string Message { get; set; } = "";
    public int? Count { get; set; }
    public string? Returns { get; set; }
    public int? Params { get; set; }
}

public sealed class TestCase
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "io";          // io | code
    public string Input { get; set; } = "";
    public string? Expected { get; set; }
    public string Mode { get; set; } = "lenient";     // exact | lenient | contains | regex | numbers | screen
    public bool CompareWithReference { get; set; }
    public int Seed { get; set; } = 1;
    public bool Hidden { get; set; }
    public string? Code { get; set; }
    public Dictionary<string, string> ExpectedFiles { get; } = new();
    public List<string> RemovedFiles { get; } = new();
    public Dictionary<string, string> Files { get; } = new();
}

public sealed class KnowledgeEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string Level { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed class CurriculumSnapshot
{
    public CurriculumManifest Manifest { get; init; } = new();
    public List<Level> Levels { get; init; } = new();
    public Dictionary<string, Lesson> Lessons { get; init; } = new();
    public Dictionary<string, Exercise> Exercises { get; init; } = new();
    public List<KnowledgeEntry> Knowledge { get; init; } = new();
    public List<string> Problems { get; init; } = new();
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
}
