namespace CSharpQuest.Server.Execution;

/// <summary>Eén broncodebestand van de leerling, bv. "Program.cs" of "Domain/Persoon.cs".</summary>
public sealed record SourceFile(string Path, string Content);

/// <summary>Categorieën waarin we alle feedback indelen (zie opdracht: syntax, compileer, logisch, ...).</summary>
public static class FeedbackCategory
{
    public const string Syntax = "syntax";            // Syntaxfout: de code kan niet gelezen worden
    public const string Compile = "compile";          // Compileerfout: de code klopt niet volgens de regels van C#
    public const string Warning = "warning";          // Waarschuwing van de compiler
    public const string Runtime = "runtime";          // Fout tijdens de uitvoering (exception)
    public const string Logic = "logic";              // Logische fout: verkeerd resultaat
    public const string Output = "output";            // Foutieve output (opmaak, tekst)
    public const string Structure = "structure";      // Slechte structuur
    public const string Redundant = "redundant";      // Onnodige code
    public const string Improvement = "improvement";  // Mogelijke verbetering
    public const string Security = "security";        // Niet toegelaten in de sandbox
    public const string Requirement = "requirement";  // Opdrachtvereiste niet nageleefd
}

public sealed class CodeDiagnostic
{
    public string Severity { get; init; } = "error";     // error | warning | info
    public string Category { get; init; } = FeedbackCategory.Compile;
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";            // Nederlandse, begrijpelijke uitleg
    public string? Original { get; init; }                // Originele compilerboodschap
    public string? Explanation { get; init; }             // Waarom is dit fout?
    public string? Tip { get; init; }                     // Hoe los je het op?
    public string? File { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
}

public sealed class CompileRequestOptions
{
    public bool ImplicitUsings { get; init; } = true;
    public string? MainType { get; init; }
    public bool Trace { get; init; }
    /// <summary>Extra C#-code met een eigen Main (voor unit-tests op methods van de leerling).</summary>
    public string? TestHarness { get; init; }
}

public sealed class TraceVariableInfo
{
    public string Name { get; init; } = "";
    public string Method { get; init; } = "";
    public string Kind { get; init; } = "local";   // local | param
    public int DeclLine { get; init; }
    public int ScopeEndLine { get; init; }
    public string? File { get; init; }
}

public sealed class CompileOutcome
{
    public bool Success { get; init; }
    public List<CodeDiagnostic> Diagnostics { get; init; } = new();
    public byte[]? Assembly { get; init; }
    public long CompileMs { get; init; }
    public List<TraceVariableInfo> TraceVariables { get; init; } = new();
    public string? TraceWarning { get; init; }
    public List<string> EntryPoints { get; init; } = new();
}

/// <summary>Resultaat van één (niet-interactieve) uitvoering.</summary>
public sealed class RunOutcome
{
    public string Status { get; set; } = "ok"; // ok | exception | timeout | cpuLimit | outputLimit | inputExhausted | stackOverflow | exitCode | memory | internal
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public int ExitCode { get; set; }
    public long DurationMs { get; set; }
    public RuntimeErrorInfo? Exception { get; set; }
    public List<CheckResult> Checks { get; set; } = new();
    public Dictionary<string, string> Files { get; set; } = new();
    public string? TraceJsonl { get; set; }
    public int? LinesRead { get; set; }
    public string? Message { get; set; }
}

public sealed class RuntimeErrorInfo
{
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Inner { get; set; }
    public int? Line { get; set; }
    public string? File { get; set; }
    public string? Method { get; set; }
    public List<string> Stack { get; set; } = new();
    public string Title { get; set; } = "";         // Nederlandse titel
    public string Explanation { get; set; } = "";   // Wat betekent dit?
    public string Tip { get; set; } = "";           // Hoe los je het op?
}

public sealed class CheckResult
{
    public string Label { get; set; } = "";
    public bool Passed { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? Note { get; set; }
}
