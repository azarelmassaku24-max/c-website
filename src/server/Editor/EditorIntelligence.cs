using System.Collections.Immutable;
using System.Text;
using CSharpQuest.Server.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;

namespace CSharpQuest.Server.Editor;

public sealed class CompletionItemDto
{
    public string Label { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? Detail { get; init; }
    public string SortText { get; init; } = "";
    public string FilterText { get; init; } = "";
    public bool Preselect { get; init; }
}

/// <summary>Echte C# IntelliSense (completion en hover) via de Roslyn workspace-API.</summary>
public sealed class EditorIntelligence
{
    readonly ReferenceCatalog _catalog;
    readonly ILogger<EditorIntelligence> _log;
    readonly Lazy<MefHostServices?> _host;

    public EditorIntelligence(ReferenceCatalog catalog, ILogger<EditorIntelligence> log)
    {
        _catalog = catalog;
        _log = log;
        _host = new Lazy<MefHostServices?>(() =>
        {
            try { return MefHostServices.Create(MefHostServices.DefaultAssemblies); }
            catch (Exception ex) { _log.LogWarning(ex, "Roslyn workspace kon niet starten; IntelliSense uitgeschakeld"); return null; }
        });
    }

    (AdhocWorkspace, Document)? Build(IReadOnlyList<SourceFile> files, string activeFile)
    {
        var host = _host.Value;
        if (host == null) return null;
        var ws = new AdhocWorkspace(host);
        var projectId = ProjectId.CreateNewId();
        var info = ProjectInfo.Create(projectId, VersionStamp.Create(), "Program", "Program", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.ConsoleApplication).WithNullableContextOptions(NullableContextOptions.Disable),
            parseOptions: _catalog.ParseOptions,
            metadataReferences: _catalog.References);
        var project = ws.AddProject(info);
        Document? active = null;
        foreach (var f in files)
        {
            var doc = ws.AddDocument(projectId, CSharpCompilerService.NormalizePath(f.Path), SourceText.From(f.Content ?? "", Encoding.UTF8));
            if (CSharpCompilerService.NormalizePath(f.Path) == CSharpCompilerService.NormalizePath(activeFile)) active = doc;
        }
        ws.AddDocument(projectId, "GlobalUsings.g.cs",
            SourceText.From(string.Join("\n", ReferenceCatalog.ImplicitUsingNamespaces.Select(n => $"global using global::{n};"))));
        active ??= ws.CurrentSolution.GetProject(projectId)!.Documents.First();
        return (ws, ws.CurrentSolution.GetDocument(active.Id)!);
    }

    public async Task<List<CompletionItemDto>> CompleteAsync(IReadOnlyList<SourceFile> files, string activeFile, int line, int column, CancellationToken ct)
    {
        var built = Build(files, activeFile);
        if (built == null) return new();
        var (ws, doc) = built.Value;
        using (ws)
        {
            var text = await doc.GetTextAsync(ct);
            int position = Position(text, line, column);
            var service = CompletionService.GetService(doc);
            if (service == null) return new();
            var list = await service.GetCompletionsAsync(doc, position, cancellationToken: ct);
            if (list == null) return new();
            return list.ItemsList
                .Where(i => !i.DisplayText.StartsWith("__") && !i.DisplayText.StartsWith("<"))
                .Take(400)
                .Select(i => new CompletionItemDto
                {
                    Label = i.DisplayText + i.DisplayTextSuffix,
                    Kind = i.Tags.FirstOrDefault() ?? "",
                    Detail = string.IsNullOrEmpty(i.InlineDescription) ? null : i.InlineDescription,
                    SortText = i.SortText,
                    FilterText = i.FilterText,
                    Preselect = i.Rules.MatchPriority > 0,
                }).ToList();
        }
    }

    public async Task<string?> HoverAsync(IReadOnlyList<SourceFile> files, string activeFile, int line, int column, CancellationToken ct)
    {
        var built = Build(files, activeFile);
        if (built == null) return null;
        var (ws, doc) = built.Value;
        using (ws)
        {
            var text = await doc.GetTextAsync(ct);
            int position = Position(text, line, column);
            var service = QuickInfoService.GetService(doc);
            if (service == null) return null;
            var info = await service.GetQuickInfoAsync(doc, position, ct);
            if (info == null) return null;
            var sb = new StringBuilder();
            foreach (var section in info.Sections)
            {
                var t = section.Text.Trim();
                if (t.Length == 0) continue;
                if (section.Kind == QuickInfoSectionKinds.Description) sb.Append("```csharp\n").Append(t).Append("\n```\n");
                else sb.Append(t).Append("\n\n");
            }
            return sb.Length == 0 ? null : sb.ToString().Trim();
        }
    }

    static int Position(SourceText text, int line, int column)
    {
        int l = Math.Clamp(line - 1, 0, text.Lines.Count - 1);
        var tl = text.Lines[l];
        return Math.Min(tl.Start + Math.Max(0, column - 1), tl.End);
    }
}
