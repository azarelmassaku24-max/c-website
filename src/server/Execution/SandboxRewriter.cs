using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CSharpQuest.Server.Execution;

/// <summary>
/// Herschrijft de code van de leerling (met behoud van regelnummers) zodat Console, Thread.Sleep,
/// Random en Environment via de C# Quest runtime lopen en bestandspaden bewaakt worden.
/// </summary>
public sealed class SandboxRewriter : CSharpSyntaxRewriter
{
    static readonly HashSet<string> ConsoleMembers = new()
    {
        "Write", "WriteLine", "ReadLine", "Read", "ReadKey", "KeyAvailable", "Clear", "SetCursorPosition",
        "GetCursorPosition", "CursorLeft", "CursorTop", "CursorVisible", "CursorSize", "ForegroundColor",
        "BackgroundColor", "ResetColor", "Beep", "Title", "WindowWidth", "WindowHeight", "BufferWidth",
        "BufferHeight", "LargestWindowWidth", "LargestWindowHeight", "WindowLeft", "WindowTop", "SetWindowSize",
        "SetBufferSize", "TreatControlCAsInput", "IsOutputRedirected", "IsInputRedirected", "IsErrorRedirected",
        "OutputEncoding", "InputEncoding", "Out", "Error", "In",
    };

    static readonly HashSet<string> EnvironmentMembers = new()
    {
        "GetFolderPath", "GetEnvironmentVariable", "GetEnvironmentVariables", "SetEnvironmentVariable", "UserName",
        "MachineName", "UserDomainName", "CurrentDirectory", "GetCommandLineArgs", "CommandLine", "GetLogicalDrives",
    };

    static readonly HashSet<string> GuardedParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "path", "sourceFileName", "destFileName", "destinationFileName", "destinationBackupFileName",
        "sourceDirName", "destDirName", "fileName", "linkPath", "archiveFileName", "destinationArchiveFileName",
        "sourceDirectoryName", "destinationDirectoryName",
    };

    readonly SemanticModel _model;
    readonly INamedTypeSymbol? _console, _environment, _random, _thread;

    public SandboxRewriter(SemanticModel model)
    {
        _model = model;
        var c = model.Compilation;
        _console = c.GetTypeByMetadataName("System.Console");
        _environment = c.GetTypeByMetadataName("System.Environment");
        _random = c.GetTypeByMetadataName("System.Random");
        _thread = c.GetTypeByMetadataName("System.Threading.Thread");
    }

    ISymbol? SymbolOf(SyntaxNode node)
    {
        try
        {
            var info = _model.GetSymbolInfo(node);
            return info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        }
        catch { return null; }
    }

    string? RuntimeTypeFor(ISymbol symbol)
    {
        if (!symbol.IsStatic) return null;
        var type = symbol.ContainingType?.OriginalDefinition;
        if (type == null) return null;
        if (SymbolEqualityComparer.Default.Equals(type, _console) && ConsoleMembers.Contains(symbol.Name)) return "QConsole";
        if (SymbolEqualityComparer.Default.Equals(type, _environment) && EnvironmentMembers.Contains(symbol.Name)) return "QEnvironment";
        if (SymbolEqualityComparer.Default.Equals(type, _thread) && symbol.Name == "Sleep") return "QThread";
        if (SymbolEqualityComparer.Default.Equals(type, _random) && symbol.Name == "Shared") return "QRandom";
        return null;
    }

    static ExpressionSyntax RuntimeType(string name) =>
        ParseExpression("global::QuestRuntime." + name);

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var symbol = SymbolOf(node);
        var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
        if (symbol != null && node.Kind() == SyntaxKind.SimpleMemberAccessExpression)
        {
            var runtime = RuntimeTypeFor(symbol);
            if (runtime != null)
                return visited.WithExpression(RuntimeType(runtime).WithTriviaFrom(visited.Expression));
        }
        return visited;
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        // using static System.Console;  ->  WriteLine("x")
        if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name == node) return base.VisitIdentifierName(node);
        if (node.Parent is QualifiedNameSyntax || node.Ancestors().Any(a => a is UsingDirectiveSyntax)) return base.VisitIdentifierName(node);
        if (node.Parent is MemberBindingExpressionSyntax || node.Parent is NameColonSyntax) return base.VisitIdentifierName(node);
        var symbol = SymbolOf(node);
        if (symbol is IMethodSymbol or IPropertySymbol)
        {
            var runtime = RuntimeTypeFor(symbol);
            if (runtime != null)
                return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, RuntimeType(runtime), IdentifierName(node.Identifier.WithoutTrivia()))
                    .WithTriviaFrom(node);
        }
        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var ctor = SymbolOf(node) as IMethodSymbol;
        var visited = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
        if (ctor != null && SymbolEqualityComparer.Default.Equals(ctor.ContainingType?.OriginalDefinition, _random) && visited.Initializer == null)
        {
            return InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, RuntimeType("QRandom"), IdentifierName("Create")),
                    visited.ArgumentList ?? ArgumentList())
                .WithTriviaFrom(visited);
        }
        if (ctor != null && visited.ArgumentList != null)
            return visited.WithArgumentList(GuardArguments(ctor, node.ArgumentList!, visited.ArgumentList));
        return visited;
    }

    public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        var ctor = SymbolOf(node) as IMethodSymbol;
        var visited = (ImplicitObjectCreationExpressionSyntax)base.VisitImplicitObjectCreationExpression(node)!;
        if (ctor != null && SymbolEqualityComparer.Default.Equals(ctor.ContainingType?.OriginalDefinition, _random) && visited.Initializer == null)
        {
            return InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, RuntimeType("QRandom"), IdentifierName("Create")),
                    visited.ArgumentList)
                .WithTriviaFrom(visited);
        }
        if (ctor != null)
            return visited.WithArgumentList(GuardArguments(ctor, node.ArgumentList, visited.ArgumentList));
        return visited;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var method = SymbolOf(node) as IMethodSymbol;
        var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        if (method != null)
            return visited.WithArgumentList(GuardArguments(method, node.ArgumentList, visited.ArgumentList));
        return visited;
    }

    /// <summary>Wikkelt string-padargumenten van System.IO-API's in QPath.Guard(...).</summary>
    ArgumentListSyntax GuardArguments(IMethodSymbol method, ArgumentListSyntax original, ArgumentListSyntax visited)
    {
        var type = method.ContainingType;
        if (type == null) return visited;
        string ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        if (!ns.StartsWith("System.IO", StringComparison.Ordinal) || type.Name == "Path") return visited;

        var args = visited.Arguments;
        for (int i = 0; i < original.Arguments.Count && i < args.Count; i++)
        {
            var arg = original.Arguments[i];
            IParameterSymbol? parameter = null;
            if (arg.NameColon != null)
                parameter = method.Parameters.FirstOrDefault(p => p.Name == arg.NameColon.Name.Identifier.ValueText);
            else if (i < method.Parameters.Length)
                parameter = method.Parameters[i];
            if (parameter == null || parameter.Type.SpecialType != SpecialType.System_String) continue;
            if (!GuardedParameterNames.Contains(parameter.Name)) continue;

            var current = args[i];
            var guarded = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, RuntimeType("QPath"), IdentifierName("Guard")),
                ArgumentList(SingletonSeparatedList(Argument(current.Expression.WithoutTrivia()))));
            args = args.Replace(current, current.WithExpression(guarded.WithTriviaFrom(current.Expression)));
        }
        return visited.WithArguments(args);
    }
}

/// <summary>
/// Maakt private leden 'internal' zodat automatische tests methods van de leerling kunnen oproepen.
/// Wordt enkel gebruikt bij unit-tests, nooit bij gewone uitvoering.
/// </summary>
public sealed class TestAccessRewriter : CSharpSyntaxRewriter
{
    static SyntaxTokenList Open(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.InternalKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword))
            return modifiers;
        int idx = modifiers.IndexOf(SyntaxKind.PrivateKeyword);
        if (idx >= 0)
        {
            var old = modifiers[idx];
            return modifiers.Replace(old, Token(old.LeadingTrivia, SyntaxKind.InternalKeyword, old.TrailingTrivia));
        }
        return modifiers.Insert(0, Token(SyntaxKind.InternalKeyword).WithTrailingTrivia(Space));
    }

    static bool IsMemberOfClass(SyntaxNode node) => node.Parent is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax;

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var v = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        if (!IsMemberOfClass(node) || node.ExplicitInterfaceSpecifier != null || node.Modifiers.Any(SyntaxKind.OverrideKeyword)) return v;
        return WithModifiersKeepingTrivia(v, Open(v.Modifiers));
    }

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var v = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node)!;
        if (!IsMemberOfClass(node)) return v;
        return WithModifiersKeepingTrivia(v, Open(v.Modifiers));
    }

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var v = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;
        if (!IsMemberOfClass(node) || node.ExplicitInterfaceSpecifier != null || node.Modifiers.Any(SyntaxKind.OverrideKeyword)) return v;
        return WithModifiersKeepingTrivia(v, Open(v.Modifiers));
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var v = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
        if (node.Modifiers.Any(SyntaxKind.StaticKeyword)) return v;
        return WithModifiersKeepingTrivia(v, Open(v.Modifiers));
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var v = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
        if (node.Parent is TypeDeclarationSyntax) return WithModifiersKeepingTrivia(v, Open(v.Modifiers));
        return v;
    }

    public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var v = (EnumDeclarationSyntax)base.VisitEnumDeclaration(node)!;
        if (node.Parent is TypeDeclarationSyntax) return WithModifiersKeepingTrivia(v, Open(v.Modifiers));
        return v;
    }

    static T WithModifiersKeepingTrivia<T>(T node, SyntaxTokenList modifiers) where T : MemberDeclarationSyntax
    {
        if (node.Modifiers.Count == 0 && modifiers.Count > 0)
        {
            // De leading trivia (inspringing) van het eerste token behouden zodat regelnummers gelijk blijven.
            var first = node.GetFirstToken();
            var lead = first.LeadingTrivia;
            var updated = (T)node.ReplaceToken(first, first.WithLeadingTrivia(SyntaxTriviaList.Empty));
            modifiers = modifiers.Replace(modifiers[0], modifiers[0].WithLeadingTrivia(lead));
            return (T)updated.WithModifiers(modifiers);
        }
        return (T)node.WithModifiers(modifiers);
    }
}
