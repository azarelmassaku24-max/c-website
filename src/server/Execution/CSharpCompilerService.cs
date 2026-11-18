using System.Diagnostics;
using System.Text;
using CSharpQuest.Server.Feedback;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpQuest.Server.Execution;

/// <summary>
/// Echte C#-compilatie met Roslyn.
/// Pipeline: parsen → diagnostics (vertaald) → beveiligingsanalyse → sandbox-herschrijving →
/// (optioneel) trace-instrumentatie → emit met embedded PDB (voor regelnummers in fouten).
/// </summary>
public sealed class CSharpCompilerService
{
    readonly ReferenceCatalog _catalog;
    readonly ILogger<CSharpCompilerService> _log;

    public CSharpCompilerService(ReferenceCatalog catalog, ILogger<CSharpCompilerService> log)
    {
        _catalog = catalog;
        _log = log;
    }

    public ReferenceCatalog Catalog => _catalog;

    public const int MaxFiles = 30;
    public const int MaxSourceChars = 200_000;

    public List<SyntaxTree> ParseUserFiles(IReadOnlyList<SourceFile> files)
    {
        return files.Select(f => CSharpSyntaxTree.ParseText(
            Microsoft.CodeAnalysis.Text.SourceText.From(f.Content ?? "", Encoding.UTF8),
            _catalog.ParseOptions, path: NormalizePath(f.Path))).ToList();
    }

    public static string NormalizePath(string path)
    {
        var p = (path ?? "Program.cs").Replace('\\', '/').Trim('/');
        if (p.Length == 0) p = "Program.cs";
        return p;
    }

    public CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees, CompileRequestOptions options, string? mainType = null)
    {
        var compileOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication)
            .WithOptimizationLevel(OptimizationLevel.Debug)
            .WithNullableContextOptions(NullableContextOptions.Disable)
            .WithAllowUnsafe(false)
            .WithWarningLevel(4)
            .WithConcurrentBuild(true)
            .WithMainTypeName(mainType ?? options.MainType)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
            {
                ["CS8019"] = ReportDiagnostic.Suppress, // onnodige using
                ["CS1591"] = ReportDiagnostic.Suppress,
                ["CS0105"] = ReportDiagnostic.Suppress, // dubbele using (impliciete usings)
            });
        var all = new List<SyntaxTree>(trees);
        if (options.ImplicitUsings) all.Add(_catalog.ImplicitUsingsTree());
        return CSharpCompilation.Create("Program", all, _catalog.References, compileOptions);
    }

    /// <summary>Snelle controle voor de editor: enkel diagnostics, geen uitvoerbare code.</summary>
    public List<CodeDiagnostic> Diagnose(IReadOnlyList<SourceFile> files, CompileRequestOptions options)
    {
        var trees = ParseUserFiles(files);
        var compilation = CreateCompilation(trees, options);
        var list = CollectDiagnostics(compilation, trees);
        if (!list.Any(d => d.Severity == "error"))
            foreach (var t in trees) list.AddRange(SecurityAnalyzer.Analyze(t, compilation.GetSemanticModel(t)));
        return list;
    }

    List<CodeDiagnostic> CollectDiagnostics(CSharpCompilation compilation, List<SyntaxTree> userTrees)
    {
        var userSet = new HashSet<SyntaxTree>(userTrees);
        var syntaxIds = new HashSet<Diagnostic>();
        foreach (var t in userTrees)
            foreach (var d in t.GetDiagnostics()) syntaxIds.Add(d);

        var result = new List<CodeDiagnostic>();
        foreach (var d in compilation.GetDiagnostics())
        {
            if (d.Severity == DiagnosticSeverity.Hidden) continue;
            if (d.Location.IsInSource && !userSet.Contains(d.Location.SourceTree!)) continue;
            bool isSyntax = d.Location.IsInSource && syntaxIds.Any(s => s.Id == d.Id && s.Location.SourceSpan == d.Location.SourceSpan);
            result.Add(DiagnosticTranslator.Translate(d, isSyntax));
        }
        // Max 30 meldingen, fouten eerst.
        return result.OrderBy(d => d.Severity == "error" ? 0 : 1).ThenBy(d => d.File).ThenBy(d => d.Line).Take(40).ToList();
    }

    public CompileOutcome Compile(IReadOnlyList<SourceFile> files, CompileRequestOptions options)
    {
        var sw = Stopwatch.StartNew();
        if (files.Count == 0 || files.Count > MaxFiles || files.Sum(f => f.Content?.Length ?? 0) > MaxSourceChars)
            return Fail("Q-LIMIT", "Je project is te groot (maximaal 30 bestanden en 200.000 tekens).");

        var userTrees = ParseUserFiles(files);
        var entryPoints = FindEntryPoints(userTrees);

        string? mainType = options.MainType;
        bool isTestBuild = options.TestHarness != null;
        var original = CreateCompilation(userTrees, options, isTestBuild ? null : mainType);
        var diagnostics = CollectDiagnostics(original, userTrees);
        if (isTestBuild)
            diagnostics.RemoveAll(d => d.Code is "CS0017"); // meerdere Main methods zijn ok bij tests
        if (diagnostics.Any(d => d.Severity == "error"))
            return new CompileOutcome { Success = false, Diagnostics = diagnostics, CompileMs = sw.ElapsedMilliseconds, EntryPoints = entryPoints };

        // Beveiliging
        var security = new List<CodeDiagnostic>();
        foreach (var t in userTrees) security.AddRange(SecurityAnalyzer.Analyze(t, original.GetSemanticModel(t)));
        if (security.Count > 0)
            return new CompileOutcome { Success = false, Diagnostics = diagnostics.Concat(security).ToList(), CompileMs = sw.ElapsedMilliseconds, EntryPoints = entryPoints };

        // Sandbox-herschrijving (met semantisch model van de originele compilatie)
        var rewritten = new List<SyntaxTree>();
        foreach (var t in userTrees)
        {
            var root = new SandboxRewriter(original.GetSemanticModel(t)).Visit(t.GetRoot())!;
            if (isTestBuild) root = new TestAccessRewriter().Visit(root)!;
            rewritten.Add(t.WithRootAndOptions(root, _catalog.ParseOptions));
        }

        var extra = new List<SyntaxTree> { _catalog.RuntimeTree };
        if (isTestBuild)
        {
            var usings = string.Join("\n", userTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
                .Select(n => n.Name.ToString()).Distinct().Select(n => $"using {n};"));
            extra.Add(CSharpSyntaxTree.ParseText("using QuestRuntime;\n" + usings + "\n#line 1 \"Tests.cs\"\n" + options.TestHarness, _catalog.ParseOptions, path: "QuestTests.cs", encoding: Encoding.UTF8));
            mainType = "__QuestTests";
        }

        var traceVars = new List<TraceVariableInfo>();
        string? traceWarning = null;
        var finalTrees = rewritten;
        if (options.Trace)
        {
            (finalTrees, traceVars, traceWarning) = Instrument(rewritten, extra, options, mainType);
        }

        var final = CreateCompilation(finalTrees.Concat(extra), options, mainType);
        using var pe = new MemoryStream();
        var emit = final.Emit(pe, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));
        if (!emit.Success)
        {
            var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            _log.LogWarning("Interne compilatiefout na herschrijven: {Errors}", string.Join(" | ", errors.Take(5)));
            var translated = errors.Where(e => e.Location.SourceTree?.FilePath != "QuestRuntime.cs")
                .Select(e => DiagnosticTranslator.Translate(e, false)).ToList();
            if (translated.Count == 0)
                translated.Add(new CodeDiagnostic { Code = "Q-INTERNAL", Category = FeedbackCategory.Compile, Message = "Interne fout bij het voorbereiden van je programma. Probeer het opnieuw of vereenvoudig je code." });
            return new CompileOutcome { Success = false, Diagnostics = diagnostics.Concat(translated).ToList(), CompileMs = sw.ElapsedMilliseconds, EntryPoints = entryPoints };
        }

        return new CompileOutcome
        {
            Success = true,
            Diagnostics = diagnostics,
            Assembly = pe.ToArray(),
            CompileMs = sw.ElapsedMilliseconds,
            TraceVariables = traceVars,
            TraceWarning = traceWarning,
            EntryPoints = entryPoints,
        };
    }

    (List<SyntaxTree>, List<TraceVariableInfo>, string?) Instrument(List<SyntaxTree> trees, List<SyntaxTree> extra, CompileRequestOptions options, string? mainType)
    {
        var skip = new HashSet<string>();
        var baseCompilation = CreateCompilation(trees.Concat(extra), options, mainType);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var vars = new List<TraceVariableInfo>();
            var instrumented = new List<SyntaxTree>();
            foreach (var t in trees)
            {
                var ins = new TraceInstrumenter(baseCompilation.GetSemanticModel(t), skip);
                var root = ins.Visit(t.GetRoot())!;
                instrumented.Add(t.WithRootAndOptions(root, _catalog.ParseOptions));
                vars.AddRange(ins.Variables);
            }
            var test = CreateCompilation(instrumented.Concat(extra), options, mainType);
            var errors = test.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count == 0) return (instrumented, vars, null);

            bool progress = false;
            foreach (var e in errors)
            {
                var tree = e.Location.SourceTree;
                if (tree == null) continue;
                var node = tree.GetRoot().FindNode(e.Location.SourceSpan, getInnermostNodeForTie: true);
                var call = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault(i => i.Expression.ToString().StartsWith("global::QuestRuntime.QTrace.", StringComparison.Ordinal));
                if (call == null) continue;
                var args = call.ArgumentList.Arguments;
                if (args.Count == 0 || args[0].Expression is not LiteralExpressionSyntax lineLit) continue;
                string name = node is IdentifierNameSyntax id ? id.Identifier.ValueText : node.ToString().TrimStart('@');
                if (skip.Add(lineLit.Token.ValueText + ":" + name)) progress = true;
            }
            if (!progress)
            {
                _log.LogInformation("Trace-instrumentatie niet mogelijk: {Errors}", string.Join(" | ", errors.Take(3)));
                return (trees, new List<TraceVariableInfo>(), "Deze code kon niet volledig getraceerd worden; het programma wordt zonder traceerinformatie uitgevoerd.");
            }
        }
        return (trees, new List<TraceVariableInfo>(), "Deze code kon niet volledig getraceerd worden.");
    }

    public static List<string> FindEntryPoints(IEnumerable<SyntaxTree> trees)
    {
        var result = new List<string>();
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            if (root.DescendantNodes().OfType<GlobalStatementSyntax>().Any()) result.Add("(top-level statements)");
            foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (m.Identifier.ValueText != "Main" || !m.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
                var parts = new List<string>();
                foreach (var a in m.Ancestors())
                {
                    if (a is BaseTypeDeclarationSyntax t) parts.Insert(0, t.Identifier.ValueText);
                    else if (a is BaseNamespaceDeclarationSyntax n) parts.Insert(0, n.Name.ToString());
                }
                result.Add(string.Join(".", parts));
            }
        }
        return result.Distinct().ToList();
    }

    static CompileOutcome Fail(string code, string message) => new()
    {
        Success = false,
        Diagnostics = { new CodeDiagnostic { Code = code, Message = message, Category = FeedbackCategory.Compile } },
    };

    /// <summary>Warmt Roslyn op zodat de eerste echte compilatie snel is.</summary>
    public void WarmUp()
    {
        try
        {
            Compile(new[] { new SourceFile("Program.cs", "class Program { static void Main() { int x = 1; System.Console.WriteLine(x); } }") },
                new CompileRequestOptions { Trace = true });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Opwarmen van de compiler mislukt"); }
    }
}
