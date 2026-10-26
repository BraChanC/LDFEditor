using LdfEditor.Services;

namespace LdfEditor.Services.Parsing;

/// <summary>Shared character lexer producing <see cref="LdfToken"/> stream for the model parser.</summary>
public static class LdfHandLexer
{
    public static IReadOnlyList<LdfToken> Tokenize(string input)
    {
        var list = new List<LdfToken>();
        int i = 0, line = 1, col = 1;
        while (i < input.Length)
        {
            SkipTrivia(input, ref i, ref line, ref col);
            if (i >= input.Length) break;
            int sl = line, sc = col;
            char c = input[i];
            if (c is '{' or '}' or '(' or ')' or ',' or ':' or ';' or '=' or '-')
            {
                list.Add(new LdfToken(c switch
                {
                    '{' => LdfTokKind.LBrace,
                    '}' => LdfTokKind.RBrace,
                    '(' => LdfTokKind.LParen,
                    ')' => LdfTokKind.RParen,
                    ',' => LdfTokKind.Comma,
                    ':' => LdfTokKind.Colon,
                    ';' => LdfTokKind.Semi,
                    '=' => LdfTokKind.Eq,
                    _ => LdfTokKind.Minus
                }, c.ToString(), sl, sc));
                i++; col++;
                continue;
            }
            if (c == '%')
            {
                list.Add(new LdfToken(LdfTokKind.Ident, "%", sl, sc));
                i++; col++;
                continue;
            }
            if (c == '"')
            {
                i++; col++;
                var sb = new System.Text.StringBuilder();
                while (i < input.Length && input[i] != '"')
                {
                    if (input[i] == '\\' && i + 1 < input.Length) { sb.Append(input[i + 1]); i += 2; col += 2; }
                    else { sb.Append(input[i]); i++; col++; }
                }
                if (i < input.Length) { i++; col++; }
                list.Add(new LdfToken(LdfTokKind.String, sb.ToString(), sl, sc));
                continue;
            }
            if (c == '0' && i + 1 < input.Length && (input[i + 1] is 'x' or 'X'))
            {
                int j = i + 2;
                while (j < input.Length && Uri.IsHexDigit(input[j])) j++;
                var text = input[i..j];
                Advance(input, ref i, ref line, ref col, j - i);
                list.Add(new LdfToken(LdfTokKind.Int, text, sl, sc));
                continue;
            }
            if (char.IsDigit(c))
            {
                int j = i;
                while (j < input.Length && char.IsDigit(input[j])) j++;
                bool isFloat = j < input.Length && input[j] == '.';
                if (isFloat)
                {
                    j++;
                    while (j < input.Length && char.IsDigit(input[j])) j++;
                }
                var text = input[i..j];
                Advance(input, ref i, ref line, ref col, j - i);
                list.Add(new LdfToken(isFloat ? LdfTokKind.Float : LdfTokKind.Int, text, sl, sc));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int j = i + 1;
                while (j < input.Length && (char.IsLetterOrDigit(input[j]) || input[j] == '_')) j++;
                var text = input[i..j];
                Advance(input, ref i, ref line, ref col, j - i);
                list.Add(new LdfToken(LdfTokKind.Ident, text, sl, sc));
                continue;
            }
            throw new LdfParseException(
                $"Unexpected character '{c}' at line {line}, col {col}",
                Array.Empty<LdfSectionOrderValidator.Issue>());
        }
        list.Add(new LdfToken(LdfTokKind.Eof, "", line, col));
        return list;
    }

    private static void Advance(string input, ref int i, ref int line, ref int col, int n)
    {
        for (int k = 0; k < n; k++)
        {
            if (input[i] == '\n') { line++; col = 1; }
            else col++;
            i++;
        }
    }

    private static void SkipTrivia(string input, ref int i, ref int line, ref int col)
    {
        while (i < input.Length)
        {
            char c = input[i];
            if (c is ' ' or '\t' or '\r') { i++; col++; continue; }
            if (c == '\n') { i++; line++; col = 1; continue; }
            if (c == '/' && i + 1 < input.Length && input[i + 1] == '/')
            {
                i += 2; col += 2;
                while (i < input.Length && input[i] != '\n') { i++; col++; }
                continue;
            }
            if (c == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                i += 2; col += 2;
                while (i + 1 < input.Length && !(input[i] == '*' && input[i + 1] == '/'))
                {
                    if (input[i] == '\n') { line++; col = 1; } else col++;
                    i++;
                }
                if (i + 1 < input.Length) { i += 2; col += 2; }
                continue;
            }
            break;
        }
    }
}
