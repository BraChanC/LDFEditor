namespace LdfEditor.Services.Parsing;

public enum LdfTokKind
{
    Ident,
    Int,
    Float,
    String,
    Kw,          /* reserved word kept as Ident text; Kind=Ident is fine */
    LBrace,
    RBrace,
    LParen,
    RParen,
    Comma,
    Colon,
    Semi,
    Eq,
    Minus,
    Eof
}

public readonly struct LdfToken
{
    public LdfTokKind Kind { get; }
    public string Text { get; }
    public int Line { get; }
    public int Column { get; }

    public LdfToken(LdfTokKind kind, string text, int line, int column)
    {
        Kind = kind;
        Text = text;
        Line = line;
        Column = column;
    }

    public override string ToString() => $"{Kind}:{Text}@{Line}:{Column}";
}
