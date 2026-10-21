namespace Lab.Summary;

/// <summary>
/// Reads results.csv as written by <c>RunResult.AppendCsv</c>: a header line, then one
/// comma-separated row per run, no quoting. Anything else is rejected instead of guessed.
/// </summary>
public static class ResultsCsv
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Parse(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var headerIndex = lines.FindIndex(line => line.Length > 0);
        if (headerIndex < 0)
        {
            return [];
        }

        var header = Fields(lines[headerIndex], headerIndex);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            var fields = Fields(lines[i], i);
            if (fields.Length != header.Length)
            {
                throw new FormatException($"results.csv line {i + 1}: {fields.Length} fields, header has {header.Length}.");
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var f = 0; f < header.Length; f++)
            {
                row[header[f]] = fields[f];
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string[] Fields(string line, int index)
    {
        if (line.Contains('"'))
        {
            throw new FormatException($"results.csv line {index + 1}: quoted fields are not supported.");
        }

        return line.Split(',');
    }
}
