using System.Text;

namespace MovieLibrary.Core.Text;

public static class CsvParser
{
    public static IReadOnlyList<string[]> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        // The ONE definition of "this row holds something". A row of a single empty field is the
        // blank line between records and is dropped; a single field that is empty because it was
        // written as "" is real data and is kept, which is what fieldStarted records. This
        // predicate used to be spelled out at both flush sites, and that duplication is what
        // drifted and forced Amendment B. Both sites call this now.
        bool RowHasContent() => row.Count > 1 || row[0].Length > 0 || fieldStarted;

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
                    if (RowHasContent()) rows.Add([.. row]);
                    row.Clear(); fieldStarted = false;
                    break;
                default: field.Append(ch); fieldStarted = true; break;
            }
        }

        if (field.Length > 0 || row.Count > 0 || fieldStarted)
        {
            row.Add(field.ToString());
            if (RowHasContent()) rows.Add([.. row]);
        }

        return rows;
    }
}
