using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CSharpQuest.Server.Execution;

/// <summary>
/// Voegt meetpunten toe aan de code van de leerling zodat we een echte traceertabel en call stack
/// kunnen tonen. Alle extra code komt op dezelfde regel als de originele code, zodat regelnummers
/// (in de editor én in stack traces) ongewijzigd blijven.
/// </summary>
public sealed class TraceInstrumenter : CSharpSyntaxRewriter
{
    readonly SemanticModel _model;
    readonly HashSet<string> _skip;
    readonly Dictionary<string, TraceVariableInfo> _vars = new();
    readonly Stack<IMethodSymbol?> _methods = new();
    bool _topLevelEntered;

    public TraceInstrumenter(SemanticModel model, HashSet<string> skip)
    {
        _model = model;
        _skip = skip;
    }

    public IEnumerable<TraceVariableInfo> Variables => _vars.Values;

    static ExpressionSyntax Trace(string member) => ParseExpression("global::QuestRuntime.QTrace." + member);

    static int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    static int LineOf(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    static int EndLineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

    static StatementSyntax Call(string member, IEnumerable<ArgumentSyntax> args) =>
        ExpressionStatement(InvocationExpression(Trace(member), ArgumentList(SeparatedList(args))));

    static ArgumentSyntax Lit(int value) => Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(value)));
    static ArgumentSyntax Lit(string value) => Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value)));

    // --------------------------------------------------------------- variables
    bool Capturable(ISymbol s)
    {
        ITypeSymbol? type = s switch
        {
            ILocalSymbol l when !l.IsConst && !l.IsRef => l.Type,
            IParameterSymbol p when p.RefKind != RefKind.Out && !p.IsThis => p.Type,
            _ => null,
        };
        if (type == null) return false;
        if (type.IsRefLikeType || type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer) return false;
        if (s is IParameterSymbol { RefKind: RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter }) return true;
        return true;
    }

    IEnumerable<ArgumentSyntax> Pairs(int line, IEnumerable<ISymbol> symbols)
    {
        foreach (var s in symbols.Distinct(SymbolEqualityComparer.Default).OrderBy(s => s.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0))
        {
            if (!Capturable(s)) continue;
            if (_skip.Contains(line + ":" + s.Name)) continue;
            Register(s);
            yield return Lit(s.Name);
            yield return Argument(IdentifierName(EscapeIdentifier(s.Name)));
        }
    }

    static string EscapeIdentifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    void Register(ISymbol s)
    {
        var decl = s.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (decl == null) return;
        string method = MethodName(decl);
        int declLine = LineOf(decl);
        string key = method + "|" + s.Name + "|" + declLine;
        if (_vars.ContainsKey(key)) return;
        int end;
        if (s is IParameterSymbol)
            end = EndLineOf(decl.Ancestors().FirstOrDefault(a => a is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax or CompilationUnitSyntax) ?? decl);
        else
        {
            var scope = decl.Ancestors().FirstOrDefault(a => a is BlockSyntax or ForStatementSyntax or CommonForEachStatementSyntax
                or SwitchSectionSyntax or UsingStatementSyntax or CatchClauseSyntax or CompilationUnitSyntax);
            end = scope == null ? declLine : EndLineOf(scope);
        }
        _vars[key] = new TraceVariableInfo
        {
            Name = s.Name,
            Method = method,
            Kind = s is IParameterSymbol ? "param" : "local",
            DeclLine = declLine,
            ScopeEndLine = end,
            File = decl.SyntaxTree.FilePath,
        };
    }

    static string MethodName(SyntaxNode node)
    {
        foreach (var a in node.AncestorsAndSelf())
        {
            switch (a)
            {
                case LocalFunctionStatementSyntax lf: return MethodName(lf.Parent!) + " › " + lf.Identifier.ValueText;
                case MethodDeclarationSyntax m: return TypeName(m) + "." + m.Identifier.ValueText;
                case ConstructorDeclarationSyntax c: return "new " + c.Identifier.ValueText;
                case AccessorDeclarationSyntax acc when acc.Parent?.Parent is PropertyDeclarationSyntax p:
                    return TypeName(p) + "." + p.Identifier.ValueText + " (" + acc.Keyword.ValueText + ")";
                case OperatorDeclarationSyntax op: return TypeName(op) + ".operator " + op.OperatorToken.ValueText;
                case CompilationUnitSyntax: return "Main";
            }
        }
        return "?";
    }

    static string TypeName(SyntaxNode member) =>
        member.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "";

    IEnumerable<ISymbol> FlowSymbols(SyntaxNode region, bool includeReadReferences, bool includeAllRead = false)
    {
        DataFlowAnalysis? flow;
        try
        {
            flow = region switch
            {
                ExpressionSyntax e => _model.AnalyzeDataFlow(e),
                StatementSyntax s => _model.AnalyzeDataFlow(s),
                _ => null,
            };
        }
        catch { yield break; }
        if (flow == null || !flow.Succeeded) yield break;
        var declaredInside = new HashSet<ISymbol>(flow.VariablesDeclared, SymbolEqualityComparer.Default);
        foreach (var s in flow.WrittenInside)
            if (InScopeAfter(s, region, declaredInside)) yield return s;
        foreach (var s in flow.ReadInside)
        {
            if (!InScopeAfter(s, region, declaredInside)) continue;
            if (includeAllRead) { yield return s; continue; }
            if (!includeReadReferences) continue;
            var t = (s as ILocalSymbol)?.Type ?? (s as IParameterSymbol)?.Type;
            if (t != null && t.IsReferenceType && t.SpecialType != SpecialType.System_String) yield return s;
        }
    }

    static bool InScopeAfter(ISymbol s, SyntaxNode region, HashSet<ISymbol> declaredInside)
    {
        if (s is not (ILocalSymbol or IParameterSymbol)) return false;
        if (s is IParameterSymbol { IsThis: true }) return false;
        if (declaredInside.Contains(s))
        {
            // Variabelen gedeclareerd in een lambda binnen de regel zijn erna niet meer bereikbaar.
            var decl = s.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (decl == null) return false;
            if (decl.Ancestors().TakeWhile(a => a != region).Any(a => a is AnonymousFunctionExpressionSyntax)) return false;
        }
        else
        {
            var decl = s.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (decl != null && decl.Ancestors().Any(a => a is AnonymousFunctionExpressionSyntax) && !region.Ancestors().Any(a => a is AnonymousFunctionExpressionSyntax))
                return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- statements
    List<StatementSyntax> InstrumentStatement(StatementSyntax original)
    {
        var list = new List<StatementSyntax>();
        int line = LineOf(original);
        switch (original)
        {
            case BlockSyntax b:
                list.Add(InstrumentBlock(b));
                return list;
            case EmptyStatementSyntax:
                list.Add(original);
                return list;
            case LocalFunctionStatementSyntax lf:
                list.Add((StatementSyntax)VisitLocalFunctionStatement(lf)!);
                return list;
            case LocalDeclarationStatementSyntax decl:
                {
                    var visited = (StatementSyntax)Visit(decl)!;
                    var symbols = decl.Declaration.Variables
                        .Where(v => v.Initializer != null)
                        .Select(v => _model.GetDeclaredSymbol(v))
                        .Where(s => s != null)
                        .Cast<ISymbol>()
                        .Concat(FlowSymbols(decl, includeReadReferences: true).Where(s => decl.Declaration.Variables.All(v => v.Identifier.ValueText != s.Name) && IsWrittenOrRef(s, decl)));
                    var pairs = decl.IsConst ? new List<ArgumentSyntax>() : Pairs(line, symbols).ToList();
                    list.Add(Lead(Call("L", new[] { Lit(line) }), visited));
                    list.Add(StripLead(visited));
                    if (pairs.Count > 0) list.Add(Call("V", new[] { Lit(line) }.Concat(pairs)));
                    return list;
                }
            case ExpressionStatementSyntax expr:
                {
                    var visited = (StatementSyntax)Visit(expr)!;
                    var pairs = Pairs(line, FlowSymbols(expr, includeReadReferences: true)).ToList();
                    list.Add(Lead(Call("L", new[] { Lit(line) }), visited));
                    list.Add(StripLead(visited));
                    if (pairs.Count > 0) list.Add(Call("V", new[] { Lit(line) }.Concat(pairs)));
                    return list;
                }
            case ReturnStatementSyntax ret:
                {
                    var visited = (ReturnStatementSyntax)Visit(ret)!;
                    list.Add(Lead(Call("L", new[] { Lit(line) }), visited));
                    var method = _methods.Count > 0 ? _methods.Peek() : null;
                    if (visited.Expression != null && method != null && !method.ReturnsByRef && !method.ReturnsByRefReadonly && !method.ReturnsVoid
                        && method.ReturnType.TypeKind != TypeKind.Error && !method.ReturnType.IsRefLikeType && !method.IsAsync)
                    {
                        var typeSyntax = ParseTypeName(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        var wrapped = InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                ParseExpression("global::QuestRuntime.QTrace"),
                                GenericName(Identifier("Ret"), TypeArgumentList(SingletonSeparatedList(typeSyntax)))),
                            ArgumentList(SeparatedList(new[] { Lit(line), Argument(visited.Expression.WithoutTrivia()) })));
                        visited = visited.WithExpression(wrapped.WithLeadingTrivia(Space));
                    }
                    list.Add(StripLead(visited));
                    return list;
                }
            case IfStatementSyntax ifs:
                list.Add((StatementSyntax)VisitIfStatement(ifs)!);
                return list;
            case WhileStatementSyntax ws:
                list.Add((StatementSyntax)VisitWhileStatement(ws)!);
                return list;
            case DoStatementSyntax ds:
                list.Add((StatementSyntax)VisitDoStatement(ds)!);
                return list;
            case ForStatementSyntax fs:
                list.Add((StatementSyntax)VisitForStatement(fs)!);
                return list;
            case ForEachStatementSyntax fe:
                list.Add((StatementSyntax)VisitForEachStatement(fe)!);
                return list;
            case TryStatementSyntax ts:
                list.Add((StatementSyntax)Visit(ts)!);
                return list;
            case LabeledStatementSyntax ls:
                list.Add(ls.WithStatement(Embedded(ls.Statement)));
                return list;
            default:
                {
                    // break, continue, throw, switch, using, lock, goto, ...
                    var visited = (StatementSyntax)Visit(original)!;
                    list.Add(Lead(Call("L", new[] { Lit(line) }), visited));
                    list.Add(StripLead(visited));
                    return list;
                }
        }
    }

    bool IsWrittenOrRef(ISymbol s, SyntaxNode region) => true;

    static StatementSyntax Lead(StatementSyntax inserted, StatementSyntax original) =>
        inserted.WithLeadingTrivia(original.GetLeadingTrivia());

    static StatementSyntax StripLead(StatementSyntax s) => s.WithLeadingTrivia(SyntaxTriviaList.Empty);

    BlockSyntax InstrumentBlock(BlockSyntax block)
    {
        var statements = new List<StatementSyntax>();
        foreach (var s in block.Statements) statements.AddRange(InstrumentStatement(s));
        return block.WithStatements(List(statements));
    }

    /// <summary>Een losse statement (bv. de body van een if zonder accolades) wordt een block.</summary>
    StatementSyntax Embedded(StatementSyntax original)
    {
        if (original is BlockSyntax b) return InstrumentBlock(b);
        if (original is IfStatementSyntax ifs) return (StatementSyntax)VisitIfStatement(ifs)!;
        var list = InstrumentStatement(original);
        if (list.Count == 1) return list[0];
        var lead = list[0].GetLeadingTrivia();
        list[0] = list[0].WithLeadingTrivia(SyntaxTriviaList.Empty);
        return Block(Token(lead, SyntaxKind.OpenBraceToken, SyntaxTriviaList.Empty), List(list), Token(SyntaxKind.CloseBraceToken));
    }

    ExpressionSyntax WrapCondition(int line, ExpressionSyntax original, ExpressionSyntax visited, IEnumerable<ISymbol> extra)
    {
        bool hasPatternVariables = original.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>()
            .Any(d => d.Parent is DeclarationPatternSyntax or VarPatternSyntax or RecursivePatternSyntax or ParenthesizedVariableDesignationSyntax);
        if (hasPatternVariables) return visited;
        var symbols = FlowSymbols(original, includeReadReferences: true, includeAllRead: true).Concat(extra);
        var args = new List<ArgumentSyntax> { Lit(line), Argument(ParenthesizedExpression(visited.WithoutTrivia())) };
        args.AddRange(Pairs(line, symbols));
        return InvocationExpression(Trace("C"), ArgumentList(SeparatedList(args))).WithTriviaFrom(visited);
    }

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        int line = LineOf(node);
        var cond = WrapCondition(line, node.Condition, (ExpressionSyntax)Visit(node.Condition)!, Array.Empty<ISymbol>());
        var result = node.WithCondition(cond).WithStatement(Embedded(node.Statement));
        if (node.Else != null)
        {
            var elseStmt = node.Else.Statement is IfStatementSyntax nested
                ? (StatementSyntax)VisitIfStatement(nested)!
                : Embedded(node.Else.Statement);
            result = result.WithElse(node.Else.WithStatement(elseStmt));
        }
        return result;
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        int line = LineOf(node);
        var cond = WrapCondition(line, node.Condition, (ExpressionSyntax)Visit(node.Condition)!, Array.Empty<ISymbol>());
        return node.WithCondition(cond).WithStatement(Embedded(node.Statement));
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        int line = LineOf(node.WhileKeyword);
        var body = Embedded(node.Statement);
        var cond = WrapCondition(line, node.Condition, (ExpressionSyntax)Visit(node.Condition)!, Array.Empty<ISymbol>());
        return node.WithStatement(body).WithCondition(cond);
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        int line = LineOf(node);
        var declared = node.Declaration?.Variables.Select(v => _model.GetDeclaredSymbol(v)).Where(s => s != null).Cast<ISymbol>().ToList()
                       ?? new List<ISymbol>();
        var result = node.WithStatement(Embedded(node.Statement));
        if (node.Condition != null)
            result = result.WithCondition(WrapCondition(line, node.Condition, (ExpressionSyntax)Visit(node.Condition)!, declared));
        return result;
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        int line = LineOf(node);
        var symbol = _model.GetDeclaredSymbol(node);
        var body = Embedded(node.Statement);
        var pairs = symbol == null ? new List<ArgumentSyntax>() : Pairs(line, new ISymbol[] { symbol }).ToList();
        var first = Call("V", new[] { Lit(line) }.Concat(pairs));
        if (body is BlockSyntax block)
            body = block.WithStatements(block.Statements.Insert(0, first));
        else
            body = Block(first, body);
        return node.WithStatement(body);
    }

    public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node)
    {
        var statements = new List<StatementSyntax>();
        foreach (var s in node.Statements) statements.AddRange(InstrumentStatement(s));
        return node.WithStatements(List(statements));
    }

    public override SyntaxNode? VisitBlock(BlockSyntax node) => InstrumentBlock(node);

    // Lambdas en expression bodies laten we ongemoeid.
    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;
    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;
    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

    // ------------------------------------------------------------------ methods
    static bool IsIterator(BlockSyntax body) =>
        body.DescendantNodes(n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<YieldStatementSyntax>().Any();

    BlockSyntax? InstrumentBody(SyntaxNode decl, BlockSyntax? body, IMethodSymbol? symbol, IEnumerable<ISymbol> parameters, bool includeThis)
    {
        if (body == null || IsIterator(body) || symbol is { IsAsync: true }) return null;
        _methods.Push(symbol);
        try
        {
            int line = LineOf(decl);
            var inner = InstrumentBlock(body);
            var args = new List<ArgumentSyntax> { Lit(MethodName(body)), Lit(decl.SyntaxTree.FilePath), Lit(line) };
            args.AddRange(Pairs(line, parameters));
            if (includeThis) { args.Add(Lit("this")); args.Add(Argument(ThisExpression())); }
            var enter = Call("Enter", args);
            var tryStmt = TryStatement(
                Block(Token(SyntaxKind.OpenBraceToken), inner.Statements, Token(SyntaxKind.CloseBraceToken)),
                List<CatchClauseSyntax>(),
                FinallyClause(Block(Call("Exit", Array.Empty<ArgumentSyntax>()))));
            return inner.WithStatements(List(new StatementSyntax[] { enter, tryStmt }));
        }
        finally { _methods.Pop(); }
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Body == null) return node;
        var symbol = _model.GetDeclaredSymbol(node);
        var body = InstrumentBody(node, node.Body, symbol, symbol?.Parameters ?? Enumerable.Empty<IParameterSymbol>(),
            includeThis: symbol is { IsStatic: false } && symbol.ContainingType.TypeKind == TypeKind.Class);
        return body == null ? node : node.WithBody(body);
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        if (node.Body == null) return node;
        var symbol = _model.GetDeclaredSymbol(node);
        var body = InstrumentBody(node, node.Body, symbol, symbol?.Parameters ?? Enumerable.Empty<IParameterSymbol>(), includeThis: false);
        return body == null ? node : node.WithBody(body);
    }

    public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        if (node.Body == null) return node;
        var symbol = _model.GetDeclaredSymbol(node);
        var parameters = symbol?.Parameters ?? Enumerable.Empty<IParameterSymbol>();
        var body = InstrumentBody(node, node.Body, symbol, parameters, includeThis: false);
        return body == null ? node : node.WithBody(body);
    }

    public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        if (node.Body == null) return node;
        var symbol = _model.GetDeclaredSymbol(node);
        var body = InstrumentBody(node, node.Body, symbol, symbol?.Parameters ?? Enumerable.Empty<IParameterSymbol>(), includeThis: false);
        return body == null ? node : node.WithBody(body);
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        if (node.Body == null) return node;
        var symbol = _model.GetDeclaredSymbol(node) as IMethodSymbol;
        var body = InstrumentBody(node, node.Body, symbol, symbol?.Parameters ?? Enumerable.Empty<IParameterSymbol>(), includeThis: false);
        return body == null ? node : node.WithBody(body);
    }

    public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var members = new List<MemberDeclarationSyntax>();
        foreach (var member in node.Members)
        {
            if (member is GlobalStatementSyntax g)
            {
                if (g.Statement is LocalFunctionStatementSyntax lf)
                {
                    members.Add(g.WithStatement((StatementSyntax)VisitLocalFunctionStatement(lf)!));
                    continue;
                }
                var entry = _model.GetEnclosingSymbol(g.Statement.SpanStart) as IMethodSymbol;
                _methods.Push(entry);
                try
                {
                    var list = InstrumentStatement(g.Statement);
                    if (!_topLevelEntered)
                    {
                        _topLevelEntered = true;
                        list.Insert(0, Lead(Call("Enter", new[] { Lit("Main"), Lit(g.SyntaxTree.FilePath), Lit(LineOf(g)) }), list[0]));
                        list[1] = StripLead(list[1]);
                    }
                    members.AddRange(list.Select(s => GlobalStatement(s)));
                }
                finally { _methods.Pop(); }
            }
            else members.Add((MemberDeclarationSyntax)Visit(member)!);
        }
        return node.WithMembers(List(members));
    }
}
