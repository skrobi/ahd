using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>Parser według definicji parsera – układ kolumn, typy, wymagane, dopełnianie zerami, długość (bez bazy).</summary>
public sealed class MappedParserTests
{
    private static readonly ParserRow Parser = new(1, 1, 3, "TEST", "test", "Test",
    [
        new ParserField("Wbs", "WBS Element", FieldTypes.Text, 20, Required: true),
        new ParserField("Cost", "Cost Element", FieldTypes.Text, 10, PadDigits: 10),
        new ParserField("Amount", "Value", FieldTypes.Decimal),
        new ParserField("Year", "fiscal year", FieldTypes.Integer, Required: true),   // nazwa kolumny bez rozróżniania wielkości liter
        new ParserField("Created", "Created on", FieldTypes.Date),
        new ParserField("Invoice", "Invoice Number", FieldTypes.Text, 5),
        new ParserField("Note", "", FieldTypes.Text, 50),                              // pole spoza pliku – zostaje puste
    ], true, DateTimeOffset.Now, "test", null, null);

    private static readonly string[] Headers = ["Value", "WBS Element", "Cost Element", "Fiscal Year", "Created on", "Invoice Number", "Unused"];

    [Fact]
    public void Values_go_to_parser_fields_with_types_and_other_columns_stay_raw_only()
    {
        var result = MappedParser.Parse(Parser, Headers, [["1 234,50-", " WBS1 ", "51105550", "2026", "2026-03-29", "F1", "x"]]);

        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(["Wbs", "Cost", "Amount", "Year", "Created", "Invoice"], result.Fields.Select(f => f.Field));
        Assert.Equal(["Unused"], result.ExtraColumns);
        Assert.Empty(result.MissingColumns);
        var row = Assert.Single(result.Rows);
        Assert.Equal(1, row.RowNumber);
        Assert.Equal(["WBS1", "0051105550", -1234.50m, 2026, new DateTime(2026, 3, 29), "F1"], row.Values);
    }

    [Fact]
    public void Empty_optional_field_is_null_and_empty_required_field_is_an_error_with_row_number()
    {
        var result = MappedParser.Parse(Parser, Headers,
        [
            ["", "WBS1", "", "2026", "", "", ""],
            ["10", "", "1", "2026", "", "", ""],
        ]);

        Assert.Equal(1, result.ErrorCount);
        Assert.Empty(result.Rows);   // plik z błędem nie daje danych kanonicznych
        var issue = Assert.Single(result.Issues);
        Assert.Equal("WBS Element: pole wymagane", issue.Message);
        Assert.Equal("wiersz danych 2", issue.Element);
    }

    [Fact]
    public void Wrong_values_and_too_long_text_are_errors()
    {
        var result = MappedParser.Parse(Parser, Headers, [["abc", "WBS1", "1", "rok", "jutro", "FAKTURA-123", ""]]);

        Assert.Equal(
            ["Value: 'abc' – oczekiwano liczby", "Fiscal Year: 'rok' – oczekiwano liczby całkowitej", "Created on: 'jutro' – oczekiwano daty",
             "Invoice Number: tekst dłuższy niż 5 znaków (11)"],
            result.Issues.Select(i => i.Message));
        Assert.Equal(4, result.ErrorCount);
    }

    [Fact]
    public void Missing_parser_column_in_file_is_a_layout_error()
    {
        var result = MappedParser.Parse(Parser, ["WBS Element", "Cost Element", "Created on"], [["W", "1", ""]]);

        Assert.Equal(["Value", "fiscal year", "Invoice Number"], result.MissingColumns);
        Assert.Equal("Brak kolumn parsera TEST: Value, fiscal year, Invoice Number", Assert.Single(result.Issues).Message);
        Assert.Empty(result.Rows);
    }
}
