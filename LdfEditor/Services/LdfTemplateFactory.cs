namespace LdfEditor.Services;

/// <summary>Compatibility shim — New File uses <see cref="LdfDefaultModelCreator"/>.</summary>
public static class LdfTemplateFactory
{
    public static IReadOnlyList<(string MenuLabel, string DialectKey)> Dialects =>
        LdfDefaultModelCreator.Dialects;

    public static string Create(string dialectKey) =>
        LdfWriter.Write(LdfDefaultModelCreator.Create(dialectKey));
}
