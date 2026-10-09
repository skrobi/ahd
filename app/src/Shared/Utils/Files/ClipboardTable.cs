namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Blok komórek ze schowka w układzie Excela: wiersze rozdzielone końcem linii, komórki – tabulatorem. Komórka
/// w cudzysłowie (Excel tak kopiuje tekst z końcem linii lub cudzysłowem) – bez cudzysłowów, "" → ".
/// Ostatni pusty wiersz (koniec linii na końcu kopii z Excela) jest pomijany.
/// </summary>
public static class ClipboardTable
{
    public static IReadOnlyList<string[]> Parse(string? text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text))
            return rows;
        var row = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                    quoted = false;
                else
                    cell.Append(ch);
            }
            else if (ch == '"' && cell.Length == 0)
                quoted = true;
            else if (ch == '\t')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else
                cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add([.. row]);
        }
        return rows;
    }
}
