using System.IO;
using LdfEditor.Models;
using LdfEditor.Services.Diagnostics;
using LdfEditor.Services.Parsing;

namespace LdfEditor.Services;

/// <summary>
/// Same public API as the ANTLR loader — implementation uses <b>Superpower</b> tokenizer + shared model parser.
/// </summary>
public sealed class LdfAntlrLoader
{
    public LdfDatabase Load(string path)
    {
        var text = File.ReadAllText(path);
        return LoadText(text, path);
    }

    public LdfDatabase LoadText(string text, string path)
    {
        var result = LdfDiagnosticEngine.Analyze(text, path);
        if (result.HasErrors || result.Database == null)
            throw new LdfParseException(
                result.FormatErrorDialog(),
                result.AsLegacyIssues(),
                result.Diagnostics);

        result.Database.FilePath = path ?? "";
        return result.Database;
    }

    internal LdfDatabase LoadTextUnchecked(string text, string path)
    {
        var displayName = string.IsNullOrEmpty(path)
            ? "Untitled"
            : Path.GetFileNameWithoutExtension(path);
        var tokens = LdfSuperpowerTokenizer.Tokenize(text);
        var db = LdfTokenModelParser.Parse(tokens, displayName);
        db.FilePath = path ?? "";
        if (string.IsNullOrEmpty(db.Name)) db.Name = displayName;
        return db;
    }
}
