using System.Text.RegularExpressions;
using CSharpQuest.Server.Curriculum;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpQuest.Server.Grading;

public sealed class RuleResult
{
    public string Type { get; init; } = "";
    public bool Passed { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>Controleert structurele opdrachtvereisten met de syntaxboom (bv. "gebruik een for-loop").</summary>
public static class RuleChecker
{
    public static List<RuleResult> Check(IEnumerable<ExerciseRule> rules, IReadOnlyList<SyntaxTree> trees)
    {
        var roots = trees.Select(t => t.GetRoot()).ToList();
        var nodes = roots.SelectMany(r => r.DescendantNodes()).ToList();
        string code = string.Join("\n", roots.Select(r => StripComments(r)));
        var results = new List<RuleResult>();
        foreach (var rule in rules)
        {
            bool ok = Evaluate(rule, nodes, code);
            results.Add(new RuleResult { Type = rule.Type, Passed = ok, Message = rule.Message.Length > 0 ? rule.Message : Describe(rule) });
        }
        return results;
    }

    static string StripComments(SyntaxNode root)
    {
        var comments = root.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
        return root.ReplaceTrivia(comments, (_, _) => SyntaxFactory.Space).ToFullString();
    }

    static bool Evaluate(ExerciseRule rule, List<SyntaxNode> nodes, string code)
    {
        string target = rule.Target;
        int count = rule.Count ?? 1;
        switch (rule.Type)
        {
            case "uses":
                return CountUses(nodes, target) >= count;
            case "forbids":
                return CountUses(nodes, target) == 0;
            case "loop":
                {
                    int n = target switch
                    {
                        "for" => nodes.OfType<ForStatementSyntax>().Count(),
                        "foreach" => nodes.OfType<ForEachStatementSyntax>().Count(),
                        "while" => nodes.OfType<WhileStatementSyntax>().Count(),
                        "do" => nodes.OfType<DoStatementSyntax>().Count(),
                        _ => nodes.Count(n => n is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax),
                    };
                    return n >= count;
                }
            case "no-loop":
                return target switch
                {
                    "for" => !nodes.OfType<ForStatementSyntax>().Any(),
                    "foreach" => !nodes.OfType<ForEachStatementSyntax>().Any(),
                    "while" => !nodes.OfType<WhileStatementSyntax>().Any(),
                    "do" => !nodes.OfType<DoStatementSyntax>().Any(),
                    _ => !nodes.Any(n => n is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax),
                };
            case "statement":
                return target switch
                {
                    "if" => nodes.OfType<IfStatementSyntax>().Count() >= count,
                    "else-if" => nodes.OfType<IfStatementSyntax>().Count(i => i.Parent is ElseClauseSyntax) >= count,
                    "switch" => nodes.OfType<SwitchStatementSyntax>().Count() >= count,
                    "try" => nodes.OfType<TryStatementSyntax>().Count() >= count,
                    "catch" => nodes.OfType<CatchClauseSyntax>().Count() >= count,
                    "finally" => nodes.OfType<FinallyClauseSyntax>().Count() >= count,
                    "using" => nodes.Count(n => n is UsingStatementSyntax || n is LocalDeclarationStatementSyntax l && l.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) >= count,
                    "break" => nodes.OfType<BreakStatementSyntax>().Count() >= count,
                    "return" => nodes.OfType<ReturnStatementSyntax>().Count() >= count,
                    "interpolation" => nodes.OfType<InterpolatedStringExpressionSyntax>().Count() >= count,
                    "const" => nodes.OfType<LocalDeclarationStatementSyntax>().Count(l => l.IsConst) + nodes.OfType<FieldDeclarationSyntax>().Count(f => f.Modifiers.Any(SyntaxKind.ConstKeyword)) >= count,
                    "array" => nodes.OfType<ArrayTypeSyntax>().Count() >= count,
                    "enum" => nodes.OfType<EnumDeclarationSyntax>().Count() >= count,
                    "this" => nodes.OfType<ThisExpressionSyntax>().Count() >= count,
                    "out" => nodes.OfType<ArgumentSyntax>().Count(a => a.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) >= count,
                    "logical-and" => nodes.Count(n => n.IsKind(SyntaxKind.LogicalAndExpression)) >= count,
                    "logical-or" => nodes.Count(n => n.IsKind(SyntaxKind.LogicalOrExpression)) >= count,
                    _ => false,
                };
            case "no-statement":
                return target switch
                {
                    "if" => !nodes.OfType<IfStatementSyntax>().Any(),
                    "logical" => !nodes.Any(n => n.IsKind(SyntaxKind.LogicalAndExpression) || n.IsKind(SyntaxKind.LogicalOrExpression)),
                    "break" => !nodes.OfType<BreakStatementSyntax>().Any(),
                    _ => true,
                };
            case "method":
                {
                    var methods = nodes.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == target).ToList();
                    if (methods.Count == 0) return false;
                    return methods.Any(m =>
                        (rule.Returns == null || Regex.Replace(m.ReturnType.ToString(), @"\s", "") == Regex.Replace(rule.Returns, @"\s", ""))
                        && (rule.Params == null || m.ParameterList.Parameters.Count == rule.Params));
                }
            case "methods":
                return nodes.OfType<MethodDeclarationSyntax>().Count(m => m.Identifier.ValueText != "Main") >= count;
            case "calls":
                return nodes.OfType<InvocationExpressionSyntax>().Count(i => InvokedName(i) == target) >= count;
            case "class":
                return nodes.OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.ValueText == target);
            case "interface":
                return nodes.OfType<InterfaceDeclarationSyntax>().Any(c => c.Identifier.ValueText == target);
            case "implements":
                {
                    var parts = target.Split(':');
                    if (parts.Length != 2) return false;
                    return nodes.OfType<TypeDeclarationSyntax>().Any(c => c.Identifier.ValueText == parts[0]
                        && c.BaseList != null && c.BaseList.Types.Any(b => Regex.Replace(b.Type.ToString(), @"\s", "") == parts[1]));
                }
            case "property":
                {
                    var parts = target.Split('.');
                    return nodes.OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.ValueText == parts[^1]
                        && (parts.Length == 1 || p.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText == parts[0]));
                }
            case "readonly-property":
                {
                    var parts = target.Split('.');
                    return nodes.OfType<PropertyDeclarationSyntax>().Any(p => p.Identifier.ValueText == parts[^1]
                        && (p.ExpressionBody != null || p.AccessorList != null && !p.AccessorList.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) && !a.Modifiers.Any(SyntaxKind.PrivateKeyword))));
                }
            case "constructor":
                return nodes.OfType<ConstructorDeclarationSyntax>().Count(c => c.Identifier.ValueText == target && (rule.Params == null || c.ParameterList.Parameters.Count == rule.Params)) >= count;
            case "constructor-chain":
                return nodes.OfType<ConstructorDeclarationSyntax>().Any(c => c.Identifier.ValueText == target && c.Initializer is { RawKind: (int)SyntaxKind.ThisConstructorInitializer });
            case "private-fields":
                {
                    var fields = nodes.OfType<FieldDeclarationSyntax>().Where(f => !f.Modifiers.Any(SyntaxKind.ConstKeyword) && !f.Modifiers.Any(SyntaxKind.StaticKeyword)).ToList();
                    return fields.All(f => !f.Modifiers.Any(SyntaxKind.PublicKeyword));
                }
            case "new":
                return nodes.Count(n => n is ObjectCreationExpressionSyntax o && TypeBase(o.Type) == target
                    || n is ImplicitObjectCreationExpressionSyntax) >= count;
            case "type":
                return nodes.Count(n => n is TypeSyntax t && TypeBase(t) == target) >= count;
            case "no-literal":
                return !nodes.OfType<LiteralExpressionSyntax>().Any(l => l.Token.Text == target
                    && !l.Ancestors().Any(a => a is LocalDeclarationStatementSyntax { IsConst: true } or FieldDeclarationSyntax));
            case "max-main-statements":
                {
                    var main = nodes.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.ValueText == "Main");
                    if (main?.Body == null) return true;
                    return main.Body.DescendantNodes().OfType<StatementSyntax>().Count(s => s is not BlockSyntax) <= count;
                }
            case "files":
                return nodes.Select(n => n.SyntaxTree.FilePath).Distinct().Count() >= count;
            case "namespace":
                return nodes.OfType<BaseNamespaceDeclarationSyntax>().Any(n => n.Name.ToString() == target || n.Name.ToString().StartsWith(target + "."));
            case "regex":
                return Regex.IsMatch(code, target);
            case "no-regex":
                return !Regex.IsMatch(code, target);
            default:
                return true;
        }
    }

    static string TypeBase(TypeSyntax t) => t switch
    {
        GenericNameSyntax g => g.Identifier.ValueText,
        QualifiedNameSyntax q => TypeBase(q.Right),
        ArrayTypeSyntax a => TypeBase(a.ElementType) + "[]",
        _ => t.ToString(),
    };

    static string InvokedName(InvocationExpressionSyntax i) => i.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => i.Expression.ToString(),
    };

    /// <summary>Telt het gebruik van bv. "Math.Min", "TryParse", "Split" of "string.Join".</summary>
    static int CountUses(List<SyntaxNode> nodes, string target)
    {
        var parts = target.Split('.');
        string member = parts[^1];
        string? owner = parts.Length > 1 ? string.Join(".", parts[..^1]) : null;
        int n = 0;
        foreach (var node in nodes)
        {
            if (node is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == member)
            {
                if (owner == null || ma.Expression.ToString().EndsWith(owner, StringComparison.OrdinalIgnoreCase)
                    || (owner is "string" && ma.Expression.ToString() is "String" or "string"))
                    n++;
            }
            else if (owner == null && node is IdentifierNameSyntax id && id.Identifier.ValueText == member && node.Parent is not MemberAccessExpressionSyntax
                     && node.Parent is InvocationExpressionSyntax)
                n++;
        }
        return n;
    }

    static string Describe(ExerciseRule r) => r.Type switch
    {
        "uses" => $"Gebruik {r.Target}",
        "forbids" => $"Gebruik geen {r.Target}",
        "loop" => $"Gebruik een {r.Target}-loop",
        "method" => $"Definieer een method {r.Target}",
        "class" => $"Definieer een klasse {r.Target}",
        _ => $"{r.Type} {r.Target}",
    };
}
