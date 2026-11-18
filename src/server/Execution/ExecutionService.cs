using System.Text;
using System.Text.Json;
using CSharpQuest.Server.Feedback;

namespace CSharpQuest.Server.Execution;

public sealed class RunSpec
{
    public string Stdin { get; init; } = "";
    public int? Seed { get; init; }
    public bool TestMode { get; init; } = true;
    public bool Trace { get; init; }
    public Dictionary<string, string> Files { get; init; } = new();
    public SandboxLimits Limits { get; init; } = new();
    public string Culture { get; init; } = "nl-BE";
    public int Cols { get; init; } = 100;
    public int Rows { get; init; } = 30;
}

/// <summary>Coördineert compileren en uitvoeren in de sandbox.</summary>
public sealed class ExecutionService
{
    readonly CSharpCompilerService _compiler;
    readonly ILogger<ExecutionService> _log;
    readonly SemaphoreSlim _gate;
    readonly string _root;
    int _counter;

    public ExecutionService(CSharpCompilerService compiler, ILogger<ExecutionService> log, IConfiguration config)
    {
        _compiler = compiler;
        _log = log;
        int max = config.GetValue<int?>("Sandbox:MaxConcurrentRuns") ?? Math.Max(2, Environment.ProcessorCount / 2);
        _gate = new SemaphoreSlim(max, max);
        _root = config.GetValue<string>("Sandbox:Root") ?? Path.Combine(Path.GetTempPath(), "csharpquest-sandbox");
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        Directory.CreateDirectory(_root);
    }

    public CSharpCompilerService Compiler => _compiler;

    public PreparedProgram Prepare(byte[] assembly)
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(dir, "Program.dll");
        File.WriteAllBytes(dll, assembly);
        var version = $"{Environment.Version.Major}.{Environment.Version.Minor}.0";
        var config = Path.Combine(dir, "Program.runtimeconfig.json");
        File.WriteAllText(config, $$"""
        {
          "runtimeOptions": {
            "tfm": "net{{Environment.Version.Major}}.{{Environment.Version.Minor}}",
            "framework": { "name": "Microsoft.NETCore.App", "version": "{{version}}" },
            "configProperties": {
              "System.GC.Server": false,
              "System.GC.Concurrent": false,
              "System.Globalization.Invariant": false,
              "System.Runtime.TieredPGO": false
            }
          }
        }
        """);
        return new PreparedProgram(dir, dll, config);
    }

    public string NewWorkDir(PreparedProgram program) =>
        Path.Combine(program.Directory, "run" + Interlocked.Increment(ref _counter));

    public static Dictionary<string, string> QuestEnvironment(RunSpec spec, string? tracePath)
    {
        var env = new Dictionary<string, string>
        {
            ["QUEST_MODE"] = spec.TestMode ? "test" : "interactive",
            ["QUEST_CULTURE"] = spec.Culture,
            ["QUEST_COLS"] = spec.Cols.ToString(),
            ["QUEST_ROWS"] = spec.Rows.ToString(),
        };
        if (spec.Seed.HasValue) env["QUEST_SEED"] = spec.Seed.Value.ToString();
        if (tracePath != null) env["QUEST_TRACE"] = tracePath;
        return env;
    }

    public static void WriteFiles(string workDir, Dictionary<string, string> files)
    {
        Directory.CreateDirectory(workDir);
        foreach (var (name, content) in files)
        {
            var safe = SafeRelative(name);
            if (safe == null) continue;
            var full = Path.Combine(workDir, safe);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content ?? "", new UTF8Encoding(false));
        }
    }

    static string? SafeRelative(string name)
    {
        var n = name.Replace('\\', '/').TrimStart('/');
        if (n.Length == 0 || n.Contains("..") || Path.IsPathRooted(n) || n.Contains(':')) return null;
        return n.Replace('/', Path.DirectorySeparatorChar);
    }

    public static Dictionary<string, string> CollectFiles(string workDir)
    {
        var result = new Dictionary<string, string>();
        if (!Directory.Exists(workDir)) return result;
        foreach (var f in Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories).Take(50))
        {
            var info = new FileInfo(f);
            if (info.Length > 256 * 1024) continue;
            var rel = Path.GetRelativePath(workDir, f).Replace('\\', '/');
            try { result[rel] = File.ReadAllText(f); } catch { }
        }
        return result;
    }

    /// <summary>Voert een voorbereid programma één keer uit met gegeven invoer (niet-interactief).</summary>
    public async Task<RunOutcome> RunAsync(PreparedProgram program, RunSpec spec, IReadOnlyCollection<string> userFiles, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var workDir = NewWorkDir(program);
            WriteFiles(workDir, spec.Files);
            string tracePath = workDir + ".trace.jsonl";
            await using var proc = SandboxProcess.Start(program, workDir, spec.Limits, QuestEnvironment(spec, spec.Trace ? tracePath : null), buffer: true);
            var stdin = (spec.Stdin ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            _ = Task.Run(async () =>
            {
                await proc.WriteInputAsync(stdin);
                proc.CloseInput();
            }, ct);
            int exitCode;
            using (ct.Register(() => proc.Kill("cancelled")))
                exitCode = await proc.WaitForExitAsync();

            var outcome = new RunOutcome
            {
                Output = proc.BufferedStdout,
                Error = proc.BufferedStderr,
                ExitCode = exitCode,
                DurationMs = (long)proc.Elapsed.TotalMilliseconds,
            };
            ReadReport(proc.ReportPath, outcome, userFiles);
            Classify(outcome, proc.KillReason);
            outcome.Files = CollectFiles(workDir);
            if (spec.Trace && File.Exists(tracePath))
            {
                var info = new FileInfo(tracePath);
                outcome.TraceJsonl = info.Length > 12 * 1024 * 1024 ? null : await File.ReadAllTextAsync(tracePath, ct);
            }
            return outcome;
        }
        finally { _gate.Release(); }
    }

    public static void ReadReport(string reportPath, RunOutcome outcome, IReadOnlyCollection<string> userFiles)
    {
        if (!File.Exists(reportPath)) return;
        foreach (var line in File.ReadLines(reportPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "exception":
                        {
                            var info = new RuntimeErrorInfo
                            {
                                Type = root.GetProperty("exType").GetString() ?? "",
                                Message = root.GetProperty("message").GetString() ?? "",
                                Inner = root.TryGetProperty("inner", out var inner) ? inner.GetString() : null,
                            };
                            foreach (var f in root.GetProperty("frames").EnumerateArray())
                            {
                                string method = f.GetProperty("method").GetString() ?? "";
                                string file = f.GetProperty("file").GetString() ?? "";
                                int lineNo = f.GetProperty("line").GetInt32();
                                info.Stack.Add(lineNo > 0 ? $"{method} (regel {lineNo} in {file})" : method);
                                if (info.Line == null && lineNo > 0 && userFiles.Any(u => Path.GetFileName(u) == file))
                                {
                                    info.Line = lineNo;
                                    info.File = userFiles.First(u => Path.GetFileName(u) == file);
                                    info.Method = method;
                                }
                            }
                            RuntimeErrorTranslator.Enrich(info);
                            outcome.Exception = info;
                            break;
                        }
                    case "inputExhausted":
                        outcome.LinesRead = root.GetProperty("linesRead").GetInt32();
                        outcome.Status = "inputExhausted";
                        break;
                    case "check":
                        outcome.Checks.Add(new CheckResult
                        {
                            Label = root.GetProperty("label").GetString() ?? "",
                            Passed = root.GetProperty("passed").GetBoolean(),
                            Expected = root.GetProperty("expected").GetString(),
                            Actual = root.GetProperty("actual").GetString(),
                            Note = root.TryGetProperty("note", out var note) ? note.GetString() : null,
                        });
                        break;
                }
            }
            catch (JsonException) { }
        }
    }

    public static void Classify(RunOutcome outcome, string? killReason)
    {
        if (killReason is "timeout" or "cpuLimit" or "outputLimit" or "cancelled")
            outcome.Status = killReason;
        else if (outcome.Status == "inputExhausted") { }
        else if (outcome.Exception != null) outcome.Status = "exception";
        else if (outcome.ExitCode == -1073741571 || outcome.ExitCode == 134 && outcome.Error.Contains("Stack overflow") || outcome.Error.Contains("Stack overflow."))
            outcome.Status = "stackOverflow";
        else if (outcome.ExitCode == -1073741801 || outcome.ExitCode == -532462766 && outcome.Error.Contains("OutOfMemory"))
            outcome.Status = "memory";
        else if (outcome.ExitCode != 0) outcome.Status = "exitCode";
        else outcome.Status = "ok";

        if (outcome.Status is not ("ok" or "exception" or "exitCode"))
        {
            var (title, explanation, _) = RuntimeErrorTranslator.ForStatus(outcome.Status);
            outcome.Message = title + (explanation.Length > 0 ? ": " + explanation : "");
        }
        if (outcome.Status == "stackOverflow")
        {
            outcome.Exception = new RuntimeErrorInfo { Type = "System.StackOverflowException", Message = "Stack overflow" };
            RuntimeErrorTranslator.Enrich(outcome.Exception);
            // Interne .NET-stackdump niet tonen.
            outcome.Error = "";
        }
    }
}
