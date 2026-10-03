using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Odczyt arkusza Excel (.xlsx, .xlsm) strumieniowo: XmlReader bezpośrednio na części arkusza w archiwum ZIP, wiersz
/// po wierszu, bez budowania modelu dokumentu – szybko i w stałej pamięci także przy milionie wierszy. Wartości jak przy
/// odczycie komórek: tekst bez zmian, liczba w formacie niezmiennym („R”), data RRRR-MM-DD (z godziną, gdy jest), czas
/// trwania ([h]:mm) jako TimeSpan, wartość logiczna TRUE / FALSE. Nagłówek – pierwszy wiersz z treścią; puste wiersze pominięte.
/// </summary>
public static class XlsxStreamReader
{
    private const byte NoDate = 0, IsDate = 1, IsDuration = 2;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,   // odstępy między elementami; tekst w <t xml:space="preserve"> zostaje
        CheckCharacters = false,
        CloseInput = true,
    };

    /// <summary>open – nowy strumień pliku przy każdym odczycie (nagłówek, każde wywołanie Rows()).</summary>
    public static TabularSource Open(Func<Stream> open, string fileType, string? sheetName)
    {
        string sheetPath, name;
        string[] strings;
        byte[] dateStyles;
        bool date1904;
        using (var zip = new ZipArchive(open(), ZipArchiveMode.Read))
        {
            string? Target(IEnumerable<(string Id, string Type, string Target)> relations, Func<(string Id, string Type, string Target), bool> match) =>
                relations.Where(match).Select(r => r.Target).FirstOrDefault();
            var workbookPath = Target(Relationships(zip, "_rels/.rels"), r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal)) is { } root
                ? Resolve("", root)
                : "xl/workbook.xml";
            var (sheets, is1904) = Workbook(zip, workbookPath);
            var relations = Relationships(zip, RelationshipsPath(workbookPath)).ToList();
            var index = sheetName is null ? sheets.Count > 0 ? 0 : -1 : sheets.FindIndex(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidDataException(sheetName is null ? "Plik Excel bez arkuszy" : $"Brak arkusza {sheetName}");
            var sheet = sheets[index];
            sheetPath = Resolve(workbookPath, Target(relations, r => r.Id == sheet.RelationId)
                                              ?? throw new InvalidDataException($"Plik Excel: brak części arkusza {sheet.Name}"));
            name = sheet.Name;
            strings = Target(relations, r => r.Type.EndsWith("/sharedStrings", StringComparison.Ordinal)) is { } stringsPath
                ? SharedStrings(zip, Resolve(workbookPath, stringsPath))
                : [];
            dateStyles = Target(relations, r => r.Type.EndsWith("/styles", StringComparison.Ordinal)) is { } stylesPath
                ? DateStyles(zip, Resolve(workbookPath, stylesPath))
                : [];
            date1904 = is1904;
        }

        IEnumerable<(int Row, string?[] Cells)> Cells()
        {
            using var zip = new ZipArchive(open(), ZipArchiveMode.Read);
            using var xml = XmlReader.Create(Entry(zip, sheetPath)?.Open() ?? throw new InvalidDataException($"Plik Excel: brak części {sheetPath}"), Settings);
            var rowNumber = 0;
            var cells = new List<string?>(64);
            xml.Read();
            while (!xml.EOF)
            {
                if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "row")
                {
                    xml.Read();
                    continue;
                }
                rowNumber = int.TryParse(xml.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : rowNumber + 1;
                cells.Clear();
                if (xml.IsEmptyElement)
                {
                    xml.Read();
                    yield return (rowNumber, []);
                    continue;
                }
                var depth = xml.Depth;
                var column = 0;
                xml.Read();
                while (!xml.EOF && !(xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth))
                {
                    if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "c")
                    {
                        xml.Read();
                        continue;
                    }
                    column = xml.GetAttribute("r") is { } reference ? ColumnNumber(reference) : column + 1;
                    var value = Cell(xml, strings, dateStyles, date1904);
                    while (cells.Count < column - 1)
                        cells.Add(null);
                    cells.Add(value);
                }
                xml.Read();   // koniec wiersza
                yield return (rowNumber, cells.ToArray());
            }
        }

        var header = Cells().FirstOrDefault(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c)));
        if (header.Cells is null)
            return new TabularSource([], fileType, name, null, null, () => []);
        var headers = header.Cells.Select(c => (c ?? "").Trim()).ToList();
        // Wiersz co najmniej tak szeroki jak nagłówek (puste komórki na końcu wiersza nie są zapisane w pliku).
        return new TabularSource(headers, fileType, name, null, null,
            () => Cells().Where(r => r.Row > header.Row && r.Cells.Any(c => !string.IsNullOrWhiteSpace(c)))
                .Select(r => r.Cells.Length >= headers.Count ? r.Cells : [.. r.Cells, .. new string?[headers.Count - r.Cells.Length]]));
    }

    /// <summary>Komórka &lt;c&gt; (czytnik stoi na jej początku) → tekst; czytnik przechodzi za koniec komórki.</summary>
    private static string? Cell(XmlReader xml, string[] strings, byte[] dateStyles, bool date1904)
    {
        var type = xml.GetAttribute("t");
        var style = int.TryParse(xml.GetAttribute("s"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s < dateStyles.Length ? dateStyles[s] : NoDate;
        if (xml.IsEmptyElement)
        {
            xml.Read();
            return null;
        }
        string? raw = null, inline = null;
        var depth = xml.Depth;
        xml.Read();
        while (!xml.EOF && !(xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth))
        {
            if (xml.NodeType != XmlNodeType.Element)
                xml.Read();
            else if (xml.LocalName == "v")
                raw = xml.ReadElementContentAsString();
            else if (xml.LocalName == "is")
                inline = RichText(xml);
            else
                xml.Skip();   // formuła i inne
        }
        xml.Read();
        return Text(type, raw, inline, style, strings, date1904);
    }

    private static string? Text(string? type, string? raw, string? inline, byte style, string[] strings, bool date1904)
    {
        if (type == "inlineStr")
            return inline;
        if (raw is null || raw.Length == 0)
            return null;
        switch (type)
        {
            case "s":
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < strings.Length ? strings[i] : raw;
            case "str" or "e":
                return raw;
            case "b":
                return raw == "1" ? "TRUE" : "FALSE";
            case "d":
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso) ? DateText(iso) : raw;
        }
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return raw;
        if (style == IsDuration)
            return TimeSpan.FromDays(number).ToString("c", CultureInfo.InvariantCulture);
        if (style == IsDate && number is >= -657435 and < 2958466)
            return DateText(DateTime.FromOADate(date1904 ? number + 1462 : number));
        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string DateText(DateTime d) =>
        d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Tekst elementu &lt;si&gt; / &lt;is&gt; (czytnik stoi na jego początku): wszystkie &lt;t&gt; bez wymowy (rPh).</summary>
    private static string RichText(XmlReader xml)
    {
        if (xml.IsEmptyElement)
        {
            xml.Read();
            return "";
        }
        string? single = null;
        StringBuilder? text = null;
        var depth = xml.Depth;
        xml.Read();
        while (!xml.EOF && !(xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth))
        {
            if (xml.NodeType != XmlNodeType.Element)
                xml.Read();
            else if (xml.LocalName == "rPh")
                xml.Skip();
            else if (xml.LocalName == "t")
            {
                var part = xml.ReadElementContentAsString();
                if (single is null && text is null)
                    single = part;
                else
                    (text ??= new StringBuilder(single)).Append(part);
            }
            else
                xml.Read();
        }
        xml.Read();
        return text?.ToString() ?? single ?? "";
    }

    /// <summary>Kolumna z adresu komórki, np. „AB12” → 28.</summary>
    private static int ColumnNumber(string reference)
    {
        var column = 0;
        foreach (var ch in reference)
        {
            if (ch is < 'A' or > 'Z')
                break;
            column = column * 26 + (ch - 'A' + 1);
        }
        return column;
    }

    private static ZipArchiveEntry? Entry(ZipArchive zip, string path) =>
        zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Relacje części pakietu (plik .rels): identyfikator, typ, cel.</summary>
    private static IEnumerable<(string Id, string Type, string Target)> Relationships(ZipArchive zip, string path)
    {
        if (Entry(zip, path) is not { } entry)
            yield break;
        using var xml = XmlReader.Create(entry.Open(), Settings);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "Relationship")
                yield return (xml.GetAttribute("Id") ?? "", xml.GetAttribute("Type") ?? "", xml.GetAttribute("Target") ?? "");
        }
    }

    /// <summary>„xl/workbook.xml” → „xl/_rels/workbook.xml.rels”.</summary>
    private static string RelationshipsPath(string partPath)
    {
        var slash = partPath.LastIndexOf('/');
        return $"{partPath[..(slash + 1)]}_rels/{partPath[(slash + 1)..]}.rels";
    }

    /// <summary>Cel relacji względem części źródłowej (albo bezwzględny „/xl/…”) → ścieżka w archiwum.</summary>
    private static string Resolve(string sourcePath, string target)
    {
        if (target.StartsWith('/'))
            return target.TrimStart('/');
        var parts = sourcePath.Split('/')[..^1].ToList();
        foreach (var segment in target.Split('/'))
        {
            if (segment == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
            }
            else if (segment is not ("." or ""))
                parts.Add(segment);
        }
        return string.Join('/', parts);
    }

    private static (List<(string Name, string RelationId)> Sheets, bool Date1904) Workbook(ZipArchive zip, string path)
    {
        var sheets = new List<(string, string)>();
        var date1904 = false;
        using var xml = XmlReader.Create(Entry(zip, path)?.Open() ?? throw new InvalidDataException("Plik Excel bez skoroszytu"), Settings);
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element)
                continue;
            if (xml.LocalName == "workbookPr")
                date1904 = xml.GetAttribute("date1904") is "1" or "true";
            else if (xml.LocalName == "sheet")
            {
                string? relationId = null;
                for (var i = 0; i < xml.AttributeCount; i++)
                {
                    xml.MoveToAttribute(i);
                    if (xml.LocalName == "id" && xml.NamespaceURI.EndsWith("relationships", StringComparison.Ordinal))
                        relationId = xml.Value;
                }
                xml.MoveToElement();
                sheets.Add((xml.GetAttribute("name") ?? "", relationId ?? ""));
            }
        }
        return (sheets, date1904);
    }

    private static string[] SharedStrings(ZipArchive zip, string path)
    {
        if (Entry(zip, path) is not { } entry)
            return [];
        var strings = new List<string>();
        using var xml = XmlReader.Create(entry.Open(), Settings);
        xml.Read();
        while (!xml.EOF)
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "si")
                strings.Add(RichText(xml));
            else
                xml.Read();
        }
        return strings.ToArray();
    }

    /// <summary>Rodzaj liczby dla każdego stylu komórki (cellXfs): data, czas trwania albo zwykła liczba.</summary>
    private static byte[] DateStyles(ZipArchive zip, string path)
    {
        if (Entry(zip, path) is not { } entry)
            return [];
        var custom = new Dictionary<uint, string>();
        var styles = new List<byte>();
        var inCellFormats = false;
        using var xml = XmlReader.Create(entry.Open(), Settings);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "cellXfs")
                inCellFormats = false;
            if (xml.NodeType != XmlNodeType.Element)
                continue;
            if (xml.LocalName == "numFmt" && uint.TryParse(xml.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                custom[id] = xml.GetAttribute("formatCode") ?? "";
            else if (xml.LocalName == "cellXfs")
                inCellFormats = !xml.IsEmptyElement;
            else if (inCellFormats && xml.LocalName == "xf")
                styles.Add(uint.TryParse(xml.GetAttribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out var format)
                    ? Kind(format, custom.GetValueOrDefault(format))
                    : NoDate);
        }
        return styles.ToArray();
    }

    private static byte Kind(uint id, string? code)
    {
        if (code is null)
            return id switch
            {
                46 => IsDuration,
                >= 14 and <= 22 or >= 27 and <= 36 or 45 or 47 or >= 50 and <= 58 => IsDate,
                _ => NoDate,
            };
        var plain = new StringBuilder();
        var duration = false;
        for (var i = 0; i < code.Length; i++)
        {
            var ch = code[i];
            if (ch == '"')
            {
                i = code.IndexOf('"', i + 1) is var end && end < 0 ? code.Length : end;
                continue;
            }
            if (ch == '\\' || ch == '_' || ch == '*')
            {
                i++;
                continue;
            }
            if (ch == '[')
            {
                var close = code.IndexOf(']', i + 1);
                var inside = close < 0 ? "" : code[(i + 1)..close].ToLowerInvariant();
                duration |= inside is "h" or "hh" or "m" or "mm" or "s" or "ss";
                i = close < 0 ? code.Length : close;
                continue;
            }
            if (ch == ';')
                break;   // tylko pierwsza sekcja formatu
            plain.Append(char.ToLowerInvariant(ch));
        }
        if (duration)
            return IsDuration;
        var text = plain.ToString();
        return text.IndexOfAny(['y', 'd', 'h', 's']) >= 0 || (text.Contains('m') && !text.Contains('0') && !text.Contains('#')) ? IsDate : NoDate;
    }
}
