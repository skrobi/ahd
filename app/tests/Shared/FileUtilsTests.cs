using System.Text;
using PzlEv.Shared.Utils.Files;
using Xunit;

namespace PzlEv.Tests.Shared;

public class FileUtilsTests
{
    [Theory]
    [InlineData("1 254,51", "1254.51")]
    [InlineData("1\u00A0254,51", "1254.51")]
    [InlineData("261.54", "261.54")]
    [InlineData("-12,5", "-12.5")]
    [InlineData("12,50-", "-12.5")]
    [InlineData("0,000", "0")]
    [InlineData("1.234.567,8", "1234567.8")]
    [InlineData("1E-05", "0.00001")]
    [InlineData("1,234.56", "1234.56")]       // zapis angielski (Excel EN, dane z USA)
    [InlineData("1,234,567", "1234567")]
    [InlineData("1.234.567", "1234567")]
    [InlineData("1,5", "1.5")]
    public void Polish_numbers_are_parsed(string text, string expected)
    {
        Assert.True(PolishNumber.TryParse(text, out var value));
        Assert.Equal(expected, PolishNumber.ToCanonical(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12,5x")]
    public void Invalid_numbers_are_rejected(string text) => Assert.False(PolishNumber.TryParse(text, out _));

    [Fact]
    public void Header_signature_is_stable()
    {
        // Wartości wzorcowe – sygnatury zapisane już w bazie (meta.SourceFile) muszą zostać porównywalne.
        string[] actuals =
        [
            "Project Definition", "WBS Element", "Cost Element", "Cost element descr.", "Cost element name", "CO object name",
            "Transaction Currency", "Value TranCurr", "Object Currency", "Value in Obj. Crcy", "Report currency", "Val.in rep.cur.",
            "Total Quantity", "Partner-CCtr", "Source object name", "Partner Object Class", "Partner object", "Original material",
            "Original material description", "Fiscal Year", "Created on", "Period",
        ];
        Assert.Equal("7c59f446fe9c3d5e", HeaderSignature.Compute(actuals));
        Assert.Equal("850f23ed0adb20a8", HeaderSignature.Compute(["Kod", "Opis Ź", " Kwota "]));
    }

    [Fact]
    public void Csv_in_cp1250_with_semicolons_and_quotes_is_read()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = "Kod;Opis;Kwota\r\nA1;\"Zażółć; gęślą\";\"1 254,51\"\r\n\r\nA2;\"cytat \"\"x\"\"\";-3,5\r\n";
        var bytes = Encoding.GetEncoding(1250).GetBytes(text);

        var data = TabularFileReader.Read(bytes, "plik.csv");

        Assert.Equal(["Kod", "Opis", "Kwota"], data.Headers);
        Assert.Equal("cp1250", data.Encoding);
        Assert.Equal(";", data.Delimiter);
        Assert.Equal(2, data.Rows.Count);
        Assert.Equal("Zażółć; gęślą", data.Rows[0][1]);
        Assert.Equal("cytat \"x\"", data.Rows[1][1]);
    }

    [Fact]
    public void Tab_separated_utf8_with_bom_is_read()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("A\tB\n1\t2\n")).ToArray();
        var data = TabularFileReader.Read(bytes, "plik.txt");
        Assert.Equal(["A", "B"], data.Headers);
        Assert.Equal("TAB", data.Delimiter);
        Assert.Equal("utf-8", data.Encoding);
        Assert.Equal("2", Assert.Single(data.Rows)[1]);
    }

    [Fact]
    public void Excel_write_and_read_keeps_leading_zeros_and_types()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pzl-ev-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelTableWriter.Write(path, "Cost Category", ["Numer", "Kwota", "Data"],
            [
                ["0051105550", 1254.51m, new DateOnly(2026, 3, 29)],
                ["9221X550", null, null],
            ]);

            var data = TabularFileReader.Read(path);

            Assert.Equal("Cost Category", data.Sheet);
            Assert.Equal(["Numer", "Kwota", "Data"], data.Headers);
            Assert.Equal("0051105550", data.Rows[0][0]);
            Assert.Equal("1254.51", data.Rows[0][1]);
            Assert.Equal("2026-03-29", data.Rows[0][2]);
            Assert.Equal("9221X550", data.Rows[1][0]);
            Assert.Null(data.Rows[1][1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Excel_is_read_row_by_row_with_header_in_first_used_row()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pzl-ev-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Dane");
                sheet.Cell(3, 2).Value = "Kod";   // nagłówek w 3. wierszu, od kolumny B
                sheet.Cell(3, 3).Value = "Opis";
                sheet.Cell(3, 4).Value = "Data";
                sheet.Cell(3, 5).Value = "Czas";
                for (var i = 0; i < 5000; i++)
                {
                    var row = 4 + i + (i >= 2500 ? 1 : 0);   // pusty wiersz w środku
                    sheet.Cell(row, 2).Value = $"{i:00000}";
                    if (i % 2 == 0)
                        sheet.Cell(row, 3).Value = $"opis {i}";   // co drugi wiersz bez opisu i dalszych komórek
                }
                sheet.Cell(4, 4).Value = new DateTime(2026, 3, 29);
                sheet.Cell(4, 4).Style.NumberFormat.Format = "dd.mm.yyyy";
                sheet.Cell(4, 5).Value = TimeSpan.FromHours(30);
                sheet.Cell(4, 5).Style.NumberFormat.Format = "[h]:mm";
                workbook.SaveAs(path);
            }

            var source = TabularFileReader.Open(File.ReadAllBytes(path), "plik.xlsx");
            var rows = source.Rows().ToList();

            Assert.Equal(("Dane", "xlsx"), (source.Sheet, source.FileType));
            Assert.Equal(["", "Kod", "Opis", "Data", "Czas"], source.Headers);
            Assert.Equal(5000, rows.Count);
            Assert.Equal(Enumerable.Range(0, 5000).Select(i => $"{i:00000}"), rows.Select(r => r[1]));
            Assert.All(rows, r => Assert.True(r.Length >= source.Headers.Count));   // puste komórki na końcu wiersza – null
            Assert.Equal(new string?[] { null, "00000", "opis 0", "2026-03-29", "1.06:00:00" }, rows[0]);
            Assert.Equal(new string?[] { null, "00001", null, null, null }, rows[1]);
            Assert.Equal(5000, source.Rows().Count());   // każde wywołanie czyta plik od początku
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Excel_parts_are_read_by_relationships_with_rich_text_inline_text_booleans_and_1904_dates()
    {
        // Skoroszyt zapisany „ręcznie”: niestandardowe nazwy części, tekst sformatowany z wymową (rPh), tekst w komórce,
        // wartość logiczna, wynik formuły, data w systemie 1904, drugi arkusz wybrany po nazwie.
        using var content = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(content, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Part(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
            const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Part("_rels/.rels", $"""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="{rel}/officeDocument" Target="/excel/book.xml"/></Relationships>""");
            Part("excel/_rels/book.xml.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="a" Type="{rel}/worksheet" Target="sheets/one.xml"/>
                  <Relationship Id="b" Type="{rel}/worksheet" Target="sheets/two.xml"/>
                  <Relationship Id="s" Type="{rel}/sharedStrings" Target="text.xml"/>
                  <Relationship Id="t" Type="{rel}/styles" Target="/excel/style.xml"/>
                </Relationships>
                """);
            Part("excel/book.xml", $"""<workbook xmlns="{main}" xmlns:r="{rel}"><workbookPr date1904="1"/><sheets><sheet name="Pierwszy" sheetId="1" r:id="a"/><sheet name="Dane" sheetId="2" r:id="b"/></sheets></workbook>""");
            Part("excel/text.xml", $"""<sst xmlns="{main}"><si><t>Kod</t></si><si><r><t>Zażółć </t></r><r><rPr><b/></rPr><t>gęślą</t></r><rPh sb="0" eb="1"><t>X</t></rPh></si></sst>""");
            Part("excel/style.xml", $"""<styleSheet xmlns="{main}"><numFmts><numFmt numFmtId="164" formatCode="yyyy\-mm\-dd"/></numFmts><cellStyleXfs><xf numFmtId="164"/></cellStyleXfs><cellXfs><xf numFmtId="0"/><xf numFmtId="164"/></cellXfs></styleSheet>""");
            Part("excel/sheets/one.xml", $"""<worksheet xmlns="{main}"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>inny</t></is></c></row></sheetData></worksheet>""");
            Part("excel/sheets/two.xml", $"""
                <worksheet xmlns="{main}"><sheetData>
                  <row r="2"><c r="B2" t="s"><v>0</v></c><c r="C2" t="inlineStr"><is><t>Opis</t></is></c><c r="D2" t="s"><v>0</v></c><c r="E2" t="inlineStr"><is><t>Data</t></is></c></row>
                  <row r="3"><c r="B3"><v>7</v></c><c r="C3" t="s"><v>1</v></c><c r="D3" t="b"><v>1</v></c><c r="E3" s="1"><v>45379</v></c></row>
                  <row r="4"><c r="B4" t="str"><f>A1&amp;"x"</f><v>wynik</v></c><c r="C4" t="inlineStr"><is><r><t xml:space="preserve"> a </t></r><r><t>b</t></r></is></c><c r="D4" t="e"><v>#N/A</v></c></row>
                </sheetData></worksheet>
                """);
        }

        var data = TabularFileReader.Read(content.ToArray(), "plik.xlsx", "dane");

        Assert.Equal("Dane", data.Sheet);
        Assert.Equal(["", "Kod", "Opis", "Kod", "Data"], data.Headers);
        Assert.Equal(new string?[] { null, "7", "Zażółć gęślą", "TRUE", "2028-03-29" }, data.Rows[0]);   // 45379 w systemie 1904 = 2028-03-29
        Assert.Equal(new string?[] { null, "wynik", " a b", "#N/A", null }, data.Rows[1]);
        Assert.Equal("inny", TabularFileReader.Read(content.ToArray(), "plik.xlsx").Headers.Single());
        Assert.Throws<InvalidDataException>(() => TabularFileReader.Read(content.ToArray(), "plik.xlsx", "Brak"));
    }

    [Fact]
    public void Csv_is_read_row_by_row_and_each_reading_starts_from_beginning()
    {
        var text = new StringBuilder("Kod;Opis;Kwota\n");
        for (var i = 0; i < 20000; i++)
            text.Append(i % 2 == 0 ? $"{i};\"opis\n{i}\";{i},5\n" : $"{i};;\n");   // wartość w cudzysłowie z nową linią

        var source = TabularFileReader.Open(Encoding.UTF8.GetBytes(text.ToString()), "plik.csv");
        var rows = source.Rows().ToList();

        Assert.Equal(["Kod", "Opis", "Kwota"], source.Headers);
        Assert.Equal(20000, rows.Count);
        Assert.Equal(new string?[] { "0", "opis\n0", "0,5" }, rows[0]);
        Assert.Equal(new string?[] { "1", null, null }, rows[1]);
        Assert.Equal(20000, source.Rows().Count());
    }

    [Theory]
    [InlineData("2026-03-29")]
    [InlineData("2026-03-29 00:00:00")]
    [InlineData("29.03.2026")]
    public void Dates_are_parsed(string text)
    {
        Assert.True(DateText.TryParse(text, out var date));
        Assert.Equal(new DateOnly(2026, 3, 29), date);
    }

    [Theory]
    [InlineData("ACTUALS_PAF2_B6_AC1.xlsx\0", "ACTUALS_PAF2_B6_AC1.xlsx")]   // nazwa z WebDAV w .NET (dotnet/runtime#62429)
    [InlineData("ACTUALS_PAF2_B6_AC1.xlsx", "ACTUALS_PAF2_B6_AC1.xlsx")]
    [InlineData(".\0", ".")]
    public void Folder_entry_names_lose_trailing_null(string name, string expected) => Assert.Equal(expected, FolderEntries.CleanName(name));

    [Fact]
    public void Folder_entries_list_files_and_subfolders_sorted()
    {
        var root = Directory.CreateTempSubdirectory("pzl-ev-folder-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "b.csv"), "x");
            File.WriteAllText(Path.Combine(root, "A.xlsx"), "x");
            Directory.CreateDirectory(Path.Combine(root, "Archiwum"));

            var (files, folders) = FolderEntries.List(root);

            Assert.Equal(["A.xlsx", "b.csv"], files.Select(f => f.Name));
            Assert.Equal(["Archiwum"], folders);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
