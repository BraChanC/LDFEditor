using LdfEditor.Services;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace LdfEditor.Services.Parsing;

/// <summary>
/// Pidgin front-end: validates LDF header with Pidgin combinators, then lexes + builds model.
/// </summary>
public static class LdfPidginTokenizer
{
    private static readonly Parser<char, string> HeaderKeyword =
        SkipWhitespaces
            .Then(String("LIN_description_file"))
            .Before(SkipWhitespaces)
            .Before(Char(';'));

    public static IReadOnlyList<LdfToken> Tokenize(string input)
    {
        var stripped = StripLeadingComments(input);
        var header = HeaderKeyword.Parse(stripped);
        if (!header.Success)
        {
            throw new LdfParseException(
                "Pidgin: expected 'LIN_description_file;' at start of LDF. " +
                (header.Error?.ToString() ?? ""),
                Array.Empty<LdfSectionOrderValidator.Issue>());
        }
        return LdfHandLexer.Tokenize(input);
    }

    private static string StripLeadingComments(string input)
    {
        int i = 0;
        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
            if (i + 1 < input.Length && input[i] == '/' && input[i + 1] == '/')
            {
                i += 2;
                while (i < input.Length && input[i] != '\n') i++;
                continue;
            }
            if (i + 1 < input.Length && input[i] == '/' && input[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < input.Length && !(input[i] == '*' && input[i + 1] == '/')) i++;
                if (i + 1 < input.Length) i += 2;
                continue;
            }
            break;
        }
        return input[i..];
    }
}
