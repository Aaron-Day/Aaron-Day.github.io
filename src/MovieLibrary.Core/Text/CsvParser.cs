using System.Text;

namespace MovieLibrary.Core.Text;

public static class CsvParser
{
    public static IReadOnlyList<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"': inQuotes = true; fieldStarted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); fieldStarted = false; break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    if (row.Count > 1 || row[0].Length > 0 || fieldStarted) rows.Add([.. row]);
                    row.Clear(); fieldStarted = false;
                    break;
                default: field.Append(ch); fieldStarted = true; break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add([.. row]);
        }

        return rows;
    }
}
