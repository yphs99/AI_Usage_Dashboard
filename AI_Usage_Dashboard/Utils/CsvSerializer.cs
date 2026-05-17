using System.Text;

namespace AI_Usage_Dashboard.Utils;

public static class CsvSerializer
{
    public static string Serialize(IEnumerable<string[]> headers_and_rows, string[] headers)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in headers_and_rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        return sb.ToString();
    }

    public static async Task WriteFileAsync(string path, string[] headers, IEnumerable<string[]> rows)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));

        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string? value)
    {
        if (value is null) return "\"\"";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
