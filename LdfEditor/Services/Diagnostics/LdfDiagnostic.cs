namespace LdfEditor.Services.Diagnostics;

/// <summary>VS-style diagnostic for LDF (severity + span + code + message + rule).</summary>
public sealed class LdfDiagnostic
{
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;
    public string Code { get; init; } = "";
    public string Category { get; init; } = "LDF";
    public string Message { get; init; } = "";
    public string Why { get; init; } = "";
    public string Rule { get; init; } = "";
    public string Source { get; init; } = "";
    public int Line { get; init; }          /* 1-based; 0 = unknown */
    public int Column { get; init; }        /* 1-based */
    public int Length { get; init; } = 1;   /* highlight length */

    public override string ToString() =>
        Line > 0
            ? $"L{Line}:{Column} [{Code}] {Message}"
            : $"[{Code}] {Message}";
}

public enum DiagnosticSeverity
{
    Error = 0,
    Warning = 1,
    Info = 2,
    Hint = 3
}
