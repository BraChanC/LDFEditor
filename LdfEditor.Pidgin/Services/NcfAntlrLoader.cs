using LdfEditor.Models;

namespace LdfEditor.Services;

/// <summary>NCF remains ANTLR-only in the original project; stubbed here.</summary>
public sealed class NcfAntlrLoader
{
    public NcfFile Load(string path) =>
        throw new NotSupportedException(
            "NCF import is only available in the ANTLR LdfEditor build. Use LdfEditor for NCF.");

    public NcfFile LoadText(string text) =>
        throw new NotSupportedException(
            "NCF import is only available in the ANTLR LdfEditor build.");
}
