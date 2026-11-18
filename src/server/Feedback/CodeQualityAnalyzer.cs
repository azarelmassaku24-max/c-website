using System.Text.RegularExpressions;
using CSharpQuest.Server.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpQuest.Server.Feedback;

/// <summary>
/// Code review volgens de conventies van de cursus. Elke regel is gekoppeld aan het level waarin
/// het onderwerp aan bod komt, zodat beginners geen feedback krijgen over leerstof die nog moet komen.
/// </summary>
public static class CodeQualityAnalyzer
{
    public static List<CodeDiagnostic> Analyze(IEnumerable<SyntaxTree> trees, Compilation? compilation, int level)
    {
        var result = new List<CodeDiagnostic>();
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            var model = compilation?.GetSemanticModel(tree);
            Naming(root, result, level);
            MultipleDeclarators(root, result);
            InterpolationCalculations(root, result);
            Concatenation(root, result);
            BoolComparisons(root, result);
            ReturnBool(root, result);
            EmptyCatch(root, result, level);
            EmptyBlocks(root, result);
            MagicValues(root, result);
            LoopBounds(root, model, result, level);
            if (level >= 6) { LongMethods(root, result); Duplicates(root, result); CommandQuery(root, model, result); }
            if (level >= 9) Streams(root, model, result);
            if (level >= 10) Fields(root, result);
            if (level >= 12) RemoveDuringForeach(root, result);
            if (root.DescendantNodes().OfType<GotoStatementSyntax>().FirstOrDefault() is { } g)
                Add(result, g, "Q-GOTO", FeedbackCategory.Structure, "Vermijd goto", "goto maakt het verloop van je programma moeilijk te volgen.", "Gebruik een loop, break of een method.");
        }
        // Niet overladen: maximaal 8 opmerkingen, belangrijkste eerst.
        return result.GroupBy(r => r.Code + r.Line).Select(g => g.First())
            .OrderBy(r => r.Category == FeedbackCategory.Logic ? 0 : r.Category == FeedbackCategory.Structure ? 1 : r.Category == FeedbackCategory.Redundant ? 2 : 3)
            .ThenBy(r => r.Line).Take(8).ToList();
    }

    static void Add(List<CodeDiagnostic> list, SyntaxNode node, string code, string category, string message, string explanation, string tip, string severity = "info")
    {
        var span = node.GetLocation().GetLineSpan();
        list.Add(new CodeDiagnostic
        {
            Severity = severity,
            Category = category,
            Code = code,
            Message = message,
            Explanation = explanation,
            Tip = tip,
            File = span.Path,
            Line = span.StartLinePosition.Line + 1,
            Column = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1,
        });
    }

    // ------------------------------------------------------------------ naming
    static void Naming(SyntaxNode root, List<CodeDiagnostic> list, int level)
    {
        foreach (var v in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (v.Parent?.Parent is not LocalDeclarationStatementSyntax decl) continue;
            if (decl.IsConst) continue;
            string name = v.Identifier.ValueText;
            if (name == "_" || name.Length == 0) continue;
            if (char.IsUpper(name[0]))
                Add(list, v, "Q-NAME-CASE", FeedbackCategory.Improvement, $"Variabelennaam '{name}' begint met een hoofdletter",
                    "Volgens de C#-conventies (en de cursus) beginnen namen van variabelen met een kleine letter (camelCasing).", $"Hernoem naar '{char.ToLower(name[0]) + name[1..]}'.");
            else if (name.Contains('_'))
                Add(list, v, "Q-NAME-UNDERSCORE", FeedbackCategory.Improvement, $"Variabelennaam '{name}' bevat een underscore",
                    "In C# gebruiken we camelCasing voor variabelen: elk volgend woord begint met een hoofdletter, zonder underscores.", "Bijvoorbeeld deler_als_tekst wordt delerAlsTekst.");
        }
        if (level >= 6)
            foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                string name = m.Identifier.ValueText;
                if (name.Length > 0 && char.IsLower(name[0]))
                    Add(list, m.Identifier.Parent!, "Q-NAME-METHOD", FeedbackCategory.Improvement, $"Method-naam '{name}' begint met een kleine letter",
                        "Namen van methods (en klassen en properties) beginnen in C# met een hoofdletter (PascalCasing).", $"Hernoem naar '{char.ToUpper(name[0]) + name[1..]}'.");
            }
        foreach (var c in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            string name = c.Identifier.ValueText;
            if (name.Length > 0 && char.IsLower(name[0]))
                Add(list, c, "Q-NAME-CLASS", FeedbackCategory.Improvement, $"Klassenaam '{name}' begint met een kleine letter",
                    "Klassennamen beginnen met een hoofdletter (PascalCasing).", $"Hernoem naar '{char.ToUpper(name[0]) + name[1..]}'.");
        }
    }

    static void MultipleDeclarators(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var d in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            if (d.Declaration.Variables.Count > 1 && !d.IsConst)
                Add(list, d, "Q-MULTI-DECL", FeedbackCategory.Structure, "Meerdere variabelen op één regel gedeclareerd",
                    "De cursus raadt aan om slechts één variabele per regel te introduceren, en dat te doen waar je ze voor het eerst nodig hebt.", "Splits de declaratie op over meerdere regels.");
    }

    static void InterpolationCalculations(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var hole in root.DescendantNodes().OfType<InterpolationSyntax>())
        {
            if (hole.Expression is BinaryExpressionSyntax b && b.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression
                    or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression)
            {
                Add(list, hole, "Q-INTERP-CALC", FeedbackCategory.Improvement, "Berekening binnen string interpolatie",
                    "Tussen de accolades van een interpolatie zet je enkel namen van variabelen, geen berekeningen. Zo hou je het berekenen gescheiden van het tonen van output.",
                    $"Bereken '{b}' eerst in een variabele met een duidelijke naam en zet die variabele tussen de accolades.");
                return;
            }
        }
    }

    static void Concatenation(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma || ma.Name.Identifier.ValueText is not ("WriteLine" or "Write")) continue;
            if (inv.ArgumentList.Arguments.Count != 1) continue;
            var arg = inv.ArgumentList.Arguments[0].Expression;
            if (arg is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression }) continue;
            var parts = Flatten(arg).ToList();
            if (parts.Count >= 3 && parts.Any(p => p is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression })
                && parts.Count(p => p is not LiteralExpressionSyntax) >= 1)
            {
                Add(list, arg, "Q-CONCAT", FeedbackCategory.Improvement, "Gebruik string interpolatie in plaats van +",
                    "De vele \" en + symbolen maken het moeilijk om te zien hoe de tekst eruitziet. String interpolatie ($\"...{variabele}...\") is leesbaarder.",
                    "Schrijf bv. $\"Dag {naam}, je bent {leeftijd} jaar.\"");
                return;
            }
        }

        static IEnumerable<ExpressionSyntax> Flatten(ExpressionSyntax e)
        {
            if (e is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } b)
            {
                foreach (var x in Flatten(b.Left)) yield return x;
                foreach (var x in Flatten(b.Right)) yield return x;
            }
            else yield return e;
        }
    }

    static void BoolComparisons(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var b in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (b.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)) continue;
            if (b.Right is LiteralExpressionSyntax r && r.Kind() is SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression
                || b.Left is LiteralExpressionSyntax l && l.Kind() is SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression)
            {
                Add(list, b, "Q-BOOL-COMPARE", FeedbackCategory.Redundant, "Onnodige vergelijking met true/false",
                    "Een bool is zelf al true of false. 'if (ok == true)' betekent hetzelfde als 'if (ok)'.", "Schrijf 'if (ok)' of 'if (!ok)'.");
                return;
            }
        }
    }

    static void ReturnBool(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var ifs in root.DescendantNodes().OfType<IfStatementSyntax>())
        {
            if (ifs.Else == null) continue;
            if (IsReturnBool(ifs.Statement) is { } a && IsReturnBool(ifs.Else.Statement) is { } b && a != b)
                Add(list, ifs, "Q-RETURN-BOOL", FeedbackCategory.Redundant, "if/else met return true/false kan korter",
                    "De voorwaarde is zelf al een bool. 'if (x > 5) return true; else return false;' is hetzelfde als 'return x > 5;'.",
                    a ? $"Schrijf: return {ifs.Condition};" : $"Schrijf: return !({ifs.Condition});");
        }

        static bool? IsReturnBool(StatementSyntax s)
        {
            if (s is BlockSyntax { Statements.Count: 1 } blk) s = blk.Statements[0];
            if (s is ReturnStatementSyntax { Expression: LiteralExpressionSyntax lit })
                return lit.Kind() switch { SyntaxKind.TrueLiteralExpression => true, SyntaxKind.FalseLiteralExpression => false, _ => null };
            return null;
        }
    }

    static void EmptyCatch(SyntaxNode root, List<CodeDiagnostic> list, int level)
    {
        foreach (var c in root.DescendantNodes().OfType<CatchClauseSyntax>())
            if (c.Block.Statements.Count == 0)
                Add(list, c, "Q-EMPTY-CATCH", FeedbackCategory.Structure, "Lege catch: de fout wordt stil 'ingeslikt'",
                    "Als je een exception opvangt zonder er iets mee te doen, merkt niemand dat er iets misliep. Dat maakt fouten zoeken heel moeilijk.",
                    "Toon minstens een duidelijke melding aan de gebruiker, of vang enkel de exceptions op die je echt kan afhandelen.");
    }

    static void EmptyBlocks(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var b in root.DescendantNodes().OfType<BlockSyntax>())
        {
            if (b.Statements.Count > 0 || b.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia))) continue;
            if (b.Parent is IfStatementSyntax or ElseClauseSyntax or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax)
            {
                Add(list, b.Parent, "Q-EMPTY-BLOCK", FeedbackCategory.Redundant, "Leeg code block",
                    "Dit block bevat geen opdrachten en doet dus niets.", "Verwijder het of vul het aan.");
                return;
            }
        }
    }

    static readonly HashSet<string> TrivialNumbers = new() { "0", "1", "2", "-1", "10", "100", "0.5", "1000", "0.0", "1.0", "2.0" };

    static void MagicValues(SyntaxNode root, List<CodeDiagnostic> list)
    {
        var literals = root.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.NumericLiteralExpression))
            .Where(l => !l.Ancestors().Any(a => a is LocalDeclarationStatementSyntax { IsConst: true } or FieldDeclarationSyntax
                or CaseSwitchLabelSyntax or InitializerExpressionSyntax or AttributeSyntax or ElementAccessExpressionSyntax or ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Next" } } }))
            .Where(l => !TrivialNumbers.Contains(l.Token.Text.TrimEnd('m', 'M', 'd', 'D', 'f', 'F')))
            .GroupBy(l => l.Token.ValueText)
            .Where(g => g.Count() >= 2)
            .FirstOrDefault();
        if (literals != null)
        {
            var first = literals.First();
            Add(list, first, "Q-MAGIC", FeedbackCategory.Improvement, $"Magic value {first.Token.Text} komt {literals.Count()} keer voor",
                "Een getal dat zomaar middenin je code staat, zegt niets over zijn betekenis. Als de waarde ooit verandert, moet je ze op meerdere plaatsen aanpassen.",
                "Declareer bovenaan een const met een duidelijke naam, bv. const double btwPercentage = 21.0;");
        }
    }

    static void LoopBounds(SyntaxNode root, SemanticModel? model, List<CodeDiagnostic> list, int level)
    {
        foreach (var f in root.DescendantNodes().OfType<ForStatementSyntax>())
        {
            if (f.Condition is not BinaryExpressionSyntax cond) continue;
            // i <= x.Length  => klassieke IndexOutOfRange
            if (cond.IsKind(SyntaxKind.LessThanOrEqualExpression) && cond.Right is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count" } ma)
            {
                string idx = cond.Left.ToString();
                bool usesIndex = f.Statement.DescendantNodes().OfType<ElementAccessExpressionSyntax>()
                    .Any(e => e.Expression.ToString() == ma.Expression.ToString() && e.ArgumentList.Arguments.Any(a => a.ToString() == idx));
                if (usesIndex)
                    Add(list, cond, "Q-LOOP-LE-LENGTH", FeedbackCategory.Logic, $"Let op: {cond} gaat één positie te ver",
                        $"De laatste geldige index van {ma.Expression} is {ma.Expression}.{ma.Name} - 1. Met <= probeer je in de laatste iteratie een element te benaderen dat niet bestaat (IndexOutOfRangeException).",
                        $"Gebruik {idx} < {ma}.", "warning");
            }
            // i < 5 terwijl een array overlopen wordt
            if (level >= 4 && cond.IsKind(SyntaxKind.LessThanExpression) && cond.Right is LiteralExpressionSyntax lit && model != null)
            {
                string idx = cond.Left.ToString();
                var access = f.Statement.DescendantNodes().OfType<ElementAccessExpressionSyntax>()
                    .FirstOrDefault(e => e.ArgumentList.Arguments.Count == 1 && e.ArgumentList.Arguments[0].ToString() == idx);
                if (access != null && model.GetTypeInfo(access.Expression).Type is IArrayTypeSymbol)
                    Add(list, lit, "Q-LOOP-LITERAL", FeedbackCategory.Improvement, $"Gebruik {access.Expression}.Length in plaats van {lit}",
                        "Als de grootte van de array ooit verandert, klopt je loop niet meer. Met .Length past de loop zich automatisch aan.",
                        $"Schrijf: {idx} < {access.Expression}.Length");
            }
        }
    }

    static void LongMethods(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (m.Body == null) continue;
            int count = m.Body.DescendantNodes().OfType<StatementSyntax>().Count(s => s is not BlockSyntax);
            if (count > 40)
                Add(list, m.Identifier.Parent!, "Q-LONG-METHOD", FeedbackCategory.Structure, $"De method {m.Identifier.ValueText} is erg lang ({count} opdrachten)",
                    "Lange methods zijn moeilijk te begrijpen en te testen. Door de code op te delen in kleinere methods met een duidelijke naam, wordt je programma leesbaarder.",
                    "Zoek stukken code die samen één taak uitvoeren en maak er een aparte method van.");
        }
    }

    static void Duplicates(SyntaxNode root, List<CodeDiagnostic> list)
    {
        var seen = new Dictionary<string, SyntaxNode>();
        foreach (var block in root.DescendantNodes().OfType<BlockSyntax>())
        {
            var stmts = block.Statements;
            for (int i = 0; i + 3 <= stmts.Count; i++)
            {
                string key = string.Join("\n", stmts.Skip(i).Take(3).Select(s => Regex.Replace(s.ToString(), @"\s+", " ")));
                if (key.Length < 60) continue;
                if (seen.TryGetValue(key, out var other) && !other.Span.OverlapsWith(stmts[i].Span))
                {
                    var otherLine = other.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    Add(list, stmts[i], "Q-DRY", FeedbackCategory.Structure, $"Herhaalde code (ook op regel {otherLine})",
                        "Dezelfde opdrachten staan meermaals in je programma. Volgens het DRY-principe (Don't Repeat Yourself) vermijd je dat: een aanpassing moet je anders op meerdere plaatsen doen.",
                        "Verplaats de herhaalde code naar een method (met parameters voor wat verschilt) en roep die method op.");
                    return;
                }
                seen.TryAdd(key, stmts[i]);
            }
        }
    }

    static void CommandQuery(SyntaxNode root, SemanticModel? model, List<CodeDiagnostic> list)
    {
        foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (m.Body == null || m.Identifier.ValueText == "Main") continue;
            if (m.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }) continue;
            var write = m.Body.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(i =>
                i.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Console" }, Name.Identifier.ValueText: "Write" or "WriteLine" });
            if (write != null)
            {
                Add(list, m.Identifier.Parent!, "Q-QUERY-OUTPUT", FeedbackCategory.Structure, $"De query-method {m.Identifier.ValueText} schrijft ook naar de console",
                    "Een method die een waarde teruggeeft (query) berekent best enkel die waarde. Output tonen is een aparte verantwoordelijkheid (command). Zo kan je de berekening hergebruiken, bv. ook in een webpagina of een test.",
                    "Laat de method enkel de waarde teruggeven en toon het resultaat in de code die de method oproept.");
                return;
            }
        }
    }

    static void Streams(SyntaxNode root, SemanticModel? model, List<CodeDiagnostic> list)
    {
        foreach (var o in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            string type = o.Type.ToString();
            if (type is not ("StreamReader" or "StreamWriter" or "System.IO.StreamReader" or "System.IO.StreamWriter")) continue;
            bool inUsing = o.Ancestors().Any(a => a is UsingStatementSyntax || a is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword });
            bool closed = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Close" or "Dispose" });
            if (!inUsing)
                Add(list, o, "Q-STREAM-USING", closed ? FeedbackCategory.Improvement : FeedbackCategory.Structure,
                    closed ? $"Gebruik een using-statement voor de {type}" : $"De {type} wordt niet (gegarandeerd) afgesloten",
                    "Een stream houdt het bestand open tot hij afgesloten wordt. Als er een exception optreedt vóór Close(), blijft het bestand open. Met using wordt de stream altijd netjes afgesloten.",
                    $"Schrijf: using (var stream = new {type}(...)) {{ ... }} of using var stream = new {type}(...);");
        }
    }

    static void Fields(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var f in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (f.Parent is not ClassDeclarationSyntax) continue;
            if (f.Modifiers.Any(SyntaxKind.ConstKeyword) || f.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
            if (f.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                Add(list, f, "Q-PUBLIC-FIELD", FeedbackCategory.Structure, "Public dataveld",
                    "Datavelden horen private te zijn: zo bepaalt het object zelf hoe zijn toestand verandert (encapsulatie). Andere code gebruikt properties of methods.",
                    "Maak het veld private (bv. _naam) en voorzie een property, of gebruik meteen een auto-implemented property { get; set; }.");
                continue;
            }
            foreach (var v in f.Declaration.Variables)
                if (!v.Identifier.ValueText.StartsWith('_'))
                {
                    Add(list, v, "Q-FIELD-NAME", FeedbackCategory.Improvement, $"Dataveld '{v.Identifier.ValueText}' zonder underscore",
                        "In de cursus beginnen namen van (private) datavelden met een underscore, zodat je ze in je code meteen onderscheidt van lokale variabelen en parameters.",
                        $"Hernoem naar '_{v.Identifier.ValueText}'.");
                    return;
                }
        }
    }

    static void RemoveDuringForeach(SyntaxNode root, List<CodeDiagnostic> list)
    {
        foreach (var fe in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            string coll = fe.Expression.ToString();
            var remove = fe.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(i =>
                i.Expression is MemberAccessExpressionSyntax ma && ma.Expression.ToString() == coll
                && ma.Name.Identifier.ValueText is "Remove" or "RemoveAt" or "Add" or "Clear" or "Insert");
            if (remove != null)
                Add(list, remove, "Q-MODIFY-FOREACH", FeedbackCategory.Logic, $"De collectie {coll} wordt gewijzigd tijdens foreach",
                    "Een collectie wijzigen terwijl je ze met foreach overloopt, veroorzaakt een InvalidOperationException.",
                    "Gebruik een for-loop die achteraan begint (i = Count - 1; i >= 0; i--) of verzamel eerst wat weg moet.", "warning");
        }
    }
}
