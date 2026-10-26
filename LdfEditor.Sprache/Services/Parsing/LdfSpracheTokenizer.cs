using LdfEditor.Services;
using Sprache;

namespace LdfEditor.Services.Parsing;

/// <summary>
/// Sprache front-end: validates LDF header with Sprache combinators, then lexes + builds model.
/// </summary>
public static class LdfSpracheTokenizer
{
    private static readonly Parser<string> Header =
        from ws in Parse.WhiteSpace.Many()
        from kw in Parse.String("LIN_description_file").Text()
        from ws2 in Parse.WhiteSpace.Many()
        from semi in Parse.Char(';')
        select kw;

    public static IReadOnlyList<LdfToken> Tokenize(string input)
    {
        /* Strip leading comments for header check */
        var trimmed = StripLeadingComments(input);
        try
        {
            Header.Parse(trimmed);
        }
        catch (ParseException ex)
        {
            throw new LdfParseException(
                "Sprache: expected 'LIN_description_file;' at start of LDF. " + ex.Message,
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
