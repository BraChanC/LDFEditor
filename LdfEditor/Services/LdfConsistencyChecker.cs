using System.IO;
using System.Text;
using LdfEditor.Models;
using LdfEditor.Services.Diagnostics;

namespace LdfEditor.Services;

/// <summary>
/// Vector-style Consistency Checker UI data — backed by <see cref="LdfDiagnosticEngine"/>.
/// </summary>
public static class LdfConsistencyChecker
{
    public enum Severity { Error, Warning, Notification }

    public sealed class Finding
    {
        public string Category { get; init; } = "";
        public string Code { get; init; } = "";
        public string Inconsistency { get; init; } = "";
        public string Source { get; init; } = "";
        public string Constraint { get; init; } = "";
        public string Why { get; init; } = "";
        public string RuleId { get; init; } = "";
        public int? Line { get; init; }
        public int? Column { get; init; }
        public Severity Severity { get; init; } = Severity.Error;
    }

    public sealed class Report
    {
        public bool Ok { get; init; }
        public bool ParseFailed { get; init; }
        public string FilePath { get; init; } = "";
        public string FileText { get; init; } = "";
        public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
        public string SummaryText { get; init; } = "";
    }

    public static Report CheckFile(string? path, string? text, LdfDatabase? db,
        IReadOnlyList<LdfSectionOrderValidator.Issue>? priorIssues = null,
        IReadOnlyList<LdfDiagnostic>? priorDiagnostics = null)
    {
        text ??= "";
        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(path) && File.Exists(path))
            text = File.ReadAllText(path);

        List<LdfDiagnostic> diags;
        if (priorDiagnostics is { Count: > 0 })
            diags = priorDiagnostics.ToList();
        else if (db != null && string.IsNullOrEmpty(text))
        {
            /* In-memory model only — semantic pass via re-serialize */
            text = LdfWriter.Write(db);
            diags = LdfDiagnosticEngine.Analyze(text, path).Diagnostics.ToList();
        }
        else
            diags = LdfDiagnosticEngine.Analyze(text, path).Diagnostics.ToList();

        /* Merge any legacy prior issues not already covered */
        if (priorIssues is { Count: > 0 })
        {
            foreach (var iss in priorIssues)
            {
                if (diags.Any(d => d.Line == iss.Line && d.Message.Contains(iss.Token)))
                    continue;
                diags.Add(new LdfDiagnostic
                {
                    Code = "LDF0020",
                    Category = "Basic / Format",
                    Severity = DiagnosticSeverity.Error,
                    Message = iss.Message,
                    Why = iss.Why,
                    Rule = iss.Constraint,
                    Source = iss.Token,
                    Line = iss.Line,
                    Column = 1,
                    Length = Math.Max(1, iss.Token.Length)
                });
            }
        }

        bool parseFailed = diags.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && (d.Code.StartsWith("LDF00") || d.Code.StartsWith("LDF01")));

        if (parseFailed)
        {
            /* Vector BC0030 + skipped dependents */
            var root = diags.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            diags.Add(new LdfDiagnostic
            {
                Code = "0030",
                Category = "Basic checks",
                Severity = DiagnosticSeverity.Error,
                Message = "Check execution failed",
                Why = root == null
                    ? "Core checks cannot run because the LDF failed validation."
                    : $"Root cause [{root.Code}] line {root.Line}: {root.Message}. {root.Why}",
                Rule = "BC0030 Core check — requires successful LDF parse",
                Source = "0030",
                Line = root?.Line ?? 0,
                Column = root?.Column ?? 1
            });
            foreach (var code in new[] { "0101", "0102", "0103", "0104", "0105", "0106", "0107", "0108",
                                         "0201", "0202", "0203", "0204", "0205", "0206" })
            {
                diags.Add(new LdfDiagnostic
                {
                    Code = code,
                    Category = code.StartsWith("01") ? "LDF Format checks" : "Modeling checks",
                    Severity = DiagnosticSeverity.Info,
                    Message = "Check not executed because preconditional check failed",
                    Why = $"Dependent check [{code}] requires a successfully validated LDF (precondition BC0030).",
                    Rule = "Dependent check skipped — parse / basic validation failed",
                    Source = "0030"
                });
            }
        }

        var findings = diags.Select(ToFinding).ToList();
        bool ok = !diags.Any(d => d.Severity == DiagnosticSeverity.Error);

        var sb = new StringBuilder();
        sb.AppendLine($"LIN Consistency Checker — {Path.GetFileName(path ?? "")}");
        sb.AppendLine($"{findings.Count} finding(s), " +
                      $"{findings.Count(f => f.Severity == Severity.Error)} error(s).");
        sb.AppendLine();
        foreach (var f in findings.Where(x => x.Severity == Severity.Error).Take(20))
        {
            sb.AppendLine($"  [{f.Code}] L{f.Line}:{f.Column}  {f.Inconsistency}");
            if (!string.IsNullOrEmpty(f.Why)) sb.AppendLine($"       Why:  {f.Why}");
            if (!string.IsNullOrEmpty(f.Constraint)) sb.AppendLine($"       Rule: {f.Constraint}");
        }

        return new Report
        {
            Ok = ok,
            ParseFailed = parseFailed,
            FilePath = path ?? "",
            FileText = text,
            Findings = findings,
            SummaryText = sb.ToString()
        };
    }

    public static Report Check(LdfDatabase db) =>
        CheckFile(db.FilePath, null, db);

    public static (bool Ok, string Report) CheckLegacy(LdfDatabase db)
    {
        var r = Check(db);
        return (r.Ok, r.SummaryText);
    }

    private static Finding ToFinding(LdfDiagnostic d) => new()
    {
        Category = string.IsNullOrEmpty(d.Category) ? "LDF" : d.Category,
        Code = d.Code,
        Inconsistency = d.Message,
        Source = d.Source,
        Constraint = d.Rule,
        Why = d.Why,
        RuleId = d.Code + (string.IsNullOrEmpty(d.Rule) ? "" : " — " + d.Rule),
        Line = d.Line > 0 ? d.Line : null,
        Column = d.Column > 0 ? d.Column : null,
        Severity = d.Severity switch
        {
            DiagnosticSeverity.Error => Severity.Error,
            DiagnosticSeverity.Warning => Severity.Warning,
            _ => Severity.Notification
        }
    };
}
