using System.Globalization;

namespace LdfEditor.Services;

/// <summary>Vector GlobalNumberVisualizationMode — Hex vs Dec for IDs/NADs in UI.</summary>
public static class LdfDisplayFormat
{
    public static bool UseHex { get; set; } = true;

    public static string FormatId(int id) =>
        UseHex
            ? $"0x{id:X}"
            : id.ToString(CultureInfo.InvariantCulture);

    public static string FormatNad(int nad) =>
        UseHex
            ? $"0x{nad:X}"
            : nad.ToString(CultureInfo.InvariantCulture);

    public static string FormatByte(int value) =>
        UseHex
            ? $"0x{value:X}"
            : value.ToString(CultureInfo.InvariantCulture);
}
