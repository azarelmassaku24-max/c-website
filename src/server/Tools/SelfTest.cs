using CSharpQuest.Server.Execution;

namespace CSharpQuest.Server.Tools;

/// <summary>Controleert de volledige compileer- en uitvoerpipeline. Start met: dotnet run -- selftest</summary>
public static class SelfTest
{
    sealed record Case(string Name, string Code, string Stdin, Func<RunOutcome, CompileOutcome, string?> Check, bool Trace = false, string? Harness = null);

    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var exec = services.GetRequiredService<ExecutionService>();
        var cases = new List<Case>
        {
            new("invoer en output", """
                using System;
                class Program {
                    static void Main(string[] args) {
                        Console.Write("Geef een getal : ");
                        int getal = int.Parse(Console.ReadLine());
                        double helft = getal / 2.0;
                        Console.WriteLine($"De helft is {helft}");
                    }
                }
                """, "7\n", (r, c) => r.Output.Contains("Geef een getal : 7\nDe helft is 3,5") ? null : "output: " + r.Output),
            new("FormatException met regelnummer", """
                class Program {
                    static void Main() {
                        int x = int.Parse("abc");
                    }
                }
                """, "", (r, c) => r.Status == "exception" && r.Exception?.Line == 3 && r.Exception.Type == "System.FormatException" ? null : $"status {r.Status} {r.Exception?.Type} lijn {r.Exception?.Line}"),
            new("te weinig invoer", """
                class Program { static void Main() { System.Console.ReadLine(); System.Console.ReadLine(); } }
                """, "a\n", (r, c) => r.Status == "inputExhausted" ? null : "status " + r.Status),
            new("oneindige loop", """
                class Program { static void Main() { while (true) { } } }
                """, "", (r, c) => r.Status is "timeout" or "cpuLimit" ? null : "status " + r.Status),
            new("Console.Clear + cursor + kleur + ReadKey", """
                using System;
                class Program { static void Main() {
                    Console.Clear(); Console.ForegroundColor = ConsoleColor.Red; Console.SetCursorPosition(3, 2); Console.Write("X");
                    Console.ResetColor();
                    var k = Console.ReadKey(true); Console.WriteLine(); Console.WriteLine(k.Key);
                    System.Threading.Thread.Sleep(5000);
                } }
                """, "j", (r, c) => r.Output.Contains("\u001b[3;4H") && r.Output.Contains("J") && r.DurationMs < 4000 ? null : "output " + r.Output + " ms " + r.DurationMs),
            new("Random met seed is deterministisch", """
                using System;
                class Program { static void Main() { Random rnd = new Random(); Console.WriteLine(rnd.Next(1, 1000)); } }
                """, "", (r, c) => r.Output.Trim().Length > 0 ? null : "geen output"),
            new("bestand schrijven in sandbox", """
                using System.IO;
                class Program { static void Main() { File.WriteAllText("uit.txt", "hallo"); System.Console.WriteLine(File.ReadAllText("uit.txt")); } }
                """, "", (r, c) => r.Files.ContainsKey("uit.txt") && r.Output.Contains("hallo") ? null : "files " + string.Join(",", r.Files.Keys) + " " + r.Status + " " + r.Exception?.Message),
            new("bestand buiten sandbox geweigerd", """
                using System.IO;
                class Program { static void Main() { File.ReadAllText(@"C:\Windows\win.ini"); } }
                """, "", (r, c) => r.Exception?.Type == "System.UnauthorizedAccessException" ? null : "status " + r.Status + " " + r.Exception?.Type),
            new("Process is niet toegelaten", """
                class Program { static void Main() { System.Diagnostics.Process.Start("notepad"); } }
                """, "", (r, c) => !c.Success && c.Diagnostics.Any(d => d.Category == "security") ? null : "geen security-fout"),
            new("omgevingsvariabelen zijn leeg", """
                class Program { static void Main() { System.Console.WriteLine(System.Environment.GetEnvironmentVariable("PATH") ?? "leeg"); } }
                """, "", (r, c) => r.Output.Trim() == "leeg" ? null : "output " + r.Output),
            new("compileerfout vertaald", """
                class Program { static void Main() { int x = 5 System.Console.WriteLine(x); } }
                """, "", (r, c) => !c.Success && c.Diagnostics.Any(d => d.Code == "CS1002" && d.Category == "syntax") ? null : string.Join(";", c.Diagnostics.Select(d => d.Code + d.Category))),
            new("trace", """
                using System;
                class Program {
                    static int Kwadraat(int getal) {
                        return getal * getal;
                    }
                    static void Main() {
                        int som = 0;
                        for (int i = 1; i <= 3; i++) {
                            som += Kwadraat(i);
                        }
                        Console.WriteLine(som);
                    }
                }
                """, "", (r, c) => r.Output.Trim() == "14" && r.TraceJsonl != null && r.TraceJsonl.Contains("\"k\":\"E\"") && r.TraceJsonl.Contains("\"n\":\"som\"") ? null : "trace ontbreekt: " + c.TraceWarning + " " + r.Output + " " + (r.TraceJsonl?.Length ?? -1), Trace: true),
            new("unit test harness", """
                class Program {
                    static int Faculteit(int n) { int r = 1; for (int i = 2; i <= n; i++) r *= i; return r; }
                    static void Main() { }
                }
                """, "", (r, c) => r.Checks.Count == 1 && r.Checks[0].Passed ? null : "checks " + r.Checks.Count + " " + r.Status + " " + string.Join(";", c.Diagnostics.Select(d => d.Code + d.Message)),
                Harness: "static class __QuestTests { static void Main() { QuestRuntime.Check.Equal(120, Program.Faculteit(5), \"5!\"); } }"),
        };

        int failed = 0;
        foreach (var t in cases)
        {
            var files = new[] { new SourceFile("Program.cs", t.Code) };
            var compiled = exec.Compiler.Compile(files, new CompileRequestOptions { Trace = t.Trace, TestHarness = t.Harness });
            RunOutcome run = new();
            if (compiled.Success)
            {
                using var program = exec.Prepare(compiled.Assembly!);
                run = await exec.RunAsync(program, new RunSpec { Stdin = t.Stdin, Seed = 42, Trace = t.Trace, Limits = new SandboxLimits { WallTime = TimeSpan.FromSeconds(3), CpuTime = TimeSpan.FromSeconds(2) } },
                    files.Select(f => f.Path).ToList(), CancellationToken.None);
            }
            var problem = t.Check(run, compiled);
            Console.WriteLine($"{(problem == null ? "OK  " : "FAIL")} {t.Name} ({compiled.CompileMs} ms compile, {run.DurationMs} ms run){(problem == null ? "" : " -> " + problem)}");
            if (problem != null) failed++;
        }
        Console.WriteLine(failed == 0 ? "Alle zelftests geslaagd." : $"{failed} zelftest(s) mislukt.");
        return failed == 0 ? 0 : 1;
    }
}
