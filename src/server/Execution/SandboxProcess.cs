using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CSharpQuest.Server.Execution;

public sealed class SandboxLimits
{
    public TimeSpan WallTime { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan CpuTime { get; init; } = TimeSpan.FromSeconds(6);
    public long MemoryBytes { get; init; } = 600L * 1024 * 1024;
    public long GcHeapBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxOutputChars { get; init; } = 200_000;
}

/// <summary>Een gecompileerd programma, klaar om (meermaals) uitgevoerd te worden in een sandbox.</summary>
public sealed class PreparedProgram : IDisposable
{
    public string Directory { get; }
    public string AssemblyPath { get; }
    public string RuntimeConfigPath { get; }

    public PreparedProgram(string directory, string assemblyPath, string runtimeConfigPath)
    {
        Directory = directory;
        AssemblyPath = assemblyPath;
        RuntimeConfigPath = runtimeConfigPath;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch { /* bestand nog in gebruik: opruimen bij volgende start */ }
    }
}

/// <summary>
/// Start het programma van de leerling in een apart dotnet-proces:
/// - eigen werkmap (de enige map waarin bestanden gelezen/geschreven mogen worden),
/// - opgeschoonde omgevingsvariabelen (geen geheimen van de server),
/// - Windows Job Object met geheugen-, CPU- en proceslimiet,
/// - GC heap limiet, wandkloklimiet en outputlimiet.
/// </summary>
public sealed class SandboxProcess : IAsyncDisposable
{
    readonly Process _process;
    readonly IDisposable? _job;
    readonly SandboxLimits _limits;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly CancellationTokenSource _cts = new();
    readonly Task _stdoutPump, _stderrPump;
    readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _outputChars;

    public string WorkDir { get; }
    public string ReportPath { get; }
    public string TracePath { get; }
    public string? KillReason { get; private set; }
    public event Action<string>? Stdout;
    public event Action<string>? Stderr;

    readonly StringBuilder _stdoutBuffer = new();
    readonly StringBuilder _stderrBuffer = new();
    readonly bool _buffer;

    public TimeSpan Elapsed => _clock.Elapsed;

    SandboxProcess(Process process, IDisposable? job, SandboxLimits limits, string workDir, string reportPath, string tracePath, bool buffer)
    {
        _process = process;
        _job = job;
        _limits = limits;
        WorkDir = workDir;
        ReportPath = reportPath;
        TracePath = tracePath;
        _buffer = buffer;
        _stdoutPump = Pump(process.StandardOutput.BaseStream, isError: false);
        _stderrPump = Pump(process.StandardError.BaseStream, isError: true);
        _ = Watchdog();
        _ = Task.Run(async () =>
        {
            try { await process.WaitForExitAsync(); } catch { }
            try { await Task.WhenAll(_stdoutPump, _stderrPump).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _exited.TrySetResult(SafeExitCode());
        });
    }

    int SafeExitCode() { try { return _process.ExitCode; } catch { return -1; } }

    public static string DotnetHost()
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var root = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        var exe = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(exe) ? exe : "dotnet";
    }

    public static SandboxProcess Start(PreparedProgram program, string workDir, SandboxLimits limits,
        IDictionary<string, string> questEnv, bool buffer)
    {
        Directory.CreateDirectory(workDir);
        string reportPath = Path.Combine(Path.GetDirectoryName(workDir)!, Path.GetFileName(workDir) + ".report.jsonl");
        string tracePath = Path.Combine(Path.GetDirectoryName(workDir)!, Path.GetFileName(workDir) + ".trace.jsonl");

        var psi = new ProcessStartInfo
        {
            FileName = DotnetHost(),
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add("--runtimeconfig");
        psi.ArgumentList.Add(program.RuntimeConfigPath);
        psi.ArgumentList.Add(program.AssemblyPath);

        // Omgeving volledig opschonen: enkel wat het .NET-proces echt nodig heeft.
        psi.Environment.Clear();
        foreach (var key in new[] { "SystemRoot", "windir", "SystemDrive", "ComSpec" })
        {
            var v = Environment.GetEnvironmentVariable(key);
            if (v != null) psi.Environment[key] = v;
        }
        if (!OperatingSystem.IsWindows()) psi.Environment["PATH"] = "/usr/bin:/bin";
        psi.Environment["TEMP"] = workDir;
        psi.Environment["TMP"] = workDir;
        psi.Environment["TMPDIR"] = workDir;
        psi.Environment["HOME"] = workDir;
        psi.Environment["USERPROFILE"] = workDir;
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_TieredPGO"] = "0";
        psi.Environment["DOTNET_TC_QuickJitForLoops"] = "1";
        psi.Environment["DOTNET_GCHeapHardLimit"] = limits.GcHeapBytes.ToString("X");
        psi.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        psi.Environment["QUEST_SANDBOX"] = workDir;
        psi.Environment["QUEST_REPORT"] = reportPath;
        foreach (var kv in questEnv) psi.Environment[kv.Key] = kv.Value;

        IDisposable? job = null;
        if (OperatingSystem.IsWindows())
        {
            try { job = new WindowsJobObject(limits.MemoryBytes, limits.CpuTime); }
            catch { job = null; }
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        if (job is WindowsJobObject wj && OperatingSystem.IsWindows())
        {
            try { wj.Assign(process); }
            catch { /* proces is mogelijk al gestopt */ }
        }
        return new SandboxProcess(process, job, limits, workDir, reportPath, tracePath, buffer);
    }

    async Task Pump(Stream stream, bool isError)
    {
        var decoder = new UTF8Encoding(false).GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[8192];
        try
        {
            while (true)
            {
                int n = await stream.ReadAsync(bytes, _cts.Token);
                if (n <= 0) break;
                int c = decoder.GetChars(bytes, 0, n, chars, 0);
                if (c == 0) continue;
                var text = new string(chars, 0, c);
                int total = Interlocked.Add(ref _outputChars, c);
                if (total > _limits.MaxOutputChars)
                {
                    Kill("outputLimit");
                    break;
                }
                if (_buffer)
                {
                    lock (isError ? _stderrBuffer : _stdoutBuffer) (isError ? _stderrBuffer : _stdoutBuffer).Append(text);
                }
                if (isError) Stderr?.Invoke(text); else Stdout?.Invoke(text);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    async Task Watchdog()
    {
        try
        {
            while (!_process.HasExited)
            {
                await Task.Delay(100, _cts.Token);
                if (_clock.Elapsed > _limits.WallTime) { Kill("timeout"); break; }
                try
                {
                    if (_process.TotalProcessorTime > _limits.CpuTime) { Kill("cpuLimit"); break; }
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    public void Kill(string reason)
    {
        KillReason ??= reason;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
    }

    public async Task WriteInputAsync(string text)
    {
        try
        {
            if (_process.HasExited) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            await _process.StandardInput.BaseStream.WriteAsync(bytes);
            await _process.StandardInput.BaseStream.FlushAsync();
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
    }

    public void CloseInput()
    {
        try { _process.StandardInput.Close(); } catch { }
    }

    public Task<int> WaitForExitAsync() => _exited.Task;

    public string BufferedStdout { get { lock (_stdoutBuffer) return _stdoutBuffer.ToString(); } }
    public string BufferedStderr { get { lock (_stderrBuffer) return _stderrBuffer.ToString(); } }

    public TimeSpan CpuUsed
    {
        get { try { return _process.TotalProcessorTime; } catch { return TimeSpan.Zero; } }
    }

    public async ValueTask DisposeAsync()
    {
        Kill("disposed");
        _cts.Cancel();
        try { await _exited.Task.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        _job?.Dispose();
        _process.Dispose();
        _cts.Dispose();
    }
}
