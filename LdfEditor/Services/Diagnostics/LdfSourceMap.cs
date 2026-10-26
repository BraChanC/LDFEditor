using System.Text.RegularExpressions;

namespace LdfEditor.Services.Diagnostics;

/// <summary>Map identifier / section occurrences to 1-based line/column (for semantic diags).</summary>
public sealed class LdfSourceMap
{
    private readonly string _text;
    private readonly string[] _lines;

    public LdfSourceMap(string text)
    {
        _text = text ?? "";
        _lines = _text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    public string[] Lines => _lines;
    public string Text => _text;

    public (int Line, int Column, int Length)? Find(string token, int startLine = 1)
    {
        if (string.IsNullOrEmpty(token)) return null;
        for (int i = Math.Max(0, startLine - 1); i < _lines.Length; i++)
        {
            var line = _lines[i];
            int idx = IndexOfWord(line, token);
            if (idx >= 0)
                return (i + 1, idx + 1, token.Length);
        }
        return null;
    }

    public (int Line, int Column, int Length)? FindSection(string sectionName)
    {
        var re = new Regex(@"^\s*" + Regex.Escape(sectionName) + @"\b", RegexOptions.CultureInvariant);
        for (int i = 0; i < _lines.Length; i++)
        {
            var m = re.Match(StripLineComment(_lines[i]));
            if (m.Success)
                return (i + 1, m.Index + 1, sectionName.Length);
        }
        return null;
    }

    private static int IndexOfWord(string line, string word)
    {
        int start = 0;
        while (start < line.Length)
        {
            int idx = line.IndexOf(word, start, StringComparison.Ordinal);
            if (idx < 0) return -1;
            bool leftOk = idx == 0 || !IsIdentChar(line[idx - 1]);
            int end = idx + word.Length;
            bool rightOk = end >= line.Length || !IsIdentChar(line[end]);
            if (leftOk && rightOk) return idx;
            start = idx + 1;
        }
        return -1;
    }

    private static bool IsIdentChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static string StripLineComment(string line)
    {
        int i = line.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }
}
