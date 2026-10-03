using System.IO;
using System.Text;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Rekordy CSV czytane strumieniowo: separator, pola w cudzysłowach (z "" jako cudzysłowem i znakami nowego wiersza
/// w środku), wiersze zakończone LF albo CRLF. Rekordy nie są gromadzone w pamięci.
/// </summary>
public static class CsvParser
{
    public static IEnumerable<string[]> Read(TextReader reader, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        int ch;
        while ((ch = reader.Read()) >= 0)
        {
            var c = (char)ch;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && reader.Peek() == '\n')
                    reader.Read();
                fields.Add(field.ToString());
                field.Clear();
                yield return fields.ToArray();
                fields.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }
}
