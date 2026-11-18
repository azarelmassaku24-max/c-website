using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CSharpQuest.Server.Curriculum;
using CSharpQuest.Server.Execution;
using CSharpQuest.Server.Feedback;

namespace CSharpQuest.Server.Grading;

public sealed class FileCheckResult
{
    public string Name { get; init; } = "";
    public bool Passed { get; init; }
    public string? Expected { get; init; }
    public string? Actual { get; init; }
}

public sealed class TestResult
{
    public string Name { get; init; } = "";
    public bool Hidden { get; init; }
    public bool Passed { get; set; }
    public string Kind { get; init; } = "io";
    public string? Input { get; init; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string Mode { get; init; } = "lenient";
    public string Category { get; set; } = FeedbackCategory.Logic;
    public string Message { get; set; } = "";
    public string Status { get; set; } = "ok";
    public List<DiffLine> Diff { get; set; } = new();
    public RuntimeErrorInfo? Exception { get; set; }
    public List<CheckResult> Checks { get; set; } = new();
    public List<FileCheckResult> FileChecks { get; set; } = new();
    public long DurationMs { get; set; }
}

public sealed class GradeResult
{
    public string ExerciseId { get; init; } = "";
    public bool Passed { get; set; }
    public bool Compiled { get; set; }
    public double Score { get; set; }
    public List<CodeDiagnostic> Diagnostics { get; set; } = new();
    public List<TestResult> Tests { get; set; } = new();
    public List<RuleResult> Rules { get; set; } = new();
    public List<CodeDiagnostic> Quality { get; set; } = new();
    public Dictionary<string, int> Categories { get; set; } = new();
    public string PrimaryCategory { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> ErrorCodes { get; set; } = new();
    public long DurationMs { get; set; }
}

/// <summary>Beoordeelt de code van de leerling tegen de tests en vereisten van een oefening.</summary>
public sealed class GradingService
{
    readonly ExecutionService _exec;
    readonly ILogger<GradingService> _log;
    readonly ConcurrentDictionary<string, string> _referenceCache = new();
    readonly ConcurrentDictionary<string, string> _predictionCache = new();

    public GradingService(ExecutionService exec, ILogger<GradingService> log)
    {
        _exec = exec;
        _log = log;
    }

    static SandboxLimits TestLimits => new() { WallTime = TimeSpan.FromSeconds(6), CpuTime = TimeSpan.FromSeconds(4), MaxOutputChars = 100_000 };

    public async Task<GradeResult> GradeAsync(Exercise exercise, IReadOnlyList<SourceFile> files, string culture, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var result = new GradeResult { ExerciseId = exercise.Id };
        var options = new CompileRequestOptions { MainType = exercise.MainType };
        var compiled = _exec.Compiler.Compile(files, options);
        result.Diagnostics = compiled.Diagnostics;
        result.Compiled = compiled.Success;
        result.ErrorCodes = compiled.Diagnostics.Where(d => d.Severity == "error").Select(d => d.Code).Distinct().ToList();

        var trees = _exec.Compiler.ParseUserFiles(files);
        if (!compiled.Success)
        {
            Finish(result, started);
            return result;
        }

        result.Rules = RuleChecker.Check(exercise.Rules, trees);
        var compilation = _exec.Compiler.CreateCompilation(trees, options);
        result.Quality = CodeQualityAnalyzer.Analyze(trees, compilation, exercise.LevelNumber);
        var userPaths = files.Select(f => CSharpCompilerService.NormalizePath(f.Path)).ToList();

        using var program = _exec.Prepare(compiled.Assembly!);
        var tasks = exercise.Tests.Select(async test =>
        {
            try
            {
                return test.Kind == "code"
                    ? await RunCodeTest(exercise, test, files, userPaths, culture, ct)
                    : await RunIoTest(exercise, test, program, userPaths, culture, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Test {Test} van {Exercise} faalde intern", test.Name, exercise.Id);
                return new TestResult { Name = test.Name, Passed = false, Category = FeedbackCategory.Logic, Message = "Interne fout bij het uitvoeren van deze test.", Status = "internal" };
            }
        }).ToList();
        result.Tests = (await Task.WhenAll(tasks)).ToList();

        foreach (var t in result.Tests.Where(t => t.Exception != null))
            result.ErrorCodes.Add(t.Exception!.Type);
        foreach (var t in result.Tests.Where(t => !t.Passed && t.Exception == null))
            result.ErrorCodes.Add("test:" + t.Category);
        foreach (var r in result.Rules.Where(r => !r.Passed))
            result.ErrorCodes.Add("rule:" + r.Type);

        Finish(result, started);
        return result;
    }

    void Finish(GradeResult result, DateTime started)
    {
        var categories = new Dictionary<string, int>();
        void Count(string c) => categories[c] = categories.GetValueOrDefault(c) + 1;
        foreach (var d in result.Diagnostics) Count(d.Category);
        foreach (var t in result.Tests.Where(t => !t.Passed)) Count(t.Category);
        foreach (var r in result.Rules.Where(r => !r.Passed)) Count(FeedbackCategory.Requirement);
        foreach (var q in result.Quality) Count(q.Category);
        result.Categories = categories;

        int total = result.Tests.Count + result.Rules.Count;
        int passed = result.Tests.Count(t => t.Passed) + result.Rules.Count(r => r.Passed);
        result.Score = !result.Compiled ? 0 : total == 0 ? 1 : (double)passed / total;
        result.Passed = result.Compiled && result.Tests.All(t => t.Passed) && result.Rules.All(r => r.Passed);

        string[] order = { FeedbackCategory.Syntax, FeedbackCategory.Compile, FeedbackCategory.Security, FeedbackCategory.Runtime, FeedbackCategory.Logic, FeedbackCategory.Output, FeedbackCategory.Requirement };
        result.PrimaryCategory = result.Passed ? "" : order.FirstOrDefault(c => categories.ContainsKey(c) && (c != FeedbackCategory.Compile || result.Diagnostics.Any(d => d.Severity == "error" && d.Category == c))) ?? "";
        result.Summary = result.PrimaryCategory switch
        {
            "" when result.Passed && result.Quality.Count == 0 => "Alle tests geslaagd en je code ziet er netjes uit. Knap werk!",
            "" when result.Passed => $"Alle tests geslaagd! Er zijn nog {result.Quality.Count} verbeterpunt(en) voor nettere code.",
            FeedbackCategory.Syntax => "Je code bevat een syntaxfout: de compiler kan ze niet lezen. Los eerst de rode markeringen op.",
            FeedbackCategory.Compile => "Je code compileert niet: ze breekt een regel van C#. Bekijk de compileerfouten.",
            FeedbackCategory.Security => "Je code gebruikt iets wat niet toegelaten is in de sandbox.",
            FeedbackCategory.Runtime => "Je programma crasht tijdens de uitvoering (exception).",
            FeedbackCategory.Logic => "Je programma werkt, maar geeft (in sommige gevallen) een verkeerd resultaat.",
            FeedbackCategory.Output => "Je resultaat klopt bijna: de output wijkt af in de tekst of opmaak.",
            FeedbackCategory.Requirement => "De output klopt, maar je oplossing volgt nog niet alle vereisten van de opdracht.",
            _ => "Nog niet alle tests zijn geslaagd.",
        };
        result.DurationMs = (long)(DateTime.UtcNow - started).TotalMilliseconds;
    }

    async Task<TestResult> RunIoTest(Exercise exercise, TestCase test, PreparedProgram program, List<string> userPaths, string culture, CancellationToken ct)
    {
        var files = new Dictionary<string, string>(exercise.Files);
        foreach (var kv in test.Files) files[kv.Key] = kv.Value;
        foreach (var naam in test.RemovedFiles) files.Remove(naam);
        var spec = new RunSpec { Stdin = test.Input, Seed = test.Seed, TestMode = true, Files = files, Limits = TestLimits, Culture = culture };
        var outcome = await _exec.RunAsync(program, spec, userPaths, ct);

        string? expected = test.Expected;
        if (test.CompareWithReference)
            expected = await ReferenceOutput(exercise, test, culture, ct);
        string? expectedDisplay = expected == null ? null
            : test.Mode == "screen" && test.CompareWithReference ? VirtualTerminal.Render(expected)
            : VirtualTerminal.StripAnsi(expected, markClears: true);

        var result = new TestResult
        {
            Name = test.Name,
            Hidden = test.Hidden,
            Kind = "io",
            Input = test.Input,
            Expected = expectedDisplay,
            Actual = VirtualTerminal.StripAnsi(outcome.Output, markClears: true),
            Mode = test.Mode,
            Status = outcome.Status,
            DurationMs = outcome.DurationMs,
        };
        if (test.Mode == "screen") result.Actual = VirtualTerminal.Render(outcome.Output);

        if (outcome.Status != "ok")
        {
            result.Passed = false;
            if (outcome.Exception != null)
            {
                result.Category = FeedbackCategory.Runtime;
                result.Exception = outcome.Exception;
                result.Message = outcome.Exception.Title + (outcome.Exception.Line != null ? $" (regel {outcome.Exception.Line})" : "");
            }
            else
            {
                result.Category = FeedbackCategory.Logic;
                var (title, explanation, tip) = RuntimeErrorTranslator.ForStatus(outcome.Status);
                result.Message = outcome.Status == "exitCode" ? $"Je programma stopte met exitcode {outcome.ExitCode}." : $"{title}. {explanation} {tip}".Trim();
            }
            if (expected != null)
                result.Diff = OutputComparer.Compare(expected, outcome.Output, test.Mode).Diff;
            return result;
        }

        bool outputOk = true;
        if (expected != null)
        {
            var cmp = OutputComparer.Compare(expected, outcome.Output, test.Mode);
            outputOk = cmp.Passed;
            result.Category = cmp.Category;
            result.Message = cmp.Message;
            result.Diff = cmp.Diff;
        }

        foreach (var (name, content) in test.ExpectedFiles)
        {
            outcome.Files.TryGetValue(name, out var actual);
            bool ok = actual != null && OutputComparer.Normalize(actual) == OutputComparer.Normalize(content);
            result.FileChecks.Add(new FileCheckResult { Name = name, Passed = ok, Expected = content, Actual = actual });
            if (!ok)
            {
                outputOk = false;
                result.Category = FeedbackCategory.Logic;
                result.Message = actual == null ? $"Het bestand '{name}' werd niet aangemaakt." : $"De inhoud van het bestand '{name}' klopt niet.";
            }
        }
        result.Passed = outputOk;
        if (result.Passed && string.IsNullOrEmpty(result.Message)) result.Message = "Geslaagd.";
        return result;
    }

    async Task<TestResult> RunCodeTest(Exercise exercise, TestCase test, IReadOnlyList<SourceFile> files, List<string> userPaths, string culture, CancellationToken ct)
    {
        var harness = "internal static class __QuestTests {\n static void Main() {\n" + test.Code + "\n }\n}\n";
        var compiled = _exec.Compiler.Compile(files, new CompileRequestOptions { TestHarness = harness });
        var result = new TestResult { Name = test.Name, Hidden = test.Hidden, Kind = "code", Mode = "code" };
        if (!compiled.Success)
        {
            result.Passed = false;
            result.Category = FeedbackCategory.Requirement;
            var err = compiled.Diagnostics.FirstOrDefault(d => d.Severity == "error");
            result.Message = err?.File == "Tests.cs"
                ? "De test kan je method niet oproepen. Controleer de naam, de parameters en het return type van je method (zoals gevraagd in de opdracht)."
                  + (err != null ? $" Detail: {err.Message}" : "")
                : err?.Message ?? "Compileerfout";
            result.Status = "compile";
            return result;
        }
        using var program = _exec.Prepare(compiled.Assembly!);
        var spec = new RunSpec { Stdin = test.Input, Seed = test.Seed, TestMode = true, Files = new Dictionary<string, string>(exercise.Files), Limits = TestLimits, Culture = culture };
        var outcome = await _exec.RunAsync(program, spec, userPaths, ct);
        result.Checks = outcome.Checks;
        result.Status = outcome.Status;
        result.DurationMs = outcome.DurationMs;
        if (outcome.Status != "ok")
        {
            result.Passed = false;
            if (outcome.Exception != null)
            {
                result.Category = FeedbackCategory.Runtime;
                result.Exception = outcome.Exception;
                result.Message = outcome.Exception.Title + (outcome.Exception.Line != null ? $" (regel {outcome.Exception.Line})" : "");
            }
            else
            {
                var (title, explanation, _) = RuntimeErrorTranslator.ForStatus(outcome.Status);
                result.Category = FeedbackCategory.Logic;
                result.Message = $"{title}. {explanation}";
            }
            return result;
        }
        result.Passed = outcome.Checks.Count > 0 && outcome.Checks.All(c => c.Passed);
        var failed = outcome.Checks.FirstOrDefault(c => !c.Passed);
        result.Category = FeedbackCategory.Logic;
        result.Message = result.Passed ? $"Alle {outcome.Checks.Count} controles geslaagd."
            : failed != null ? $"{failed.Label}: verwacht {Short(failed.Expected)}, maar kreeg {Short(failed.Actual)}." : "Geen controles uitgevoerd.";
        return result;
    }

    static string Short(string? s) => s == null ? "null" : s.Length > 120 ? s[..120] + "…" : s.Replace("\n", "⏎");

    async Task<string?> ReferenceOutput(Exercise exercise, TestCase test, string culture, CancellationToken ct)
    {
        string key = Hash(exercise.Id + "|" + string.Join("|", exercise.Solution.Select(s => s.Content)) + "|" + test.Input + "|" + test.Seed + "|" + culture
            + "|" + test.Mode + "|" + string.Join("|", test.Files.Select(kv => kv.Key + "=" + kv.Value)) + "|" + string.Join("|", test.RemovedFiles)
            + "|" + string.Join("|", exercise.Files.Select(kv => kv.Key + "=" + kv.Value)));
        if (_referenceCache.TryGetValue(key, out var cached)) return cached;
        var compiled = _exec.Compiler.Compile(exercise.Solution, new CompileRequestOptions { MainType = exercise.MainType });
        if (!compiled.Success)
        {
            _log.LogError("Modeloplossing van {Exercise} compileert niet: {Errors}", exercise.Id, string.Join("; ", compiled.Diagnostics.Select(d => d.Code + " " + d.Message)));
            return null;
        }
        using var program = _exec.Prepare(compiled.Assembly!);
        var files = new Dictionary<string, string>(exercise.Files);
        foreach (var kv in test.Files) files[kv.Key] = kv.Value;
        foreach (var naam in test.RemovedFiles) files.Remove(naam);
        var outcome = await _exec.RunAsync(program, new RunSpec { Stdin = test.Input, Seed = test.Seed, TestMode = true, Files = files, Limits = TestLimits, Culture = culture },
            exercise.Solution.Select(s => s.Path).ToList(), ct);
        // Ruwe output (met ANSI-codes) bewaren: de vergelijker normaliseert beide kanten op dezelfde manier.
        var output = test.Mode == "screen" ? VirtualTerminal.Render(outcome.Output) : outcome.Output;
        _referenceCache[key] = output;
        return output;
    }

    // -------------------------------------------------------------- predictions
    public static string WrapSnippet(string code)
    {
        if (code.Contains("static void Main") || code.Contains("class ")) return code;
        return "using System;\nusing System.Collections.Generic;\nclass Program {\n static void Main() {\n" + code + "\n }\n}\n";
    }

    public async Task<string?> PredictionOutput(Prediction p, string culture, CancellationToken ct)
    {
        string code = WrapSnippet(p.Code ?? "");
        string key = Hash(code + "|" + p.Stdin + "|" + culture);
        if (_predictionCache.TryGetValue(key, out var cached)) return cached;
        var compiled = _exec.Compiler.Compile(new[] { new SourceFile("Program.cs", code) }, new CompileRequestOptions());
        if (!compiled.Success)
        {
            var output = "Compileerfout: " + string.Join("; ", compiled.Diagnostics.Where(d => d.Severity == "error").Select(d => d.Message));
            _predictionCache[key] = output;
            return output;
        }
        using var program = _exec.Prepare(compiled.Assembly!);
        var outcome = await _exec.RunAsync(program, new RunSpec { Stdin = p.Stdin ?? "", Seed = 1, Culture = culture, Limits = TestLimits }, new[] { "Program.cs" }, ct);
        var result = VirtualTerminal.StripAnsi(outcome.Output);
        if (outcome.Exception != null) result += (result.Length > 0 ? "\n" : "") + "→ " + outcome.Exception.Type.Split('.').Last();
        _predictionCache[key] = result;
        return result;
    }

    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..32];
}
