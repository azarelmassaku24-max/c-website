using CSharpQuest.Server.Curriculum;
using CSharpQuest.Server.Grading;

namespace CSharpQuest.Server.Tools;

/// <summary>
/// Kwaliteitscontrole van de leerinhoud: dotnet run -- verify [filter]
/// - elke modeloplossing moet alle tests en vereisten halen;
/// - de startcode mag de tests NIET halen (anders zou foute code goedgekeurd worden);
/// - elke voorspelvraag van type 'output' moet uitvoerbaar zijn.
/// </summary>
public static class CurriculumVerifier
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var repo = services.GetRequiredService<CurriculumRepository>();
        var grading = services.GetRequiredService<GradingService>();
        var snapshot = repo.Current;
        string? filter = args.SkipWhile(a => a != "verify").Skip(1).FirstOrDefault();
        int failures = 0;

        foreach (var p in snapshot.Problems)
        {
            Console.WriteLine($"PROBLEEM  {p}");
            failures++;
        }

        var exercises = snapshot.Exercises.Values
            .Where(e => filter == null || e.Id.StartsWith(filter, StringComparison.OrdinalIgnoreCase) || e.LessonId.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();

        var semaphore = new SemaphoreSlim(3);
        var lines = new System.Collections.Concurrent.ConcurrentBag<(string Id, string Line, bool Ok)>();
        await Task.WhenAll(exercises.Select(async ex =>
        {
            await semaphore.WaitAsync();
            try
            {
                var sol = await grading.GradeAsync(ex, ex.Solution, "nl-BE", CancellationToken.None);
                bool solOk = sol.Passed;
                string detail = solOk ? "" : Describe(sol);
                bool starterOk = true;
                if (!ex.StarterMayPass)
                {
                    var st = await grading.GradeAsync(ex, ex.Starter, "nl-BE", CancellationToken.None);
                    starterOk = !st.Passed;
                    if (!starterOk) detail += " | startcode slaagt al voor alle tests";
                }
                bool ok = solOk && starterOk;
                lines.Add((ex.Id, $"{(ok ? "OK  " : "FAIL")}  {ex.Id,-28} {ex.Title} ({sol.Tests.Count} tests, {sol.Rules.Count} regels, {sol.DurationMs} ms){(ok ? "" : " -> " + detail)}", ok));
            }
            finally { semaphore.Release(); }
        }));
        foreach (var l in lines.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            Console.WriteLine(l.Line);
            if (!l.Ok) failures++;
        }

        foreach (var lesson in snapshot.Lessons.Values.Where(l => filter == null || l.Id.StartsWith(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.Id, StringComparer.Ordinal))
            foreach (var p in lesson.Predictions.Where(p => p.Type == "output"))
            {
                var output = await grading.PredictionOutput(p, "nl-BE", CancellationToken.None) ?? "";
                bool ok = !output.StartsWith("Compileerfout");
                if (!ok) { failures++; Console.WriteLine($"FAIL  voorspelling {lesson.Id}/{p.Id}: {output}"); }
            }

        Console.WriteLine();
        Console.WriteLine($"{exercises.Count} oefeningen en {snapshot.Lessons.Count} lessen gecontroleerd: {(failures == 0 ? "alles in orde." : failures + " probleem/problemen.")}");
        return failures == 0 ? 0 : 1;
    }

    static string Describe(GradeResult r)
    {
        if (!r.Compiled) return "oplossing compileert niet: " + string.Join("; ", r.Diagnostics.Where(d => d.Severity == "error").Select(d => $"{d.File}:{d.Line} {d.Code} {d.Message}"));
        var parts = new List<string>();
        foreach (var t in r.Tests.Where(t => !t.Passed))
            parts.Add($"test '{t.Name}': {t.Message} [{t.Status}]" + (t.Actual != null ? $" actual=«{Trim(t.Actual)}»" : "") + (t.Expected != null ? $" expected=«{Trim(t.Expected)}»" : ""));
        foreach (var rule in r.Rules.Where(x => !x.Passed)) parts.Add($"regel {rule.Type}: {rule.Message}");
        return string.Join(" | ", parts);
    }

    static string Trim(string s)
    {
        s = s.Replace("\n", "⏎");
        return s.Length > 300 ? s[..300] + "…" : s;
    }
}
