using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpQuest.Server.Execution;

/// <summary>
/// Eerste verdedigingslinie van de sandbox: controleert met het semantisch model welke .NET-API's
/// de leerling gebruikt. Alleen namespaces die in de cursus nodig zijn, zijn toegelaten.
/// (Tweede linie: aparte, beperkte process. Derde linie: padbewaking tijdens de uitvoering.)
/// </summary>
public static class SecurityAnalyzer
{
    static readonly string[] AllowedNamespacePrefixes =
    {
        "System.Collections", "System.Linq", "System.Text", "System.IO", "System.Globalization",
        "System.Numerics", "System.Buffers", "System.ComponentModel",
    };

    static readonly HashSet<string> AllowedExactNamespaces = new()
    {
        "System", "System.Threading", "System.Diagnostics", "System.Runtime.CompilerServices",
    };

    // Types in toegelaten namespaces die toch niet mogen.
    static readonly HashSet<string> DeniedTypes = new()
    {
        "System.AppDomain", "System.Activator", "System.Runtime.InteropServices.Marshal",
        "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo", "System.Diagnostics.Debugger",
        "System.Diagnostics.EventLog", "System.Diagnostics.Tracing.EventSource",
        "System.IO.Pipes.NamedPipeClientStream", "System.IO.Pipes.NamedPipeServerStream",
        "System.IO.MemoryMappedFiles.MemoryMappedFile", "System.IO.FileSystemWatcher",
        "System.Threading.Thread", "System.Threading.ThreadPool", "System.Threading.Timer",
        "System.Threading.Tasks.Task", "System.Threading.Tasks.Parallel", "System.Runtime.CompilerServices.Unsafe",
        "System.Runtime.CompilerServices.RuntimeHelpers", "System.Type",
    };

    // Toegelaten leden van overigens geweigerde types.
    static readonly HashSet<string> AllowedMembers = new()
    {
        "System.Threading.Thread.Sleep", "System.Type.Name", "System.Type.FullName", "System.Type.ToString",
        "System.Type.Equals", "System.Type.GetHashCode", "System.Type.op_Equality", "System.Type.op_Inequality",
        "System.Type.IsValueType", "System.Type.IsClass",
    };

    static readonly HashSet<string> DeniedMembers = new()
    {
        "System.IO.Directory.SetCurrentDirectory", "System.IO.File.CreateSymbolicLink",
        "System.IO.Directory.CreateSymbolicLink", "System.IO.File.ResolveLinkTarget",
        "System.Environment.FailFast", "System.Environment.SetEnvironmentVariable",
        "System.Console.SetOut", "System.Console.SetIn", "System.Console.SetError",
        "System.Console.OpenStandardOutput", "System.Console.OpenStandardInput", "System.Console.OpenStandardError",
        "System.GC.AllocateUninitializedArray", "System.GC.AllocateArray", "System.Object.GetType.Assembly",
    };

    public static List<CodeDiagnostic> Analyze(SyntaxTree tree, SemanticModel model)
    {
        var result = new List<CodeDiagnostic>();
        var root = tree.GetRoot();
        var seen = new HashSet<string>();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case UnsafeStatementSyntax or PointerTypeSyntax or FunctionPointerTypeSyntax or FixedStatementSyntax:
                    Add(result, seen, node, "Q-SEC-UNSAFE", "Onveilige code (unsafe, pointers) is niet toegelaten in de sandbox.");
                    continue;
                case AttributeSyntax attr:
                    {
                        var name = attr.Name.ToString();
                        if (name.Contains("DllImport") || name.Contains("LibraryImport") || name.Contains("UnmanagedCallersOnly") || name.Contains("ModuleInitializer"))
                            Add(result, seen, node, "Q-SEC-INTEROP", $"Het attribuut [{name}] is niet toegelaten in de sandbox.");
                        continue;
                    }
                case IdentifierNameSyntax when node.Parent is QualifiedNameSyntax q && q.Parent is UsingDirectiveSyntax:
                    continue;
                case IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                    break;
                default:
                    continue;
            }

            if (node.Ancestors().Any(a => a is UsingDirectiveSyntax)) continue;

            ISymbol? symbol;
            try
            {
                var info = model.GetSymbolInfo(node);
                symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            }
            catch { continue; }
            if (symbol == null) continue;
            if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol or IDiscardSymbol) continue;
            if (symbol.Locations.Any(l => l.IsInSource)) continue; // eigen code van de leerling
            if (symbol is INamespaceSymbol) continue;

            var problem = Check(symbol);
            if (problem != null) Add(result, seen, node, "Q-SEC-API", problem);
        }
        return result;
    }

    static string? Check(ISymbol symbol)
    {
        INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) type = reduced.ContainingType; // extension methods
        if (type == null) return null;

        while (type.ContainingType != null && type.ContainingNamespace == null) type = type.ContainingType;
        string typeName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
        int generic = typeName.IndexOf('<');
        if (generic >= 0) typeName = typeName[..generic];
        string memberName = symbol is INamedTypeSymbol ? typeName : typeName + "." + symbol.Name;

        if (DeniedMembers.Contains(memberName))
            return $"'{memberName}' is niet toegelaten in de C# Quest sandbox.";
        if (AllowedMembers.Contains(memberName)) return null;

        if (DeniedTypes.Contains(typeName) && symbol is not INamedTypeSymbol)
            return $"'{memberName}' is niet toegelaten in de C# Quest sandbox.";
        if (DeniedTypes.Contains(typeName) && symbol is INamedTypeSymbol && typeName != "System.Type" && typeName != "System.Threading.Thread")
            return $"Het type '{typeName}' is niet toegelaten in de C# Quest sandbox.";

        string ns = GetNamespace(type);
        if (ns.Length == 0) return null;
        if (AllowedExactNamespaces.Contains(ns)) return null;
        foreach (var prefix in AllowedNamespacePrefixes)
            if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                if (ns.StartsWith("System.IO.Pipes") || ns.StartsWith("System.IO.MemoryMappedFiles") || ns.StartsWith("System.IO.Pipelines"))
                    break;
                return null;
            }
        return $"De namespace '{ns}' ({memberName}) wordt niet gebruikt in deze cursus en is niet toegelaten in de sandbox.";
    }

    static string GetNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        return ns == null || ns.IsGlobalNamespace ? "" : ns.ToDisplayString();
    }

    static void Add(List<CodeDiagnostic> list, HashSet<string> seen, SyntaxNode node, string code, string message)
    {
        var span = node.GetLocation().GetLineSpan();
        string key = code + span.StartLinePosition.Line + message;
        if (!seen.Add(key)) return;
        list.Add(new CodeDiagnostic
        {
            Severity = "error",
            Category = FeedbackCategory.Security,
            Code = code,
            Message = message,
            Explanation = "Je code wordt uitgevoerd in een afgeschermde omgeving (sandbox). Om je computer te beschermen zijn enkel de onderdelen van .NET toegelaten die je in deze cursus nodig hebt.",
            Tip = "Los de opdracht op met de leerstof uit de cursus: Console, Math, strings, arrays, collecties, bestanden (in de map van je programma) en je eigen klassen.",
            File = span.Path,
            Line = span.StartLinePosition.Line + 1,
            Column = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1,
        });
    }
}
