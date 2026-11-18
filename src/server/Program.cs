using CSharpQuest.Server.Api;
using CSharpQuest.Server.Curriculum;
using CSharpQuest.Server.Editor;
using CSharpQuest.Server.Execution;
using CSharpQuest.Server.Grading;
using CSharpQuest.Server.Progress;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ReferenceCatalog>();
builder.Services.AddSingleton<CSharpCompilerService>();
builder.Services.AddSingleton<ExecutionService>();
builder.Services.AddSingleton<GradingService>();
builder.Services.AddSingleton<EditorIntelligence>();
builder.Services.AddSingleton<CurriculumRepository>();
builder.Services.AddSingleton<IProgressStore, FileProgressStore>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// ----------------------------------------------------------- CLI commando's
if (args.Contains("selftest")) return await CSharpQuest.Server.Tools.SelfTest.RunAsync(app.Services);
if (args.Contains("verify")) return await CSharpQuest.Server.Tools.CurriculumVerifier.RunAsync(app.Services, args);

// Centrale foutafhandeling: nooit stacktraces van de server naar de browser.
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(error, "Onverwachte fout bij {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Er liep iets mis op de server. Probeer het opnieuw." });
}));

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

// Front-end (src/web) als statische bestanden.
var webRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, app.Configuration["Paths:Web"] ?? "../web"));
var webFiles = new PhysicalFileProvider(webRoot);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = webFiles,
    OnPrepareResponse = ctx =>
    {
        // Eigen code altijd vers laden; vendor-bibliotheken mogen gecachet worden.
        bool vendor = ctx.File.PhysicalPath?.Contains("vendor") == true;
        ctx.Context.Response.Headers.CacheControl = vendor ? "public, max-age=604800" : "no-cache";
    },
});

Endpoints.Map(app);
app.MapFallback(async ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
});

// Compiler opwarmen zodat de eerste "Run" snel is.
_ = Task.Run(() =>
{
    app.Services.GetRequiredService<CSharpCompilerService>().WarmUp();
    _ = app.Services.GetRequiredService<CurriculumRepository>().Current;
});

app.Logger.LogInformation("C# Quest draait. Open {Url} in je browser.", app.Urls.FirstOrDefault() ?? "http://localhost:5080");
app.Run();
return 0;
