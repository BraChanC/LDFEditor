using LdfEditor.Services.Diagnostics;

namespace LdfEditor.Services;

/// <summary>Vector Read Error / General Parser failure.</summary>
public sealed class LdfParseException : Exception
{
    public IReadOnlyList<LdfSectionOrderValidator.Issue> Issues { get; }
    public IReadOnlyList<LdfDiagnostic> Diagnostics { get; }

    public LdfParseException(
        string message,
        IReadOnlyList<LdfSectionOrderValidator.Issue> issues,
        IReadOnlyList<LdfDiagnostic>? diagnostics = null)
        : base(message)
    {
        Issues = issues;
        Diagnostics = diagnostics ?? Array.Empty<LdfDiagnostic>();
    }
}
