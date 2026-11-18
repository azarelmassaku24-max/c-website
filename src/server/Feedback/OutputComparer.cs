using System.Globalization;
using System.Text.RegularExpressions;
using CSharpQuest.Server.Execution;

namespace CSharpQuest.Server.Feedback;

public sealed class DiffLine
{
    public string Kind { get; init; } = "same"; // same | expected | actual
    public string Text { get; init; } = "";
}

public sealed class CompareResult
{
    public bool Passed { get; init; }
    public string Category { get; init; } = FeedbackCategory.Output;
    public string Message { get; init; } = "";
    public List<DiffLine> Diff { get; init; } = new();
    public int? FirstDifferentLine { get; init; }
}

/// <summary>Vergelijkt de echte output van een programma met de verwachte output.</summary>
public static partial class OutputComparer
{
    public static string Normalize(string s)
    {
        // Console.Clear wordt aan beide kanten een regel "[scherm gewist]", zodat verwachte output dat kan vermelden.
        s = VirtualTerminal.StripAnsi(s.Replace("\r\n", "\n").Replace('\r', '\n'), markClears: true);
        var lines = s.Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        return string.Join("\n", lines);
    }

    static string Lenient(string s)
    {
        var lines = Normalize(s).Split('\n')
            .Select(l => Spaces().Replace(l.Trim(), " ").ToLowerInvariant())
            .Select(l => SpaceAroundPunct().Replace(l, "$1"))
            .Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    public static List<string> Numbers(string s) =>
        NumberRegex().Matches(VirtualTerminal.StripAnsi(s)).Select(m => m.Value.Replace('.', ',')).ToList();

    public static CompareResult Compare(string expected, string actual, string mode)
    {
        mode = (mode ?? "lenient").ToLowerInvariant();
        string exp = Normalize(expected);
        string act = mode == "screen" ? VirtualTerminal.Render(actual) : Normalize(actual);
        if (mode == "screen") exp = Normalize(expected);

        switch (mode)
        {
            case "regex":
                {
                    bool ok;
                    try { ok = Regex.IsMatch(act, expected.Trim(), RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
                    catch { ok = false; }
                    return new CompareResult
                    {
                        Passed = ok,
                        Category = FeedbackCategory.Output,
                        Message = ok ? "De output heeft de verwachte vorm." : "De output heeft niet de verwachte vorm.",
                        Diff = ok ? new() : act.Split('\n').Select(l => new DiffLine { Kind = "actual", Text = l }).Take(80).ToList(),
                    };
                }
            case "contains":
                {
                    var needles = exp.Split('\n').Where(l => l.Trim().Length > 0).ToList();
                    int pos = 0;
                    var actLower = Lenient(act);
                    foreach (var n in needles)
                    {
                        var key = Lenient(n);
                        int idx = actLower.IndexOf(key, pos, StringComparison.Ordinal);
                        if (idx < 0)
                            return new CompareResult
                            {
                                Passed = false,
                                Category = Numbers(n).Count > 0 ? FeedbackCategory.Logic : FeedbackCategory.Output,
                                Message = $"In de output ontbreekt: \"{n.Trim()}\"" + (pos > 0 ? " (na de vorige verwachte tekst)" : ""),
                                Diff = Diff(needles, act.Split('\n').ToList(), contains: true),
                            };
                        pos = idx + key.Length;
                    }
                    return new CompareResult { Passed = true, Message = "Alle verwachte tekst staat in de output." };
                }
            case "numbers":
                {
                    var e = Numbers(exp); var a = Numbers(act);
                    bool ok = e.SequenceEqual(a);
                    return new CompareResult
                    {
                        Passed = ok,
                        Category = FeedbackCategory.Logic,
                        Message = ok ? "Alle getallen kloppen." : $"De getallen in je output kloppen niet. Verwacht: {string.Join(" ", e)} — gekregen: {string.Join(" ", a)}",
                        Diff = ok ? new() : Diff(exp.Split('\n').ToList(), act.Split('\n').ToList()),
                    };
                }
        }

        bool exact = exp == act;
        bool lenientOk = Lenient(exp) == Lenient(act);
        bool passed = mode == "exact" ? exact : lenientOk;
        if (passed)
            return new CompareResult
            {
                Passed = true,
                Message = exact ? "De output is exact correct." : "De output is correct (kleine verschillen in spaties of hoofdletters worden genegeerd).",
            };

        var expLines = exp.Split('\n').ToList();
        var actLines = act.Split('\n').ToList();
        var diff = Diff(expLines, actLines);
        int? first = null;
        for (int i = 0; i < Math.Max(expLines.Count, actLines.Count); i++)
        {
            string el = i < expLines.Count ? expLines[i] : "";
            string al = i < actLines.Count ? actLines[i] : "";
            if (!string.Equals(Spaces().Replace(el.Trim(), " "), Spaces().Replace(al.Trim(), " "), StringComparison.OrdinalIgnoreCase)) { first = i + 1; break; }
        }

        string category;
        string message;
        if (mode == "exact" && lenientOk)
        {
            category = FeedbackCategory.Output;
            message = "Bijna! De inhoud klopt, maar er is een verschil in spaties of hoofdletters.";
        }
        else if (Numbers(exp).SequenceEqual(Numbers(act)))
        {
            category = FeedbackCategory.Output;
            message = "De berekende waarden kloppen, maar de tekst van je output wijkt af van wat gevraagd wordt.";
        }
        else if (act.Length == 0)
        {
            category = FeedbackCategory.Logic;
            message = "Je programma toont geen output.";
        }
        else
        {
            category = FeedbackCategory.Logic;
            message = "De output klopt niet: er wordt een andere waarde of tekst getoond dan verwacht.";
        }
        if (first.HasValue) message += $" Eerste verschil op outputregel {first}.";
        return new CompareResult { Passed = false, Category = category, Message = message, Diff = diff, FirstDifferentLine = first };
    }

    /// <summary>Eenvoudige LCS-diff op regelniveau (outputs zijn klein).</summary>
    static List<DiffLine> Diff(List<string> expected, List<string> actual, bool contains = false)
    {
        expected = expected.Take(300).ToList();
        actual = actual.Take(300).ToList();
        int n = expected.Count, m = actual.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = Same(expected[i], actual[j], contains) ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var result = new List<DiffLine>();
        int a = 0, b = 0;
        while (a < n && b < m)
        {
            if (Same(expected[a], actual[b], contains)) { result.Add(new DiffLine { Kind = "same", Text = actual[b] }); a++; b++; }
            else if (dp[a + 1, b] >= dp[a, b + 1]) { result.Add(new DiffLine { Kind = "expected", Text = expected[a] }); a++; }
            else { result.Add(new DiffLine { Kind = "actual", Text = actual[b] }); b++; }
        }
        while (a < n) result.Add(new DiffLine { Kind = "expected", Text = expected[a++] });
        while (b < m) result.Add(new DiffLine { Kind = "actual", Text = actual[b++] });
        return result;
    }

    static bool Same(string expected, string actual, bool contains)
    {
        string e = Spaces().Replace(expected.Trim(), " "), a = Spaces().Replace(actual.Trim(), " ");
        return contains ? a.Contains(e, StringComparison.OrdinalIgnoreCase) : string.Equals(e, a, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\s*([:?!.,;=])\s*")] private static partial Regex SpaceAroundPunct();
    [GeneratedRegex(@"-?\d+(?:[.,]\d+)?")] private static partial Regex NumberRegex();
}
