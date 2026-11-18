using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpQuest.Server.Execution;

/// <summary>
/// Levert de metadata-referenties (de .NET basisbibliotheken) waartegen de code van de leerling
/// gecompileerd wordt, en de vooraf geparste runtime-laag.
/// </summary>
public sealed class ReferenceCatalog
{
    public ImmutableArray<MetadataReference> References { get; }
    public SyntaxTree RuntimeTree { get; }
    public CSharpParseOptions ParseOptions { get; } = new(LanguageVersion.Latest, DocumentationMode.None);
    public string RuntimeDirectory { get; }

    public static readonly string[] ImplicitUsingNamespaces =
    {
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Threading",
    };

    public ReferenceCatalog()
    {
        RuntimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => string.Equals(Path.GetDirectoryName(p), RuntimeDirectory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (tpa.Count == 0)
            tpa = Directory.GetFiles(RuntimeDirectory, "System*.dll").Append(Path.Combine(RuntimeDirectory, "mscorlib.dll")).ToList();

        var refs = new List<MetadataReference>();
        foreach (var path in tpa.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { refs.Add(MetadataReference.CreateFromFile(path)); }
            catch { /* native of onleesbaar: overslaan */ }
        }
        References = refs.ToImmutableArray();

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestRuntime.cs")
            ?? throw new InvalidOperationException("Runtime resource QuestRuntime.cs ontbreekt.");
        using var reader = new StreamReader(stream);
        RuntimeTree = CSharpSyntaxTree.ParseText(reader.ReadToEnd(), ParseOptions, path: "QuestRuntime.cs", encoding: System.Text.Encoding.UTF8);
    }

    public SyntaxTree ImplicitUsingsTree() =>
        CSharpSyntaxTree.ParseText(string.Join("\n", ImplicitUsingNamespaces.Select(n => $"global using global::{n};")),
            ParseOptions, path: "GlobalUsings.g.cs", encoding: System.Text.Encoding.UTF8);
}
