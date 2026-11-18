using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpQuest.Server.Curriculum;
using CSharpQuest.Server.Editor;
using CSharpQuest.Server.Execution;
using CSharpQuest.Server.Feedback;
using CSharpQuest.Server.Grading;
using CSharpQuest.Server.Progress;

namespace CSharpQuest.Server.Api;

public sealed record CodeRequest(List<SourceFile>? Files, string? MainType, string? Stdin, Dictionary<string, string>? InitialFiles, string? Culture, int? Seed);
public sealed record PositionRequest(List<SourceFile>? Files, string? File, int Line, int Column);
public sealed record GradeRequest(List<SourceFile>? Files, string? Culture);
public sealed record PredictionAnswer(string? Text, List<int>? Choices, string? Culture);

public static class Endpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ------------------------------------------------------------- code
        api.MapPost("/code/diagnostics", (CodeRequest req, CSharpCompilerService compiler) =>
        {
            var files = Files(req.Files);
            var diagnostics = compiler.Diagnose(files, new CompileRequestOptions { MainType = req.MainType });
            return Results.Ok(new { diagnostics, entryPoints = CSharpCompilerService.FindEntryPoints(compiler.ParseUserFiles(files)) });
        });

        api.MapPost("/code/review", (CodeRequest req, CSharpCompilerService compiler, int? level) =>
        {
            var files = Files(req.Files);
            var trees = compiler.ParseUserFiles(files);
            var compilation = compiler.CreateCompilation(trees, new CompileRequestOptions());
            return Results.Ok(new { quality = CodeQualityAnalyzer.Analyze(trees, compilation, level ?? 20) });
        });

        api.MapPost("/code/run", async (CodeRequest req, ExecutionService exec, HttpContext ctx) =>
        {
            var files = Files(req.Files);
            var compiled = exec.Compiler.Compile(files, new CompileRequestOptions { MainType = req.MainType });
            if (!compiled.Success) return Results.Ok(new { compile = CompileDto(compiled) });
            using var program = exec.Prepare(compiled.Assembly!);
            var run = await exec.RunAsync(program, new RunSpec
            {
                Stdin = req.Stdin ?? "",
                Seed = req.Seed,
                Files = req.InitialFiles ?? new(),
                Culture = req.Culture ?? "nl-BE",
                Limits = new SandboxLimits { WallTime = TimeSpan.FromSeconds(10), CpuTime = TimeSpan.FromSeconds(6) },
            }, files.Select(f => CSharpCompilerService.NormalizePath(f.Path)).ToList(), ctx.RequestAborted);
            return Results.Ok(new { compile = CompileDto(compiled), run = RunDto(run) });
        });

        api.MapPost("/code/trace", async (CodeRequest req, ExecutionService exec, HttpContext ctx) =>
        {
            var files = Files(req.Files);
            var compiled = exec.Compiler.Compile(files, new CompileRequestOptions { MainType = req.MainType, Trace = true });
            if (!compiled.Success) return Results.Ok(new { compile = CompileDto(compiled) });
            using var program = exec.Prepare(compiled.Assembly!);
            var run = await exec.RunAsync(program, new RunSpec
            {
                Stdin = req.Stdin ?? "",
                Seed = req.Seed ?? 1,
                Files = req.InitialFiles ?? new(),
                Trace = true,
                Culture = req.Culture ?? "nl-BE",
                Limits = new SandboxLimits { WallTime = TimeSpan.FromSeconds(10), CpuTime = TimeSpan.FromSeconds(6) },
            }, files.Select(f => CSharpCompilerService.NormalizePath(f.Path)).ToList(), ctx.RequestAborted);
            var events = new JsonArray();
            if (run.TraceJsonl != null)
                foreach (var line in run.TraceJsonl.Split('\n'))
                {
                    if (line.Length == 0) continue;
                    try { events.Add(JsonNode.Parse(line)); } catch (JsonException) { }
                }
            return Results.Ok(new
            {
                compile = CompileDto(compiled),
                run = RunDto(run),
                events,
                variables = compiled.TraceVariables,
                warning = compiled.TraceWarning,
            });
        });

        api.MapPost("/code/complete", async (PositionRequest req, EditorIntelligence editor, HttpContext ctx) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var items = await editor.CompleteAsync(Files(req.Files), req.File ?? "Program.cs", req.Line, req.Column, cts.Token);
            return Results.Ok(new { items });
        });

        api.MapPost("/code/hover", async (PositionRequest req, EditorIntelligence editor, HttpContext ctx) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var markdown = await editor.HoverAsync(Files(req.Files), req.File ?? "Program.cs", req.Line, req.Column, cts.Token);
            return Results.Ok(new { markdown });
        });

        app.Map("/ws/run", async (HttpContext ctx, ExecutionService exec, ILoggerFactory logs) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await InteractiveSession.RunAsync(socket, exec, logs.CreateLogger("Interactive"), ctx.RequestAborted);
        });

        // ------------------------------------------------------- curriculum
        api.MapGet("/curriculum", (CurriculumRepository repo) =>
        {
            var c = repo.Current;
            return Results.Ok(new
            {
                title = c.Manifest.Title,
                skills = c.Manifest.Skills,
                ranks = c.Manifest.Ranks,
                problems = c.Problems,
                levels = c.Levels.Select(l => new
                {
                    id = l.Meta.Id,
                    number = l.Meta.Number,
                    title = l.Meta.Title,
                    subtitle = l.Meta.Subtitle,
                    source = l.Meta.Source,
                    description = l.Meta.Description,
                    kind = l.Meta.Kind,
                    color = l.Meta.Color,
                    icon = l.Meta.Icon,
                    requires = l.Meta.Requires,
                    note = l.Meta.Note,
                    lessons = l.Lessons.Select(LessonSummary),
                }),
            });
        });

        api.MapGet("/lessons/{id}", (string id, CurriculumRepository repo) =>
        {
            if (!repo.Current.Lessons.TryGetValue(id, out var lesson)) return Results.NotFound();
            return Results.Ok(new
            {
                summary = LessonSummary(lesson),
                understand = lesson.Understand,
                example = lesson.Example,
                story = lesson.Story,
                predictions = lesson.Predictions.Select(p => new { p.Id, p.Type, p.Question, p.Options, multiple = p.Correct.Count > 1 }),
            });
        });

        api.MapPost("/lessons/{id}/predictions/{pid}/check", async (string id, string pid, PredictionAnswer answer, CurriculumRepository repo, GradingService grading, HttpContext ctx) =>
        {
            if (!repo.Current.Lessons.TryGetValue(id, out var lesson)) return Results.NotFound();
            var p = lesson.Predictions.FirstOrDefault(x => x.Id == pid);
            if (p == null) return Results.NotFound();
            bool correct;
            string correctAnswer;
            string? actual = null;
            switch (p.Type)
            {
                case "choice":
                    {
                        var chosen = new HashSet<int>(answer.Choices ?? new());
                        correct = chosen.SetEquals(p.Correct);
                        correctAnswer = string.Join(" / ", p.Correct.OrderBy(i => i).Select(i => p.Options[i]));
                        break;
                    }
                case "output":
                    {
                        actual = await grading.PredictionOutput(p, answer.Culture ?? "nl-BE", ctx.RequestAborted) ?? "";
                        var cmp = OutputComparer.Compare(actual, answer.Text ?? "", "lenient");
                        // Ook goed: enkel wat het programma zelf afdrukt (zonder de meegeëchode invoer).
                        var withoutEcho = StripEcho(actual, p.Stdin);
                        correct = cmp.Passed || OutputComparer.Compare(withoutEcho, answer.Text ?? "", "lenient").Passed;
                        correctAnswer = actual;
                        break;
                    }
                default:
                    {
                        string given = Norm(answer.Text);
                        correct = p.Answers.Any(a => Norm(a) == given);
                        correctAnswer = p.Answers.FirstOrDefault() ?? "";
                        break;
                    }
            }
            return Results.Ok(new { correct, correctAnswer, explanation = p.Explanation, actualOutput = actual });
        });

        api.MapGet("/exercises/{id}", (string id, CurriculumRepository repo) =>
        {
            if (!repo.Current.Exercises.TryGetValue(id, out var ex)) return Results.NotFound();
            return Results.Ok(new
            {
                summary = ExerciseSummary(ex),
                description = ex.Description,
                starter = ex.Starter,
                files = ex.Files,
                mainType = ex.MainType,
                interactive = ex.Interactive,
                hintCount = ex.Hints.Count,
                rules = ex.Rules.Select(r => new { r.Type, r.Message }),
                tests = ex.Tests.Select(t => t.Hidden
                    ? (object)new { name = t.Name, hidden = true, kind = t.Kind }
                    : new { name = t.Name, hidden = false, kind = t.Kind, input = t.Input, expected = t.CompareWithReference ? null : t.Expected, mode = t.Mode }),
            });
        });

        api.MapGet("/exercises/{id}/hints/{n:int}", (string id, int n, CurriculumRepository repo) =>
        {
            if (!repo.Current.Exercises.TryGetValue(id, out var ex) || n < 1 || n > ex.Hints.Count) return Results.NotFound();
            return Results.Ok(new { level = n, hint = ex.Hints[n - 1] });
        });

        api.MapGet("/exercises/{id}/solution", (string id, CurriculumRepository repo) =>
        {
            if (!repo.Current.Exercises.TryGetValue(id, out var ex)) return Results.NotFound();
            return Results.Ok(new { files = ex.Solution, explanation = ex.SolutionExplanation });
        });

        api.MapPost("/exercises/{id}/grade", async (string id, GradeRequest req, CurriculumRepository repo, GradingService grading, HttpContext ctx) =>
        {
            if (!repo.Current.Exercises.TryGetValue(id, out var ex)) return Results.NotFound();
            var result = await grading.GradeAsync(ex, Files(req.Files), req.Culture ?? "nl-BE", ctx.RequestAborted);
            return Results.Ok(result);
        });

        api.MapGet("/knowledge", (CurriculumRepository repo) => Results.Ok(new
        {
            entries = repo.Current.Knowledge,
            errors = DiagnosticTranslator.Catalog().Select(e => new { code = e.Code, message = e.Message, explanation = e.Explanation, tip = e.Tip }),
        }));

        // --------------------------------------------------------- progress
        api.MapGet("/progress/{profile}", async (string profile, IProgressStore store, HttpContext ctx) =>
        {
            if (!FileProgressStore.ValidProfile(profile)) return Results.BadRequest();
            var data = await store.LoadAsync(profile, ctx.RequestAborted);
            return data == null ? Results.NotFound() : Results.Text(data.ToJsonString(), "application/json");
        });

        api.MapMethods("/progress/{profile}", new[] { "PUT", "POST" }, async (string profile, HttpContext ctx, IProgressStore store) =>
        {
            if (!FileProgressStore.ValidProfile(profile)) return Results.BadRequest();
            if (ctx.Request.ContentLength > FileProgressStore.MaxBytes) return Results.StatusCode(413);
            JsonNode? data;
            try { data = await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted); }
            catch (JsonException) { return Results.BadRequest(new { error = "Ongeldige JSON" }); }
            if (data is not JsonObject) return Results.BadRequest();
            await store.SaveAsync(profile, data, ctx.RequestAborted);
            return Results.NoContent();
        });

        api.MapDelete("/progress/{profile}", async (string profile, IProgressStore store, HttpContext ctx) =>
        {
            if (!FileProgressStore.ValidProfile(profile)) return Results.BadRequest();
            await store.DeleteAsync(profile, ctx.RequestAborted);
            return Results.NoContent();
        });

        api.MapGet("/system", () => Results.Ok(new
        {
            runtime = Environment.Version.ToString(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            isolation = OperatingSystem.IsWindows()
                ? "Apart proces + Windows Job Object (600 MB geheugen, CPU-limiet, max. 1 proces, geen UI/klembord), opgeschoonde omgevingsvariabelen, padbewaking, API-allowlist."
                : "Apart proces met GC-heaplimiet, tijdslimieten, opgeschoonde omgevingsvariabelen, padbewaking en API-allowlist. Gebruik de Docker-configuratie voor extra isolatie (geen netwerk, read-only).",
            compiler = typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.GetName().Version?.ToString(),
        }));
    }

    static string Norm(string? s) => (s ?? "").Trim().Replace("\r\n", "\n").ToLowerInvariant();

    /// <summary>Verwijdert de door de runtime meegeëchode invoerregels uit de output.</summary>
    static string StripEcho(string output, string? stdin)
    {
        if (string.IsNullOrEmpty(stdin)) return output;
        var inputs = new Queue<string>(stdin.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0));
        var result = new List<string>();
        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
        {
            if (inputs.Count > 0 && line.EndsWith(inputs.Peek(), StringComparison.Ordinal))
            {
                var rest = line[..^inputs.Dequeue().Length];
                if (rest.Trim().Length > 0) result.Add(rest);
                continue;
            }
            result.Add(line);
        }
        return string.Join("\n", result);
    }

    static List<SourceFile> Files(List<SourceFile>? files) =>
        (files ?? new()).Where(f => f.Path != null).Select(f => new SourceFile(f.Path, f.Content ?? "")).ToList();

    static object CompileDto(CompileOutcome c) => new { success = c.Success, diagnostics = c.Diagnostics, entryPoints = c.EntryPoints, compileMs = c.CompileMs };

    static object RunDto(RunOutcome r) => new
    {
        status = r.Status,
        output = r.Output,
        error = r.Error,
        exitCode = r.ExitCode,
        durationMs = r.DurationMs,
        exception = r.Exception,
        message = r.Message,
        files = r.Files,
    };

    static object LessonSummary(Lesson l) => new
    {
        id = l.Id,
        levelId = l.LevelId,
        order = l.Order,
        kind = l.Kind,
        title = l.Title,
        summary = l.Summary,
        skills = l.Skills,
        source = l.Source,
        supplement = l.Supplement,
        minutes = l.Minutes,
        story = l.Story,
        requires = l.RequiresLevels,
        predictionCount = l.Predictions.Count,
        exercises = l.Exercises.Select(ExerciseSummary),
    };

    static object ExerciseSummary(Exercise e) => new
    {
        id = e.Id,
        lessonId = e.LessonId,
        levelId = e.LevelId,
        role = e.Role,
        title = e.Title,
        difficulty = e.Difficulty,
        xp = e.Xp,
        skills = e.Skills,
        source = e.Source,
        phase = e.Phase,
        interactive = e.Interactive,
        testCount = e.Tests.Count,
    };
}
