using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using LdfEditor.Antlr;
using LdfEditor.Models;
using LdfEditor.Services;

namespace LdfEditor.Services.Diagnostics;

/// <summary>
/// VS-style multi-diagnostic analysis for LDF (syntax + LIN 2.2A structure + semantics).
/// Collects as many issues as possible instead of failing on the first error.
/// </summary>
public static class LdfDiagnosticEngine
{
    public sealed class Result
    {
        public IReadOnlyList<LdfDiagnostic> Diagnostics { get; init; } = Array.Empty<LdfDiagnostic>();
        public LdfDatabase? Database { get; init; }
        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        public string FormatErrorDialog()
        {
            var errors = Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count == 0) return "";
            var first = errors[0];
            var sb = new StringBuilder();
            sb.AppendLine("The file couldn't be loaded!");
            sb.AppendLine();
            if (first.Line > 0)
                sb.AppendLine($"Error in line {first.Line}: {first.Message}");
            else
                sb.AppendLine($"Error: {first.Message}");
            if (!string.IsNullOrEmpty(first.Why))
            {
                sb.AppendLine();
                sb.AppendLine($"Why: {first.Why}");
            }
            if (!string.IsNullOrEmpty(first.Rule))
            {
                sb.AppendLine();
                sb.AppendLine($"Rule: {first.Rule}");
            }
            if (errors.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine($"(+ {errors.Count - 1} more error(s) — see LIN Consistency Checker)");
            }
            sb.AppendLine();
            sb.Append("Error: General Parser failure.");
            return sb.ToString();
        }

        public IReadOnlyList<LdfSectionOrderValidator.Issue> AsLegacyIssues() =>
            Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new LdfSectionOrderValidator.Issue(
                    Line: d.Line,
                    Token: string.IsNullOrEmpty(d.Source) ? d.Code : d.Source,
                    Message: d.Message,
                    PreviousSection: "",
                    Constraint: d.Rule,
                    Why: d.Why,
                    RuleId: d.Rule))
                .ToList();
    }

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

    public static Result Analyze(string text, string? path = null)
    {
        text ??= "";
        var diags = new List<LdfDiagnostic>();

        /* 0) Full-width / CJK punctuation → report + ASCII-normalize for recovery */
        var norm = LdfTextNormalizer.Normalize(text);
        diags.AddRange(norm.Diagnostics);
        var workText = norm.NormalizedText;

        var map = new LdfSourceMap(workText);

        AnalyzeHeader(map, diags);
        AnalyzeMissingSemicolons(map, diags);
        AnalyzeUnknownKeywords(map, diags);
        AnalyzeSectionOrder(map, diags);
        AnalyzeDuplicateSections(map, diags);
        AnalyzeMandatorySections(map, diags);
        AnalyzeDuplicateTopLevelNames(map, diags);
        AnalyzeFrameIdsInText(map, diags);

        LdfDatabase? db = null;
        var syntax = TryParse(workText, path, diags);
        if (syntax.Ok)
        {
            db = syntax.Database;
            AnalyzeSemantics(db!, map, diags);
        }

        /* VS-style: suppress cascades / duplicates, then sort */
        diags = LdfDiagnosticSuppressor.Suppress(diags);

        return new Result { Diagnostics = diags, Database = db };
    }

    /* -------------------- Missing semicolons -------------------- */

    private static void AnalyzeMissingSemicolons(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        /* Header / single-line statements that must end with ';' */
        /* \b only on bare keywords — not after '=' (non-word → no boundary before space) */
        var needsSemi = new Regex(
            @"^\s*(LIN_description_file\b|LIN_protocol_version\s*=|LIN_language_version\s*=|" +
            @"LDF_file_revision\s*=|LIN_speed\s*=|Channel_name\s*=|" +
            @"LIN_sig_byte_order_big_endian\b|LIN_sig_byte_order_little_endian\b)",
            RegexOptions.CultureInvariant);

        /* Inside sections: "Name: …" entries typically end with ; */
        var entryLine = new Regex(
            @"^\s*[A-Za-z_][A-Za-z0-9_]*\s*:.*\S\s*$",
            RegexOptions.CultureInvariant);

        int depth = 0;
        for (int i = 0; i < map.Lines.Length; i++)
        {
            var raw = map.Lines[i];
            var line = Strip(raw).TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            int depthAtStart = depth;
            foreach (var ch in line)
            {
                if (ch == '{') depth++;
                else if (ch == '}' && depth > 0) depth--;
            }

            if (line.EndsWith(';') || line.EndsWith('{') || line.EndsWith('}'))
                continue;
            if (line.StartsWith("//") || line.StartsWith("/*"))
                continue;

            bool header = depthAtStart == 0 && needsSemi.IsMatch(line);
            bool entry = depthAtStart >= 1 && entryLine.IsMatch(line)
                         && !line.Contains('{'); /* frame header lines end with { */

            if (!header && !entry) continue;

            Add(diags, "LDF0006", i + 1, Math.Max(1, line.Length), 1,
                "Missing ';' at end of statement",
                $"Line appears to be a complete LDF statement but does not end with ';'. " +
                $"Example: append semicolon → `{Truncate(line, 40)};`",
                "LIN 2.2A — every declaration / list item ends with ';'",
                ";");
        }
    }

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : s[..n] + "…";

    /* -------------------- Header -------------------- */

    private static void AnalyzeHeader(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        bool hasDesc = false;
        for (int i = 0; i < map.Lines.Length; i++)
        {
            var t = Strip(map.Lines[i]).TrimStart();
            if (t.StartsWith("LIN_description_file", StringComparison.Ordinal))
            {
                hasDesc = true;
                if (!t.Contains(';'))
                    Add(diags, "LDF0001", i + 1, 1, 20,
                        "Missing ';' after LIN_description_file",
                        "The file must start with: LIN_description_file;",
                        "LIN 2.2A — LIN_description_file terminal");
                break;
            }
            if (t.Length > 0 && !t.StartsWith('/') && !t.StartsWith('*'))
                break;
        }
        if (!hasDesc)
            Add(diags, "LDF0001", 1, 1, 1,
                "Missing LIN_description_file;",
                "Every LDF must begin with the LIN_description_file declaration.",
                "LIN 2.2A — file header");

        RequireHeaderAssign(map, diags, "LIN_protocol_version", "LDF0002");
        RequireHeaderAssign(map, diags, "LIN_language_version", "LDF0003");
        RequireHeaderAssign(map, diags, "LIN_speed", "LDF0004");
    }

    private static void RequireHeaderAssign(LdfSourceMap map, List<LdfDiagnostic> diags, string key, string code)
    {
        var loc = map.FindSection(key);
        if (loc == null)
        {
            Add(diags, code, 0, 0, 0,
                $"Missing required header '{key}'",
                $"LIN requires '{key} = …;' near the top of the file.",
                "LIN 2.2A — global definitions");
            return;
        }
        var line = map.Lines[loc.Value.Line - 1];
        if (!line.Contains('='))
            Add(diags, code, loc.Value.Line, loc.Value.Column, key.Length,
                $"'{key}' must use assignment form: {key} = …;",
                "Header fields are assignments, not bare identifiers.",
                "LIN 2.2A — global definitions");
    }

    /* -------------------- Unknown keywords -------------------- */

    private static void AnalyzeUnknownKeywords(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        int depth = 0;
        var re = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(\{|:|=|;)", RegexOptions.CultureInvariant);
        for (int i = 0; i < map.Lines.Length; i++)
        {
            var raw = map.Lines[i];
            var line = Strip(raw);
            if (depth == 0)
            {
                var m = re.Match(line);
                if (m.Success)
                {
                    var token = m.Groups[1].Value;
                    var trail = m.Groups[2].Value;
                    bool sectionLike = trail == "{"
                        || (trail == ";" && token.StartsWith("LIN_", StringComparison.Ordinal));
                    if (sectionLike
                        && !KnownSections.Contains(token, StringComparer.OrdinalIgnoreCase))
                    {
                        var suggestion = Closest(token);
                        var msg = suggestion == null
                            ? $"Unknown keyword '{token}'"
                            : $"Unknown keyword '{token}' (did you mean '{suggestion}'?)";
                        var why = suggestion == null
                            ? $"'{token}' is not a LIN LDF section/header keyword."
                            : $"'{token}' is not valid. Closest match is '{suggestion}' " +
                              $"(edit distance {Levenshtein(token, suggestion)}).";
                        Add(diags, "LDF0010", i + 1, m.Groups[1].Index + 1, token.Length,
                            msg, why,
                            "LIN / VLDFParser — section keyword must be a grammar terminal",
                            token);
                    }
                }
            }
            foreach (var ch in line)
            {
                if (ch == '{') depth++;
                else if (ch == '}' && depth > 0) depth--;
            }
        }
    }

    /* -------------------- Section order -------------------- */

    private static void AnalyzeSectionOrder(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        int lastRank = 0;
        string? last = null;
        var re = new Regex(
            @"^\s*(LIN_protocol_version|LIN_language_version|LIN_speed|Channel_name|" +
            @"LIN_sig_byte_order_big_endian|LIN_sig_byte_order_little_endian|" +
            @"Nodes|Node_composition|Diagnostic_addresses|Signals|Diagnostic_signals|" +
            @"Frames|Sporadic_frames|Event_triggered_frames|Diagnostic_frames|" +
            @"Node_attributes|Schedule_tables|Signal_groups|Signal_encoding_types|" +
            @"Signal_representation)\b",
            RegexOptions.CultureInvariant);

        for (int i = 0; i < map.Lines.Length; i++)
        {
            var m = re.Match(Strip(map.Lines[i]));
            if (!m.Success) continue;
            var token = m.Groups[1].Value;
            if (!SectionRank.TryGetValue(token, out var rank)) continue;

            if (rank < lastRank)
            {
                var prev = last ?? "(start)";
                var expected = SectionRank.Where(kv => kv.Value > lastRank)
                    .OrderBy(kv => kv.Value).Select(kv => kv.Key).Take(5);
                Add(diags, "LDF0020", i + 1, m.Index + 1, token.Length,
                    $"Keyword expected on reading token: {token}",
                    $"Section '{token}' appeared after '{prev}', but LIN requires '{token}' before '{prev}'. " +
                    $"Expected next: {string.Join(", ", expected)}, …",
                    $"LIN 2.2A §9.2.2 / VLDFParser section order — {CanonicalOrder}",
                    token);
            }
            else
            {
                lastRank = rank;
                last = token;
            }
        }
    }

    /* -------------------- Duplicates -------------------- */

    private static void AnalyzeDuplicateSections(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var re = new Regex(
            @"^\s*(Nodes|Signals|Diagnostic_signals|Frames|Sporadic_frames|Event_triggered_frames|" +
            @"Diagnostic_frames|Node_attributes|Schedule_tables|Signal_encoding_types|" +
            @"Signal_representation|Diagnostic_addresses|Node_composition)\b",
            RegexOptions.CultureInvariant);
        for (int i = 0; i < map.Lines.Length; i++)
        {
            var m = re.Match(Strip(map.Lines[i]));
            if (!m.Success) continue;
            var name = m.Groups[1].Value;
            if (seen.TryGetValue(name, out var firstLine))
            {
                Add(diags, "LDF0021", i + 1, m.Index + 1, name.Length,
                    $"Duplicate section '{name}'",
                    $"Section '{name}' was already defined at line {firstLine}. Each section may appear at most once.",
                    "LIN 2.2A — single occurrence of each section",
                    name);
            }
            else seen[name] = i + 1;
        }
    }

    private static void AnalyzeDuplicateTopLevelNames(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        /* Signal / frame name lines:  Name: … */
        CollectDupNames(map, diags, @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:\s*\d", "LDF0031", "signal/entry");
        CollectDupNames(map, diags, @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(0x[0-9A-Fa-f]+|\d+)\s*,", "LDF0032", "frame");
    }

    private static void CollectDupNames(LdfSourceMap map, List<LdfDiagnostic> diags,
        string pattern, string code, string kind)
    {
        var re = new Regex(pattern, RegexOptions.CultureInvariant);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int depth = 0;
        string? section = null;
        for (int i = 0; i < map.Lines.Length; i++)
        {
            var line = Strip(map.Lines[i]);
            if (depth == 0)
            {
                var sm = Regex.Match(line, @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\{");
                if (sm.Success) section = sm.Groups[1].Value;
            }
            foreach (var ch in line)
            {
                if (ch == '{') depth++;
                else if (ch == '}' && depth > 0) depth--;
            }
            if (section is not ("Signals" or "Diagnostic_signals" or "Frames" or "Diagnostic_frames"
                or "Event_triggered_frames" or "Sporadic_frames" or "Schedule_tables"
                or "Signal_encoding_types"))
                continue;
            if (code == "LDF0031" && section is not ("Signals" or "Diagnostic_signals")) continue;
            if (code == "LDF0032" && section is not ("Frames" or "Diagnostic_frames" or "Event_triggered_frames")) continue;

            var m = re.Match(line);
            if (!m.Success) continue;
            var name = m.Groups[1].Value;
            if (seen.TryGetValue(name, out var first))
                Add(diags, code, i + 1, m.Groups[1].Index + 1, name.Length,
                    $"Duplicate {kind} name '{name}'",
                    $"'{name}' was already defined at line {first}. Names must be unique within the LDF.",
                    "LIN 2.2A — unique object names",
                    name);
            else seen[name] = i + 1;
        }
    }

    /* -------------------- Mandatory -------------------- */

    private static void AnalyzeMandatorySections(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        bool Has(string s) => map.FindSection(s) != null;
        if (!Has("Nodes"))
            Add(diags, "LDF0040", 0, 0, 0, "Missing mandatory section 'Nodes'",
                "LIN requires Nodes { Master: …; Slaves: …; }.",
                "LIN 2.2A — Node_def");
        if (!Has("Signals"))
            Add(diags, "LDF0041", 0, 0, 0, "Missing mandatory section 'Signals'",
                "A valid LDF must declare at least the Signals { } section (even if empty of app signals before diagnostics).",
                "LIN 2.2A — Signal_def / Vector ModelWriter");
        if (!Has("Frames") && !Has("Diagnostic_frames"))
            Add(diags, "LDF0042", 0, 0, 0, "Missing 'Frames' / 'Diagnostic_frames'",
                "LIN requires frame definitions (application and/or diagnostic).",
                "LIN 2.2A — Frame_def");
        if (!Has("Schedule_tables"))
            Add(diags, "LDF0043", 0, 0, 0, "Missing section 'Schedule_tables'",
                "Schedule_tables { } is required (may be empty).",
                "LIN 2.2A — Schedule_table_def",
                severity: DiagnosticSeverity.Warning);
    }

    /* -------------------- Frame IDs in text -------------------- */

    private static void AnalyzeFrameIdsInText(LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        /* name: id, publisher, len {   OR   name: id {  (diagnostic) */
        var re = new Regex(
            @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(0x[0-9A-Fa-f]+|\d+)\b",
            RegexOptions.CultureInvariant);
        int depth = 0;
        string? section = null;
        var usedIds = new Dictionary<int, (string Name, int Line)>();

        for (int i = 0; i < map.Lines.Length; i++)
        {
            var line = Strip(map.Lines[i]);
            if (depth == 0)
            {
                var sm = Regex.Match(line, @"^\s*(Frames|Diagnostic_frames|Event_triggered_frames)\s*\{");
                if (sm.Success) section = sm.Groups[1].Value;
            }
            int lineDepthStart = depth;
            foreach (var ch in line)
            {
                if (ch == '{') depth++;
                else if (ch == '}' && depth > 0) depth--;
            }
            if (section == null) continue;
            if (lineDepthStart < 1) continue; /* inside section body */

            var m = re.Match(line);
            if (!m.Success) continue;
            var name = m.Groups[1].Value;
            var idText = m.Groups[2].Value;
            int id = idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(idText, 16)
                : int.Parse(idText, CultureInfo.InvariantCulture);

            if (section is "Frames" or "Event_triggered_frames")
            {
                if (id < 0 || id > 59)
                    Add(diags, "LDF0050", i + 1, m.Groups[2].Index + 1, idText.Length,
                        $"Frame ID {idText} out of range for unconditional/ETF (valid 0..59 / 0x00..0x3B)",
                        $"Application frame IDs must be 0–59. 0x3C/0x3D are reserved for diagnostic frames.",
                        "LIN 2.2A — frame identifiers",
                        name);
            }
            if (section == "Diagnostic_frames")
            {
                if (id is not (0x3C or 60 or 0x3D or 61))
                    Add(diags, "LDF0051", i + 1, m.Groups[2].Index + 1, idText.Length,
                        $"Diagnostic frame ID should be 0x3C (MasterReq) or 0x3D (SlaveResp), got {idText}",
                        "Diagnostic_frames use fixed IDs 60/61 (0x3C/0x3D).",
                        "LIN 2.2A — diagnostic frame IDs",
                        name);
            }

            if (usedIds.TryGetValue(id, out var prev) && !string.Equals(prev.Name, name, StringComparison.OrdinalIgnoreCase))
                Add(diags, "LDF0052", i + 1, m.Groups[2].Index + 1, idText.Length,
                    $"Duplicate frame ID {idText} (also used by '{prev.Name}' at line {prev.Line})",
                    "Frame identifiers must be unique across Frames / Event_triggered / Diagnostic_frames.",
                    "LIN 2.2A — unique frame ID",
                    name);
            else
                usedIds[id] = (name, i + 1);

            if (depth == 0) section = null;
        }
    }

    /* -------------------- ANTLR parse -------------------- */

    private sealed class ParseOutcome
    {
        public bool Ok { get; init; }
        public LdfDatabase? Database { get; init; }
    }

    private static ParseOutcome TryParse(string text, string? path, List<LdfDiagnostic> diags)
    {
        try
        {
            var input = new AntlrInputStream(text);
            var lexer = new LdfLexer(input);
            lexer.RemoveErrorListeners();
            var lexErr = new CollectingErrorListener("LDF0100");
            lexer.AddErrorListener(lexErr);

            var tokens = new CommonTokenStream(lexer);
            var parser = new LdfParser(tokens);
            /* DefaultErrorStrategy recovers and continues → multiple syntax diagnostics */
            parser.ErrorHandler = new DefaultErrorStrategy();
            parser.RemoveErrorListeners();
            var parseErr = new CollectingErrorListener("LDF0101");
            parser.AddErrorListener(parseErr);

            _ = parser.ldfFile();
            foreach (var d in lexErr.Diagnostics) diags.Add(d);
            foreach (var d in parseErr.Diagnostics) diags.Add(d);

            if (lexErr.Diagnostics.Count > 0 || parseErr.Diagnostics.Count > 0)
                return new ParseOutcome { Ok = false };

            var db = new LdfAntlrLoader().LoadTextUnchecked(text, path ?? "");
            return new ParseOutcome { Ok = true, Database = db };
        }
        catch (Exception ex)
        {
            Add(diags, "LDF0101", 0, 0, 0,
                "Parse failed: " + FirstLine(ex.Message),
                ex.Message,
                "ANTLR / VLDFParser syntax");
            return new ParseOutcome { Ok = false };
        }
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOfAny(['\r', '\n']);
        return i < 0 ? s : s[..i];
    }

    /* -------------------- Semantics -------------------- */

    private static void AnalyzeSemantics(LdfDatabase db, LdfSourceMap map, List<LdfDiagnostic> diags)
    {
        var nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(db.Master)) nodes.Add(db.Master);
        foreach (var s in db.Slaves) nodes.Add(s);

        if (string.IsNullOrWhiteSpace(db.Master))
            Add(diags, "LDF0200", Loc(map, "Master"),
                "Master node is not defined",
                "Nodes { } must contain Master: <name>, <timebase> ms, <jitter> ms;",
                "LIN 2.2A — Node_def / MC0202");

        foreach (var sig in db.Signals)
        {
            if (!string.IsNullOrEmpty(sig.Publisher) && !nodes.Contains(sig.Publisher))
                Add(diags, "LDF0204", Loc(map, sig.Name),
                    $"Signal '{sig.Name}': unknown publisher '{sig.Publisher}'",
                    $"Publisher must be Master or a declared slave. Declared: {string.Join(", ", nodes)}.",
                    "LIN 2.2A — Signal_def / MC0204", sig.Name);
            foreach (var sub in sig.Subscribers.Where(s => !nodes.Contains(s)))
                Add(diags, "LDF0204", Loc(map, sig.Name),
                    $"Signal '{sig.Name}': unknown subscriber '{sub}'",
                    $"Subscriber '{sub}' is not in Nodes.",
                    "LIN 2.2A — Signal_def / MC0204", sig.Name);

            if (sig.SizeBits is < 1 or > 64)
                Add(diags, "LDF0205", Loc(map, sig.Name),
                    $"Signal '{sig.Name}': invalid length {sig.SizeBits} bits",
                    "Signal size must be 1..64 bits.",
                    "LIN 2.2A — signal size", sig.Name);
        }

        var frameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in db.Frames)
        {
            frameNames.Add(f.Name);
            if (!f.IsDiagnostic && !string.IsNullOrEmpty(f.Publisher) && !nodes.Contains(f.Publisher))
                Add(diags, "LDF0203", Loc(map, f.Name),
                    $"Frame '{f.Name}': unknown publisher '{f.Publisher}'",
                    "Frame publisher must be a declared node.",
                    "LIN 2.2A — Frame_def / MC0203", f.Name);

            int maxBit = f.Length > 0 ? f.Length * 8 : 64;
            foreach (var fs in f.Signals)
            {
                if (db.FindSignal(fs.Name) == null)
                    Add(diags, "LDF0203", Loc(map, fs.Name),
                        $"Frame '{f.Name}': mapped signal '{fs.Name}' is undefined",
                        "Every mapped signal must be declared in Signals or Diagnostic_signals.",
                        "LIN 2.2A — frame signal mapping / MC0203", fs.Name);
                if (fs.StartBit < 0 || fs.StartBit >= maxBit)
                    Add(diags, "LDF0206", Loc(map, f.Name),
                        $"Frame '{f.Name}': signal '{fs.Name}' start bit {fs.StartBit} outside frame ({f.Length} bytes)",
                        $"Valid start bits are 0..{maxBit - 1} for a {f.Length}-byte frame.",
                        "LIN 2.2A — signal packing", f.Name);
            }
        }

        foreach (var e in db.EventTriggeredFrames)
        {
            frameNames.Add(e.Name);
            LdfFrameAssociationRules.TryValidateEventTriggered(db, e, (sym, msg) =>
                Add(diags, "LDF0105", Loc(map, e.Name), msg,
                    "Event-Triggered Frame: collision schedule, associated unconditional frames " +
                    "(slave publishers, equal length, distinct publishers).",
                    "LIN 2.2A — Event_triggered_frame_def / Vector ValidateETFTransmitter", sym));
        }
        foreach (var sp in db.SporadicFrames)
        {
            frameNames.Add(sp.Name);
            LdfFrameAssociationRules.TryValidateSporadic(db, sp, (sym, msg) =>
                Add(diags, "LDF0106", Loc(map, sp.Name), msg,
                    "Sporadic Frame: associated frames must exist and be master-published unconditional frames.",
                    "LIN 2.2A — Sporadic_frame_def / Vector ValidateSPFrames", sym));
        }

        bool needsAttrs = ProtocolAtLeast(db.ProtocolVersion, "2.0");
        foreach (var slave in db.Slaves)
        {
            var attr = db.NodeAttributes.FirstOrDefault(a =>
                string.Equals(a.Name, slave, StringComparison.OrdinalIgnoreCase));
            if (attr == null && needsAttrs && db.DiagnosticAddresses.Count == 0)
                Add(diags, "LDF0202", Loc(map, slave),
                    $"Slave '{slave}': missing Node_attributes",
                    $"Protocol '{db.ProtocolVersion}' requires Node_attributes {{ {slave} {{ … }} }}.",
                    "LIN 2.2A — Node_attributes_def / MC0202", slave);
            if (attr != null && !string.IsNullOrEmpty(attr.ResponseError)
                && db.FindSignal(attr.ResponseError) == null)
                Add(diags, "LDF0202", Loc(map, attr.ResponseError),
                    $"Slave '{slave}': response_error '{attr.ResponseError}' not found",
                    "response_error must name a signal published by the slave.",
                    "LIN 2.2A — Node_attributes / MC0202", slave);
            if (attr != null && attr.ConfigurableFrames.Count == 0
                && needsAttrs && ProtocolAtLeast(db.ProtocolVersion, "2.1"))
                Add(diags, "LDF0108", Loc(map, slave),
                    $"Slave '{slave}': configurable_frames missing",
                    "LIN ≥ 2.1 should list configurable_frames for each slave.",
                    "LIN 2.2A — configurable_frames / FC0108", slave,
                    DiagnosticSeverity.Warning);

            if (attr != null && (attr.ConfiguredNad < 0 || attr.ConfiguredNad > 255))
                Add(diags, "LDF0207", Loc(map, slave),
                    $"Slave '{slave}': configured_NAD {attr.ConfiguredNad} out of range 0..255",
                    "NAD is one byte (0..255; 0x7F wildcard, 0xFF unconfigured).",
                    "LIN 2.2A — NAD", slave);
        }

        foreach (var st in db.Schedules)
        {
            foreach (var slot in st.Slots)
            {
                var target = slot.FrameName;
                if (string.IsNullOrEmpty(target)) continue;
                bool predefined = target is "MasterReq" or "SlaveResp" or "CommanderReq" or "ResponderResp"
                    || slot.Kind is "AssignNAD" or "AssignFrameId" or "AssignFrameIdRange"
                        or "ConditionalChangeNAD" or "DataDump" or "SaveConfiguration" or "FreeFormat"
                        or "UnassignFrameId";
                if (!predefined && !frameNames.Contains(target)
                    && slot.Kind is "Unconditional" or "EventTriggered" or "Sporadic" or "")
                    Add(diags, "LDF0201", Loc(map, st.Name),
                        $"Schedule '{st.Name}': unknown target '{target}'",
                        "Schedule slots must reference a known frame or predefined command.",
                        "LIN 2.2A — Schedule_table / MC0201", st.Name);
            }
        }

        foreach (var enc in db.Encodings)
        {
            var used = db.Representations.Any(r =>
                string.Equals(r.Encoding, enc.Name, StringComparison.OrdinalIgnoreCase));
            if (!used)
                Add(diags, "LDF0208", Loc(map, enc.Name),
                    $"Encoding '{enc.Name}' is never referenced in Signal_representation",
                    "Unused encodings are allowed but often indicate a missing representation entry.",
                    "LIN 2.2A — Signal_representation", enc.Name,
                    DiagnosticSeverity.Hint);
        }
        foreach (var r in db.Representations)
        {
            if (db.Encodings.All(e => !string.Equals(e.Name, r.Encoding, StringComparison.OrdinalIgnoreCase)))
                Add(diags, "LDF0209", Loc(map, r.Encoding),
                    $"Signal_representation references unknown encoding '{r.Encoding}'",
                    "Encoding must be declared in Signal_encoding_types { }.",
                    "LIN 2.2A — Signal_representation", r.Encoding);
            foreach (var sn in r.Signals)
            {
                if (db.FindSignal(sn) == null)
                    Add(diags, "LDF0209", Loc(map, sn),
                        $"Signal_representation references unknown signal '{sn}'",
                        "Signal must be declared in Signals { }.",
                        "LIN 2.2A — Signal_representation", sn);
            }
        }
    }

    /* -------------------- helpers -------------------- */

    private static (int Line, int Column, int Length) Loc(LdfSourceMap map, string? token)
    {
        if (string.IsNullOrEmpty(token)) return (0, 0, 0);
        return map.Find(token) ?? (0, 0, token.Length);
    }

    private static void Add(List<LdfDiagnostic> diags, string code,
        (int Line, int Column, int Length) loc,
        string message, string why, string rule, string? source = null,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
        => Add(diags, code, loc.Line, loc.Column, loc.Length, message, why, rule, source, severity);

    private static void Add(List<LdfDiagnostic> diags, string code, int line, int col, int len,
        string message, string why, string rule, string? source = null,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        diags.Add(new LdfDiagnostic
        {
            Code = code,
            Category = code.StartsWith("LDF00") || code.StartsWith("LDF01") ? "Basic / Format" : "Modeling",
            Severity = severity,
            Message = message,
            Why = why,
            Rule = rule,
            Source = source ?? "",
            Line = line,
            Column = Math.Max(1, col),
            Length = Math.Max(1, len)
        });
    }

    private static string Strip(string line)
    {
        int c = line.IndexOf("//", StringComparison.Ordinal);
        if (c >= 0) line = line[..c];
        return line;
    }

    private static string? Closest(string token)
    {
        string? best = null;
        int bestD = int.MaxValue;
        foreach (var k in KnownSections)
        {
            int d = Levenshtein(token, k);
            if (d < bestD) { bestD = d; best = k; }
        }
        if (best != null && bestD is > 0 and <= 3) return best;
        foreach (var k in KnownSections)
        {
            if (token.StartsWith(k, StringComparison.OrdinalIgnoreCase) && token.Length <= k.Length + 2)
                return k;
        }
        return bestD <= 3 ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
        {
            int c = a[i - 1] == b[j - 1] ? 0 : 1;
            dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + c);
        }
        return dp[a.Length, b.Length];
    }

    private static bool ProtocolAtLeast(string version, string min)
    {
        static double P(string v)
        {
            v = (v ?? "").Trim().Trim('"');
            if (v.StartsWith("ISO", StringComparison.OrdinalIgnoreCase)) return 2.2;
            if (v.StartsWith("J2602", StringComparison.OrdinalIgnoreCase)) return 2.0;
            if (v.StartsWith("OEM", StringComparison.OrdinalIgnoreCase)) return 1.3;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        }
        return P(version) >= P(min);
    }

    private sealed class CollectingErrorListener : BaseErrorListener, IAntlrErrorListener<int>
    {
        private readonly string _code;
        public List<LdfDiagnostic> Diagnostics { get; } = new();

        public CollectingErrorListener(string code) => _code = code;

        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            var token = offendingSymbol?.Text ?? "";
            var suggestion = Closest(token);
            var message = suggestion == null
                ? $"Syntax error near '{token}': {msg}"
                : $"Syntax error near '{token}' (did you mean '{suggestion}'?): {msg}";
            Diagnostics.Add(new LdfDiagnostic
            {
                Code = _code,
                Category = "Syntax",
                Severity = DiagnosticSeverity.Error,
                Message = message,
                Why = suggestion == null
                    ? msg
                    : $"Token '{token}' is not valid here. Closest keyword: '{suggestion}'.",
                Rule = "ANTLR Ldf.g4 / LIN grammar",
                Source = token,
                Line = line,
                Column = charPositionInLine + 1,
                Length = Math.Max(1, token.Length)
            });
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            Diagnostics.Add(new LdfDiagnostic
            {
                Code = _code,
                Category = "Lexer",
                Severity = DiagnosticSeverity.Error,
                Message = $"Lexical error: {msg}",
                Why = msg,
                Rule = "ANTLR Ldf.g4 lexer",
                Line = line,
                Column = charPositionInLine + 1,
                Length = 1
            });
        }
    }
}
