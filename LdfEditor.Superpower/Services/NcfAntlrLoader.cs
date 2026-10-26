using LdfEditor.Models;

namespace LdfEditor.Services;

public sealed class NcfAntlrLoader
{
    public NcfFile Load(string path) =>
        throw new NotSupportedException(
            "NCF import is only available in the ANTLR LdfEditor build.");

    public NcfFile LoadText(string text) =>
        throw new NotSupportedException(
            "NCF import is only available in the ANTLR LdfEditor build.");
}
