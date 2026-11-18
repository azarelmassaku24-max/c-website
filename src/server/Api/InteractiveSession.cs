using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CSharpQuest.Server.Execution;

namespace CSharpQuest.Server.Api;

/// <summary>
/// Een interactieve uitvoering via WebSocket: de browserterminal stuurt toetsaanslagen,
/// de server streamt de output van het programma terug. Zo kan de leerling echte
/// consolespelletjes (getal raden, Simon, ...) spelen.
/// </summary>
public static class InteractiveSession
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    sealed class StartMessage
    {
        public string Type { get; set; } = "";
        public List<SourceFile>? Files { get; set; }
        public string? MainType { get; set; }
        public int Cols { get; set; } = 100;
        public int Rows { get; set; } = 30;
        public Dictionary<string, string>? InitialFiles { get; set; }
        public string? Culture { get; set; }
        public string? Data { get; set; }
        public int? Seed { get; set; }
    }

    public static async Task RunAsync(WebSocket socket, ExecutionService exec, ILogger log, CancellationToken aborted)
    {
        var outbox = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        var sender = SendLoop(socket, outbox.Reader, cts.Token);
        SandboxProcess? process = null;
        PreparedProgram? program = null;
        Task? exitWatcher = null;

        try
        {
            while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                var msg = await ReceiveAsync(socket, cts.Token);
                if (msg == null) break;
                switch (msg.Type)
                {
                    case "start":
                        {
                            if (process != null) { await process.DisposeAsync(); process = null; }
                            program?.Dispose();
                            program = null;
                            var files = msg.Files ?? new();
                            outbox.Writer.TryWrite(new { type = "status", phase = "compiling" });
                            var compiled = await Task.Run(() => exec.Compiler.Compile(files, new CompileRequestOptions { MainType = msg.MainType }), cts.Token);
                            outbox.Writer.TryWrite(new { type = "compiled", success = compiled.Success, diagnostics = compiled.Diagnostics, entryPoints = compiled.EntryPoints, compileMs = compiled.CompileMs });
                            if (!compiled.Success) break;

                            program = exec.Prepare(compiled.Assembly!);
                            var workDir = exec.NewWorkDir(program);
                            ExecutionService.WriteFiles(workDir, msg.InitialFiles ?? new());
                            var spec = new RunSpec
                            {
                                TestMode = false,
                                Cols = Math.Clamp(msg.Cols, 20, 300),
                                Rows = Math.Clamp(msg.Rows, 5, 120),
                                Culture = msg.Culture ?? "nl-BE",
                                Seed = msg.Seed,
                            };
                            var limits = new SandboxLimits
                            {
                                WallTime = TimeSpan.FromMinutes(15),
                                CpuTime = TimeSpan.FromSeconds(20),
                                MaxOutputChars = 2_000_000,
                            };
                            var proc = SandboxProcess.Start(program, workDir, limits, ExecutionService.QuestEnvironment(spec, null), buffer: false);
                            process = proc;
                            proc.Stdout += s => outbox.Writer.TryWrite(new { type = "out", data = s });
                            proc.Stderr += s => outbox.Writer.TryWrite(new { type = "err", data = s });
                            outbox.Writer.TryWrite(new { type = "started" });
                            var userPaths = files.Select(f => CSharpCompilerService.NormalizePath(f.Path)).ToList();
                            exitWatcher = Task.Run(async () =>
                            {
                                int code = await proc.WaitForExitAsync();
                                var outcome = new RunOutcome { ExitCode = code, DurationMs = (long)proc.Elapsed.TotalMilliseconds };
                                ExecutionService.ReadReport(proc.ReportPath, outcome, userPaths);
                                ExecutionService.Classify(outcome, proc.KillReason == "stopped" ? null : proc.KillReason);
                                outbox.Writer.TryWrite(new
                                {
                                    type = "exit",
                                    status = proc.KillReason == "stopped" ? "stopped" : outcome.Status,
                                    exitCode = code,
                                    durationMs = outcome.DurationMs,
                                    exception = outcome.Exception,
                                    message = outcome.Message,
                                    files = ExecutionService.CollectFiles(workDir),
                                });
                            });
                            break;
                        }
                    case "input":
                        if (process != null && msg.Data != null) await process.WriteInputAsync(msg.Data);
                        break;
                    case "stop":
                        process?.Kill("stopped");
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex) { log.LogError(ex, "Interactieve sessie mislukt"); }
        finally
        {
            if (process != null)
            {
                process.Kill("stopped");
                if (exitWatcher != null) { try { await exitWatcher.WaitAsync(TimeSpan.FromSeconds(3)); } catch { } }
                await process.DisposeAsync();
            }
            program?.Dispose();
            outbox.Writer.TryComplete();
            try { await sender.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            cts.Cancel();
            if (socket.State == WebSocketState.Open)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "klaar", CancellationToken.None); } catch { }
            }
        }
    }

    static async Task<StartMessage?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > 1_000_000) return null;
            if (result.EndOfMessage) break;
        }
        try { return JsonSerializer.Deserialize<StartMessage>(ms.ToArray(), Json); }
        catch (JsonException) { return new StartMessage(); }
    }

    static async Task SendLoop(WebSocket socket, ChannelReader<object> reader, CancellationToken ct)
    {
        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                // Opeenvolgende output-stukjes bundelen tot één bericht.
                var pending = new StringBuilder();
                while (reader.TryRead(out var item))
                {
                    var json = JsonSerializer.Serialize(item, Json);
                    if (pending.Length > 0) pending.Append('\n');
                    pending.Append(json);
                    if (pending.Length > 32_000) break;
                }
                if (socket.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(pending.ToString());
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (ChannelClosedException) { }
    }
}
