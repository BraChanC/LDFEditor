namespace LdfEditor.Services.Diagnostics;

/// <summary>
/// Suppress cascading / duplicate diagnostics (VS Error List style).
/// Keeps the most specific finding per location; drops noisy parser follow-ons.
/// </summary>
public static class LdfDiagnosticSuppressor
{
    public static List<LdfDiagnostic> Suppress(IReadOnlyList<LdfDiagnostic> input)
    {
        if (input.Count == 0) return new List<LdfDiagnostic>();

        var list = input
            .OrderBy(d => d.Line <= 0 ? int.MaxValue : d.Line)
            .ThenBy(d => d.Column)
            .ThenBy(d => Priority(d.Code))
            .ToList();

        var kept = new List<LdfDiagnostic>();
        var occupied = new HashSet<(int Line, string NormMsg)>();

        foreach (var d in list)
        {
            /* 1) Exact duplicate message on same line */
            var key = (d.Line, Norm(d.Message));
            if (d.Line > 0 && occupied.Contains(key))
                continue;

            /* 2) Prefer precise analyzers over ANTLR follow-ons on same / next line */
            if ((d.Code is "LDF0100" or "LDF0101") && d.Line > 0)
            {
                bool covered = kept.Any(k =>
                    k.Code is "LDF0010" or "LDF0005" or "LDF0006" or "LDF0020"
                    && k.Line > 0
                    && d.Line >= k.Line && d.Line <= k.Line + 1
                    && (k.Code is "LDF0006"
                        || string.IsNullOrEmpty(d.Source)
                        || string.IsNullOrEmpty(k.Source)
                        || k.Source == d.Source
                        || d.Message.Contains(k.Source, StringComparison.Ordinal)));
                if (covered)
                    continue;

                /* 3) Cascade: keep first 3 ANTLR errors within ±2 lines only */
                int syntaxKept = kept.Count(k =>
                    (k.Code is "LDF0100" or "LDF0101")
                    && k.Line > 0
                    && Math.Abs(k.Line - d.Line) <= 2);
                if (syntaxKept >= 3)
                    continue;
            }

            /* 4) Missing-section errors without line: keep one per code */
            if (d.Line <= 0 && kept.Any(k => k.Code == d.Code && k.Line <= 0))
                continue;

            kept.Add(d);
            if (d.Line > 0)
                occupied.Add(key);
        }

        return kept
            .OrderBy(d => d.Line <= 0 ? int.MaxValue : d.Line)
            .ThenBy(d => d.Column)
            .ThenBy(d => d.Severity)
            .ThenBy(d => d.Code)
            .ToList();
    }

    private static int Priority(string code) => code switch
    {
        "LDF0005" => 0,  /* punctuation — most precise */
        "LDF0006" => 0,  /* missing semicolon */
        "LDF0010" => 1,  /* unknown keyword */
        "LDF0020" => 2,  /* section order */
        "LDF0021" => 2,
        "LDF0001" or "LDF0002" or "LDF0003" or "LDF0004" => 3,
        "LDF0100" or "LDF0101" => 8, /* generic parse — lower priority */
        _ => 5
    };

    private static string Norm(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return "";
        /* Collapse ANTLR "expecting {...}" tails for dedupe */
        int idx = msg.IndexOf(" expecting ", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) msg = msg[..idx];
        return msg.Trim().ToLowerInvariant();
    }
}
