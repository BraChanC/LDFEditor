using System.IO;

namespace LdfEditor.Services;

public sealed class RecentFilesStore
{
    private const int MaxCount = 8;
    private readonly string _path;

    public RecentFilesStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LdfEditor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "recent.txt");
    }

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(_path)) return Array.Empty<string>();
        return File.ReadAllLines(_path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && File.Exists(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxCount)
            .ToList();
    }

    public IReadOnlyList<string> Add(string filePath)
    {
        var list = Load().ToList();
        list.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, filePath);
        while (list.Count > MaxCount) list.RemoveAt(list.Count - 1);
        File.WriteAllLines(_path, list);
        return list;
    }

    public static string FormatDisplay(string path, int maxLen = 56)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLen) return path;
        var file = Path.GetFileName(path);
        var prefixLen = Math.Max(8, maxLen - file.Length - 5);
        if (prefixLen + 5 + file.Length > path.Length) return path;
        return path[..prefixLen] + "..." + file;
    }
}
