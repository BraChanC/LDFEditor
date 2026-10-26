using System.Text.RegularExpressions;

namespace LdfEditor.Services;

/// <summary>
/// Detects misspelled / unknown LDF section keywords (e.g. Diagnostic_signalss → Diagnostic_signals).
/// Vector VLDFParser: "Keyword expected on reading token: …"
/// </summary>
public static class LdfUnknownKeywordScanner
{
    public const string RuleId = "LIN / VLDFParser — section keyword must be one of the LIN grammar terminals";

    private static readonly string[] KnownSections =
    [
        "LIN_description_file",
        "LIN_protocol_version", "LIN_language_version", "LIN_speed", "Channel_name",
        "LIN_sig_byte_order_big_endian", "LIN_sig_byte_order_little_endian",
        "Nodes", "Node_composition", "Diagnostic_addresses",
        "Signals", "Diagnostic_signals",
        "Frames", "Sporadic_frames", "Event_triggered_frames", "Diagnostic_frames",
        "Node_attributes", "Schedule_tables",
        "Signal_groups", "Signal_encoding_types", "Signal_representation"
    ];

    /* Top-level-ish: identifier then '{'  OR identifier alone that looks like a section (has _) */
    private static readonly Regex Candidate = new(
        @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(\{|:|=|;)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<LdfSectionOrderValidator.Issue> Scan(string text)
    {
        var issues = new List<LdfSectionOrderValidator.Issue>();
        if (string.IsNullOrEmpty(text)) return issues;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool inBlock = false;
        int braceDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = StripComments(lines[i], ref inBlock);
            if (string.IsNullOrWhiteSpace(line)) continue;

            /* Only consider top-level candidates (outside section bodies). */
            if (braceDepth == 0)
            {
                var m = Candidate.Match(line);
                if (m.Success)
                {
                    var token = m.Groups[1].Value;
                    var trailer = m.Groups[2].Value;
                    /* Section starts: Token {   Header assigns: Token =   description: Token ; */
                    bool looksLikeSection = trailer == "{"
                        || (trailer == ";" && token.StartsWith("LIN_", StringComparison.Ordinal));
                    if (looksLikeSection
                        && !KnownSections.Contains(token, StringComparer.OrdinalIgnoreCase)
                        && !token.Equals("LIN_description_file", StringComparison.OrdinalIgnoreCase))
                    {
                        var suggestion = Closest(token);
                        var why = suggestion == null
                            ? $"'{token}' is not a valid LDF section / header keyword."
                            : $"'{token}' is not a valid LDF keyword. Did you mean '{suggestion}'? " +
                              $"(only one character differs from the expected section name.)";

                        var message = suggestion == null
                            ? $"Keyword expected on reading token: {token}"
                            : $"Keyword expected on reading token: {token} (did you mean {suggestion}?)";

                        issues.Add(new LdfSectionOrderValidator.Issue(
                            Line: i + 1,
                            Token: token,
                            Message: message,
                            PreviousSection: "",
                            Constraint: $"{RuleId}. Valid sections include: " +
                                        "Signals, Diagnostic_signals, Frames, Event_triggered_frames, " +
                                        "Diagnostic_frames, Node_attributes, Schedule_tables, …",
                            Why: why,
                            RuleId: RuleId));
                    }
                }
            }

            foreach (var ch in line)
            {
                if (ch == '{') braceDepth++;
                else if (ch == '}' && braceDepth > 0) braceDepth--;
            }
        }

        return issues;
    }

    public static string? Closest(string token)
    {
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var k in KnownSections)
        {
            int d = Levenshtein(token, k);
            if (d < bestDist)
            {
                bestDist = d;
                best = k;
            }
        }
        /* Suggest only if close (typo), not random. */
        if (best != null && bestDist > 0 && bestDist <= 3)
            return best;
        /* Also: prefix match with extra suffix chars (Diagnostic_signalss) */
        foreach (var k in KnownSections)
        {
            if (token.StartsWith(k, StringComparison.OrdinalIgnoreCase) && token.Length <= k.Length + 2)
                return k;
            if (k.StartsWith(token, StringComparison.OrdinalIgnoreCase) && k.Length <= token.Length + 2)
                return k;
        }
        return bestDist <= 3 ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var n = a.Length;
        var m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) dp[i, 0] = i;
        for (int j = 0; j <= m; j++) dp[0, j] = j;
        for (int i = 1; i <= n; i++)
        for (int j = 1; j <= m; j++)
        {
            int cost = a[i - 1] == b[j - 1] ? 0 : 1;
            dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
        }
        return dp[n, m];
    }

    private static string StripComments(string line, ref bool inBlock)
    {
        if (inBlock)
        {
            var end = line.IndexOf("*/", StringComparison.Ordinal);
            if (end < 0) return "";
            inBlock = false;
            line = line[(end + 2)..];
        }
        var sb = new System.Text.StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
            {
                var end = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { inBlock = true; break; }
                i = end + 1;
                continue;
            }
            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
                break;
            sb.Append(line[i]);
        }
        return sb.ToString();
    }
}
