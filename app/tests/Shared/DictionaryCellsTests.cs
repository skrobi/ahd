using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Tabela słownika jak w Excelu: sprawdzenie komórki, kolumna powiązana (Osoby), wklejanie bloku ze schowka.</summary>
public sealed class DictionaryCellsTests
{
    private static readonly DictionarySpec Spec = new()
    {
        Code = "test",
        Name = "Test",
        Description = "",
        Columns =
        [
            new("Element", ColumnType.Text, Key: true),
            new("CAM", ColumnType.Text, Required: true, Lookup: GlobalDictionaries.Persons),
            new("Kategoria", ColumnType.Choice, Choices: ["Labor", "Material"]),
            new("BAC", ColumnType.Decimal),
        ],
    };

    private static readonly IReadOnlyList<LookupOption> Persons =
    [
        new("e123456", "Anna Nowak"),
        new("e555111", "Jan Nowicki"),
        new("e777000", "Jan Kowalski"),
        new("e777001", "Jan Kowalski"),
    ];

    private static DictColumn Column(string name) => Spec.Columns.Single(c => c.Name == name);

    [Fact]
    public void Cell_problems_are_found_while_typing()
    {
        Assert.Null(DictionaryCells.Check(Spec, Column("BAC"), "1 250,5", null));
        Assert.Equal(CheckLevel.Error, DictionaryCells.Check(Spec, Column("BAC"), "abc", null)!.Level);
        Assert.Equal(CheckLevel.Error, DictionaryCells.Check(Spec, Column("Kategoria"), "Inne", null)!.Level);
        Assert.Null(DictionaryCells.Check(Spec, Column("Kategoria"), "labor", null));
        Assert.Contains("wymagane", DictionaryCells.Check(Spec, Column("Element"), " ", null)!.Message);
        Assert.Contains("wymagane", DictionaryCells.Check(Spec, Column("CAM"), null, Persons)!.Message);

        // Kolumna powiązana: USRID spoza słownika Osoby – ostrzeżenie; bez wczytanej listy – bez sprawdzania.
        Assert.Null(DictionaryCells.Check(Spec, Column("CAM"), "E123456", Persons));
        var unknown = DictionaryCells.Check(Spec, Column("CAM"), "x999", Persons)!;
        Assert.Equal(CheckLevel.Warning, unknown.Level);
        Assert.Contains("Osoby", unknown.Message);
        Assert.Null(DictionaryCells.Check(Spec, Column("CAM"), "x999", null));
    }

    [Fact]
    public void Lookup_shows_name_and_resolves_typed_text_to_usrid()
    {
        Assert.Equal("Anna Nowak", DictionaryCells.Display("e123456", Persons));
        Assert.Equal("x999", DictionaryCells.Display("x999", Persons));
        Assert.Equal("Anna Nowak (e123456)", Persons[0].Text);

        Assert.Equal("e123456", DictionaryCells.Resolve(" E123456 ", Persons));        // USRID – pisownia z listy
        Assert.Equal("e555111", DictionaryCells.Resolve("jan nowicki", Persons));      // imię i nazwisko – jednoznaczne
        Assert.Equal("e777001", DictionaryCells.Resolve("Jan Kowalski (e777001)", Persons));
        Assert.Equal("Jan Kowalski", DictionaryCells.Resolve("Jan Kowalski", Persons)); // dwie osoby – bez zgadywania
        Assert.Null(DictionaryCells.Resolve("  ", Persons));

        Assert.Equal(["e555111", "e777000", "e777001"], Persons.Where(p => p.Matches("jan")).Select(p => p.Value));
        Assert.Equal(["e123456"], Persons.Where(p => p.Matches("123")).Select(p => p.Value));
    }

    [Fact]
    public void Clipboard_block_from_excel_is_split_into_rows_and_cells()
    {
        var block = ClipboardTable.Parse("A1\tB1\r\nA2\t\r\n\"wiele\nlinii\"\t\"cytat \"\"x\"\"\"\r\n");
        Assert.Equal(3, block.Count);
        Assert.Equal(["A1", "B1"], block[0]);
        Assert.Equal(["A2", ""], block[1]);
        Assert.Equal(["wiele\nlinii", "cytat \"x\""], block[2]);

        Assert.Equal([["jedna"]], ClipboardTable.Parse("jedna"));
        Assert.Empty(ClipboardTable.Parse(""));
    }

    [Fact]
    public void Header_row_copied_with_data_is_recognised()
    {
        Assert.True(DictionaryCells.IsHeaderRow(Spec, 0, ["Element *", "cam", "Kategoria"]));
        Assert.True(DictionaryCells.IsHeaderRow(Spec, 1, ["CAM", "", "BAC", "Inna kolumna poza słownikiem"]));
        Assert.False(DictionaryCells.IsHeaderRow(Spec, 0, ["AC-CAB.6.38", "e123456"]));
        Assert.False(DictionaryCells.IsHeaderRow(Spec, 0, ["", ""]));
    }
}
