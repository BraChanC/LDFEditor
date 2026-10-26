using System.Text.RegularExpressions;

namespace LdfEditor.Services;

/// <summary>
/// Vector VLDFParser / LIN 2.2A section order (strict).
/// Matches error: "Error in line N: Keyword expected on reading token: Diagnostic_signals"
/// </summary>
public static class LdfSectionOrderValidator
{
    public sealed record Issue(
        int Line,
        string Token,
        string Message,
        string PreviousSection,
        string Constraint,
        string Why,
        string RuleId);

    /* Rank mirrors LIN_description_file production after header. */
    private static readonly Dictionary<string, int> SectionRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LIN_protocol_version"] = 1,
        ["LIN_language_version"] = 2,
        ["LIN_speed"] = 3,
        ["Channel_name"] = 4,
        ["LIN_sig_byte_order_big_endian"] = 5,
        ["LIN_sig_byte_order_little_endian"] = 5,
        ["Nodes"] = 10,
        ["Node_composition"] = 11,
        ["Diagnostic_addresses"] = 12,
        ["Signals"] = 20,
        ["Diagnostic_signals"] = 21,
        ["Frames"] = 30,
        ["Sporadic_frames"] = 31,
        ["Event_triggered_frames"] = 32,
        ["Diagnostic_frames"] = 33,
        ["Node_attributes"] = 40,
        ["Schedule_tables"] = 50,
        ["Signal_groups"] = 60,
        ["Signal_encoding_types"] = 70,
        ["Signal_representation"] = 80,
    };

    public const string CanonicalOrder =
        "Nodes → Signals → Diagnostic_signals → Frames → Sporadic_frames → " +
        "Event_triggered_frames → Diagnostic_frames → Node_attributes → " +
        "Schedule_tables → Signal_encoding_types → Signal_representation";

    public const string RuleIdSectionOrder = "LIN-2.2A-§9.2.2 / VLDFParser section order";

    private static readonly Regex SectionStart = new(
        @"^\s*(LIN_protocol_version|LIN_language_version|LIN_speed|Channel_name|" +
        @"LIN_sig_byte_order_big_endian|LIN_sig_byte_order_little_endian|" +
        @"Nodes|Node_composition|Diagnostic_addresses|Signals|Diagnostic_signals|" +
        @"Frames|Sporadic_frames|Event_triggered_frames|Diagnostic_frames|" +
        @"Node_attributes|Schedule_tables|Signal_groups|Signal_encoding_types|" +
        @"Signal_representation)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<Issue> Validate(string text)
    {
        var issues = new List<Issue>();
        if (string.IsNullOrEmpty(text)) return issues;

        int lastRank = 0;
        string? lastSection = null;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool inBlockComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = StripComments(raw, ref inBlockComment);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var m = SectionStart.Match(line);
            if (!m.Success) continue;

            var token = m.Groups[1].Value;
            if (!SectionRank.TryGetValue(token, out var rank)) continue;

            if (rank < lastRank)
            {
                var prev = lastSection ?? "(start)";
                var expected = ExpectedAfter(prev, lastRank);
                issues.Add(new Issue(
                    Line: i + 1,
                    Token: token,
                    Message: $"Keyword expected on reading token: {token}",
                    PreviousSection: prev,
                    Constraint: $"{RuleIdSectionOrder}: sections must appear in order — {CanonicalOrder}",
                    Why:
                        $"Section '{token}' appeared after '{prev}', but LIN requires '{token}' " +
                        $"before '{prev}'. At this point the parser expected one of: {expected}.",
                    RuleId: RuleIdSectionOrder));
            }
            else if (rank == lastRank
                     && !string.IsNullOrEmpty(lastSection)
                     && string.Equals(token, lastSection, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new Issue(
                    Line: i + 1,
                    Token: token,
                    Message: $"Duplicate section: {token}",
                    PreviousSection: lastSection!,
                    Constraint: $"{RuleIdSectionOrder}: each section keyword may appear at most once",
                    Why: $"Section '{token}' was already defined earlier in the file.",
                    RuleId: RuleIdSectionOrder));
            }

            if (rank >= lastRank)
            {
                lastRank = rank;
                lastSection = token;
            }
        }

        return issues;
    }

    public static string FormatVectorLoadError(IReadOnlyList<Issue> issues)
    {
        if (issues.Count == 0) return "";
        var first = issues[0];
        return "The file couldn't be loaded!\r\n\r\n" +
               $"Error in line {first.Line}: {first.Message}\r\n\r\n" +
               $"Why: {first.Why}\r\n\r\n" +
               $"Rule: {first.Constraint}\r\n\r\n" +
               "Error: General Parser failure.";
    }

    private static string ExpectedAfter(string previous, int lastRank)
    {
        var next = SectionRank
            .Where(kv => kv.Value > lastRank)
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(5)
            .ToList();
        if (next.Count == 0)
            return "(end of file / trailing optional sections)";
        return string.Join(", ", next) + ", …";
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
                if (end < 0)
                {
                    inBlock = true;
                    break;
                }
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
