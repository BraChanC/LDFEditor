using System.Text;

namespace LdfEditor.Services.Diagnostics;

/// <summary>
/// Detect full-width / CJK punctuation (中英文标点混用) and produce an ASCII-normalized
/// copy so later analyzers can continue (VS-style: report + recover).
/// Replacement chars are 1:1 so line/column stay aligned.
/// </summary>
public static class LdfTextNormalizer
{
    private static readonly Dictionary<char, (char Ascii, string Name)> Map = new()
    {
        ['；'] = (';', "full-width semicolon ；"),
        ['，'] = (',', "full-width comma ，"),
        ['：'] = (':', "full-width colon ："),
        ['．'] = ('.', "full-width full stop ．"),
        ['。'] = ('.', "ideographic full stop 。"),
        ['｛'] = ('{', "full-width left brace ｛"),
        ['｝'] = ('}', "full-width right brace ｝"),
        ['（'] = ('(', "full-width left paren （"),
        ['）'] = (')', "full-width right paren ）"),
        ['［'] = ('[', "full-width left bracket ［"),
        ['］'] = (']', "full-width right bracket ］"),
        ['＝'] = ('=', "full-width equals ＝"),
        ['＋'] = ('+', "full-width plus ＋"),
        ['－'] = ('-', "full-width hyphen-minus －"),
        ['／'] = ('/', "full-width solidus ／"),
        ['＊'] = ('*', "full-width asterisk ＊"),
        ['％'] = ('%', "full-width percent ％"),
        ['＂'] = ('"', "full-width quotation mark ＂"),
        ['＇'] = ('\'', "full-width apostrophe ＇"),
        ['“'] = ('"', "left double quotation “"),
        ['”'] = ('"', "right double quotation ”"),
        ['‘'] = ('\'', "left single quotation ‘"),
        ['’'] = ('\'', "right single quotation ’"),
        ['　'] = (' ', "ideographic space 　"),
        ['\u00A0'] = (' ', "non-breaking space"),
    };

    public sealed class NormalizeResult
    {
        public string NormalizedText { get; init; } = "";
        public IReadOnlyList<LdfDiagnostic> Diagnostics { get; init; } = Array.Empty<LdfDiagnostic>();
        public bool HadReplacements => Diagnostics.Count > 0;
    }

    public static NormalizeResult Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new NormalizeResult { NormalizedText = text ?? "" };

        var diags = new List<LdfDiagnostic>();
        var sb = new StringBuilder(text.Length);
        int line = 1, col = 1;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append(c);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    sb.Append('\n');
                    i++;
                }
                line++;
                col = 1;
                continue;
            }
            if (c == '\n')
            {
                sb.Append(c);
                line++;
                col = 1;
                continue;
            }

            if (Map.TryGetValue(c, out var repl))
            {
                diags.Add(new LdfDiagnostic
                {
                    Code = "LDF0005",
                    Category = "Punctuation / Locale",
                    Severity = DiagnosticSeverity.Error,
                    Message = $"Non-ASCII punctuation '{c}' — use ASCII '{repl.Ascii}' instead",
                    Why = $"Found {repl.Name} at line {line}, column {col}. " +
                          "LDF / VLDFParser expects ASCII punctuation only " +
                          "(; , : {{ }} ( ) = \" …). Full-width or Chinese punctuation is rejected.",
                    Rule = "LIN LDF text encoding — ASCII punctuation (ISO 8859-1 / UTF-8 ASCII subset)",
                    Source = c.ToString(),
                    Line = line,
                    Column = col,
                    Length = 1
                });
                sb.Append(repl.Ascii);
            }
            else if (c > 0x7F && IsSuspiciousNonAscii(c))
            {
                /* Other CJK / fullwidth letters inside code (not in strings) — hint only later;
                   here only flag fullwidth ASCII block FF01-FF5E not already mapped. */
                if (c is >= '\uFF01' and <= '\uFF5E')
                {
                    char ascii = (char)(c - 0xFEE0);
                    diags.Add(new LdfDiagnostic
                    {
                        Code = "LDF0005",
                        Category = "Punctuation / Locale",
                        Severity = DiagnosticSeverity.Error,
                        Message = $"Full-width character '{c}' (U+{(int)c:X4}) — use ASCII '{ascii}'",
                        Why = "Full-width Latin/punctuation from IME must be converted to half-width ASCII for LDF.",
                        Rule = "LIN LDF text encoding — ASCII only for syntax",
                        Source = c.ToString(),
                        Line = line,
                        Column = col,
                        Length = 1
                    });
                    sb.Append(ascii);
                }
                else
                    sb.Append(c);
            }
            else
                sb.Append(c);

            col++;
        }

        return new NormalizeResult
        {
            NormalizedText = sb.ToString(),
            Diagnostics = diags
        };
    }

    private static bool IsSuspiciousNonAscii(char c) =>
        char.GetUnicodeCategory(c) is
            System.Globalization.UnicodeCategory.OtherPunctuation or
            System.Globalization.UnicodeCategory.DashPunctuation or
            System.Globalization.UnicodeCategory.OpenPunctuation or
            System.Globalization.UnicodeCategory.ClosePunctuation or
            System.Globalization.UnicodeCategory.InitialQuotePunctuation or
            System.Globalization.UnicodeCategory.FinalQuotePunctuation or
            System.Globalization.UnicodeCategory.SpaceSeparator;
}
