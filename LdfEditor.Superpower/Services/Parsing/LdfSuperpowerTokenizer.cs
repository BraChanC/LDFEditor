using LdfEditor.Services;
using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace LdfEditor.Services.Parsing;

/// <summary>
/// Superpower front-end: tokenizes with Superpower TokenizerBuilder, maps to shared LdfToken, builds model.
/// </summary>
public static class LdfSuperpowerTokenizer
{
    private enum SpKind
    {
        Ident, Int, Float, String,
        LBrace, RBrace, LParen, RParen, Comma, Colon, Semi, Eq, Minus, Percent
    }

    private static readonly Tokenizer<SpKind> Tokenizer = new TokenizerBuilder<SpKind>()
        .Ignore(Span.WhiteSpace)
        .Ignore(Comment.CStyle)
        .Ignore(Comment.CPlusPlusStyle)
        .Match(Character.EqualTo('{'), SpKind.LBrace)
        .Match(Character.EqualTo('}'), SpKind.RBrace)
        .Match(Character.EqualTo('('), SpKind.LParen)
        .Match(Character.EqualTo(')'), SpKind.RParen)
        .Match(Character.EqualTo(','), SpKind.Comma)
        .Match(Character.EqualTo(':'), SpKind.Colon)
        .Match(Character.EqualTo(';'), SpKind.Semi)
        .Match(Character.EqualTo('='), SpKind.Eq)
        .Match(Character.EqualTo('-'), SpKind.Minus)
        .Match(Character.EqualTo('%'), SpKind.Percent)
        .Match(Span.Regex("0[xX][0-9a-fA-F]+"), SpKind.Int)
        .Match(Span.Regex("[0-9]+\\.[0-9]+"), SpKind.Float)
        .Match(Numerics.Integer, SpKind.Int)
        .Match(Span.Regex("\"([^\"\\\\]|\\\\.)*\""), SpKind.String)
        .Match(Span.Regex("[A-Za-z_][A-Za-z0-9_]*"), SpKind.Ident)
        .Build();

    public static IReadOnlyList<LdfToken> Tokenize(string input)
    {
        var result = Tokenizer.TryTokenize(input);
        if (!result.HasValue)
        {
            throw new LdfParseException(
                "Superpower lexer failed: " + result.ToString(),
                Array.Empty<LdfSectionOrderValidator.Issue>());
        }

        var list = new List<LdfToken>();
        foreach (var t in result.Value)
        {
            var text = t.ToStringValue();
            var line = t.Position.Line;
            var col = t.Position.Column;
            switch (t.Kind)
            {
                case SpKind.Ident:
                    list.Add(new LdfToken(LdfTokKind.Ident, text, line, col));
                    break;
                case SpKind.Percent:
                    list.Add(new LdfToken(LdfTokKind.Ident, "%", line, col));
                    break;
                case SpKind.Int:
                    list.Add(new LdfToken(LdfTokKind.Int, text, line, col));
                    break;
                case SpKind.Float:
                    list.Add(new LdfToken(LdfTokKind.Float, text, line, col));
                    break;
                case SpKind.String:
                    var inner = text.Length >= 2 ? text[1..^1] : text;
                    list.Add(new LdfToken(LdfTokKind.String, inner, line, col));
                    break;
                case SpKind.LBrace: list.Add(new LdfToken(LdfTokKind.LBrace, "{", line, col)); break;
                case SpKind.RBrace: list.Add(new LdfToken(LdfTokKind.RBrace, "}", line, col)); break;
                case SpKind.LParen: list.Add(new LdfToken(LdfTokKind.LParen, "(", line, col)); break;
                case SpKind.RParen: list.Add(new LdfToken(LdfTokKind.RParen, ")", line, col)); break;
                case SpKind.Comma: list.Add(new LdfToken(LdfTokKind.Comma, ",", line, col)); break;
                case SpKind.Colon: list.Add(new LdfToken(LdfTokKind.Colon, ":", line, col)); break;
                case SpKind.Semi: list.Add(new LdfToken(LdfTokKind.Semi, ";", line, col)); break;
                case SpKind.Eq: list.Add(new LdfToken(LdfTokKind.Eq, "=", line, col)); break;
                case SpKind.Minus: list.Add(new LdfToken(LdfTokKind.Minus, "-", line, col)); break;
            }
        }
        list.Add(new LdfToken(LdfTokKind.Eof, "", 0, 0));

        if (list.Count < 2 || list[0].Text != "LIN_description_file")
        {
            throw new LdfParseException(
                "Superpower: expected 'LIN_description_file' as first token.",
                Array.Empty<LdfSectionOrderValidator.Issue>());
        }
        return list;
    }
}
