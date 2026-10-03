using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Administration;

public sealed class SourceConfigServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private TestDatabase? _database;
    private IJournal _journal = null!;
    private SourceConfigService _service = null!;

    /// <summary>Magazyn testu w bazie testowej (TestDatabase).</summary>
    private void Use()
    {
        _database = new TestDatabase();
        _journal = new SqlJournal(_database.Sql, _services.Clock, _services.User);
        _service = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), _journal);
    }

    public void Dispose() => _database?.Dispose();

    /// <summary>Definicja z parserem ACTUALS albo bez parsera.</summary>
    private static DefinitionInput Definition(string code, string prefix, long? id = null, int? version = null, string parser = ActualsLayout.Parser) =>
        new(id, version, code, prefix, "test", parser, Active: true);

    /// <summary>Tabele bazy testu (sygnatura testu) – parser nie zakłada ani nie zmienia tabel.</summary>
    private List<string> Tables()
    {
        using var connection = _database!.Sql.Open();
        return Dapper.SqlMapper.Query<string>(connection,
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema AND LEFT(t.name, LEN(@prefix)) = @prefix ORDER BY t.name",
            new { schema = TestDatabase.Schema, prefix = _database.Sql.Settings.TablePrefix }).ToList();
    }

    [Fact]
    public void Field_name_is_proposed_from_file_column()
    {
        Assert.Equal("InvoiceNumber", SourceConfigService.FieldName("Invoice Number"));
        Assert.Equal("ValInRepCur", SourceConfigService.FieldName("Val.in rep.cur."));
        Assert.Equal("PartnerCCtr", SourceConfigService.FieldName("Partner-CCtr"));
        Assert.Equal("WartoscZlecenia", SourceConfigService.FieldName("wartość zlecenia"));
        Assert.Equal("F2026", SourceConfigService.FieldName("2026"));
        Assert.Equal("Pole", SourceConfigService.FieldName("—"));
    }

    [Fact]
    public void Parser_compared_with_file_header_proposes_new_fields_and_lists_missing_columns()
    {
        ParserField[] fields = [new("WbsElement", "WBS Element", FieldTypes.Text, 100), new("Item", "Item", FieldTypes.Text, 50), new("Note", "", FieldTypes.Text)];

        var (added, missing) = SourceConfigService.CompareWithFile(fields, ["wbs element", "Invoice Number", "item ", "", "Item No"]);

        Assert.Equal([new ParserField("InvoiceNumber", "Invoice Number", FieldTypes.Text, 400), new ParserField("ItemNo", "Item No", FieldTypes.Text, 400)], added);
        Assert.Empty(missing);
        Assert.Equal(["WBS Element"], SourceConfigService.CompareWithFile(fields, ["Item"]).MissingInFile.Select(f => f.Column));
        Assert.Equal(["Item2"], SourceConfigService.CompareWithFile([new("Item", "Pozycja", FieldTypes.Text)], ["Item"]).NewFields.Select(f => f.Field));
    }

    [SqlFact]
    public void Definition_points_to_existing_active_parser_or_none()
    {
        Use();
        Assert.Contains("Parser NIE_MA nie istnieje albo jest nieaktywny (Administracja → Parsery)",
            _service.SaveDefinition(Definition("X", "X", parser: "NIE_MA")).Issues.Select(i => i.Message));

        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);
        Assert.Equal(("ACTUALS_PAF", "ACTUALS"), (Assert.Single(_service.Definitions()).Code, _service.Definitions()[0].Parser));
        Assert.Contains(_journal.Recent(1), e => e.Message == "Definicja źródła ACTUALS_PAF (prefiks ACTUALS_PAF, parser ACTUALS) dodana");
    }

    [SqlFact]
    public void Prefix_must_be_unique_ignoring_case()
    {
        Use();
        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);
        var result = _service.SaveDefinition(Definition("ACTUALS_PAF2", "actuals_paf"));
        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Message.Contains("Prefiks actuals_paf jest już używany"));
    }

    [SqlFact]
    public void New_parser_gets_slots_in_canonical_table_and_keeps_them_in_next_versions()
    {
        Use();
        var tables = Tables();
        var v1 = _service.SaveParser(new ParserInput(null, null, "forecast", "Prognoza PAF",
        [
            new("Wbs", "WBS Element", FieldTypes.Text, 50),
            new("Amount", "Kwota", FieldTypes.Decimal),
            new("Year", "", FieldTypes.Integer),
            new("Day", "Data", FieldTypes.Date),
        ], true));

        Assert.True(v1.Success, string.Join("; ", v1.Issues.Select(i => i.Message)) + v1.Message);
        Assert.Equal("Zapisano parser FORECAST – nowe pole Wbs → T01; nowe pole Amount → N01; nowe pole Year → I01; nowe pole Day → D01.", v1.Message);
        var parser = Assert.Single(_service.Parsers(), p => p.Code == "FORECAST");
        Assert.Equal(("FORECAST", 1, ""), (parser.Table, parser.Version, parser.Fields[2].Column));   // pole spoza pliku
        Assert.Equal(["T01", "N01", "I01", "D01"], parser.Fields.Select(f => f.Slot));

        var v2 = _service.SaveParser(new ParserInput(parser.ParserId, 1, "FORECAST", "Prognoza PAF",
        [
            new("Invoice", "Invoice Number", FieldTypes.Text, 30), new("Wbs", "WBS Element", FieldTypes.Text, 400), .. parser.Fields.Skip(1),
            new("Note", "Opis", FieldTypes.Text, 2000),
        ], true));

        Assert.True(v2.Success, v2.Message);
        Assert.Equal("Zapisano parser FORECAST – nowe pole Invoice → T02; nowe pole Note → L01.", v2.Message);
        Assert.Equal(["T02", "T01", "N01", "I01", "D01", "L01"], _service.Parsers().Single(p => p.Code == "FORECAST").Fields.Select(f => f.Slot));
        Assert.Equal("T 2/40 · L 1/5 · N 1/20 · I 1/10 · D 1/10", _service.Parsers().Single(p => p.Code == "FORECAST").SlotUsage);
        Assert.Equal(2, _service.ParserHistory(parser.ParserId).Count);
        Assert.Contains(_journal.Recent(3), e => e.Message == "Parser FORECAST zmieniony (6 pól; nowe pole Invoice → T02; nowe pole Note → L01)");
        Assert.Equal(tables, Tables());   // bez zmian tabel w bazie
    }

    [SqlFact]
    public void Changing_kind_of_existing_field_is_rejected_and_nothing_is_saved()
    {
        Use();
        Assert.True(_service.SaveParser(new ParserInput(null, null, "P1", "test",
            [new("Amount", "Kwota", FieldTypes.Decimal), new("Name", "Nazwa", FieldTypes.Text, 100)], true)).Success);
        var parser = Assert.Single(_service.Parsers(), p => p.Code == "P1");

        var changed = _service.SaveParser(new ParserInput(parser.ParserId, 1, "P1", "test",
            [new("Amount", "Kwota", FieldTypes.Text, 20), new("Name", "Nazwa", FieldTypes.Text, 1000)], true));

        Assert.False(changed.Success);
        Assert.Equal("Parser ma błędy – nie zapisano.", changed.Message);
        Assert.Equal(
        [
            "Pole Amount: dane zapisane jako kwota / liczba (slot N01) – zmiana na tekst do 400 znaków niemożliwa; dodaj nowe pole",
            "Pole Name: dane zapisane jako tekst do 400 znaków (slot T01) – zmiana na tekst do 4000 znaków niemożliwa; dodaj nowe pole",
        ], changed.Issues.Select(i => i.Message));
        Assert.Equal(1, _service.Parsers().Single(p => p.Code == "P1").Version);
    }

    [SqlFact]
    public void Parser_cannot_have_more_fields_of_one_kind_than_slots()
    {
        Use();
        var dates = Enumerable.Range(1, 11).Select(i => new ParserField($"Day{i}", $"Data {i}", FieldTypes.Date)).ToList();

        var result = _service.SaveParser(new ParserInput(null, null, "P4", "test", dates, true));

        Assert.False(result.Success);
        Assert.Equal(["Pole Day11: brak wolnego slotu – data: najwięcej 10 pól w parserze"], result.Issues.Select(i => i.Message));
        Assert.DoesNotContain(_service.Parsers(), p => p.Code == "P4");
    }

    [SqlFact]
    public void Parser_fields_are_validated()
    {
        Use();
        var result = _service.SaveParser(new ParserInput(null, null, "P2", "",
        [
            new("Wbs Element", "K1", FieldTypes.Text), new("FileId", "K2", FieldTypes.Integer), new("A", "k1", "liczba"),
            new("a", "", FieldTypes.Text, 5000, Required: true),
        ], true));

        Assert.False(result.Success);
        Assert.Equal(
        [
            "Nazwa: pole wymagane",
            "Pole w bazie „Wbs Element”: litera, potem litery, cyfry i _ (bez spacji), inne niż FileId, RowNumber, ParserId, ParserVersion",
            "Pole w bazie „FileId”: litera, potem litery, cyfry i _ (bez spacji), inne niż FileId, RowNumber, ParserId, ParserVersion",
            "Pole A: typ wymagany (tekst, kwota / liczba, liczba całkowita, data)",
            "Pole a: długość tekstu od 1 do 4000",
            "Pole a: wymagane, ale bez kolumny w pliku",
            "Pole A występuje kilka razy",
            "Kolumna w pliku K1 przypisana kilku polom (Wbs Element, A)",
        ], result.Issues.Select(i => i.Message));
        Assert.Contains("Pola: co najmniej jedno pole z kolumną w pliku",
            _service.SaveParser(new ParserInput(null, null, "P3", "test", [new("A", "", FieldTypes.Text)], true)).Issues.Select(i => i.Message));
        Assert.DoesNotContain(_service.Parsers(), p => p.Code == "P2");
    }

    [SqlFact]
    public void Slot_of_removed_field_is_not_reused_and_returns_with_the_field()
    {
        Use();
        var actuals = _service.Parsers().Single(p => p.Code == ActualsLayout.Parser);
        var invoice = actuals.Fields.Single(f => f.Field == "InvoiceNumber");

        var removed = _service.SaveParser(new ParserInput(actuals.ParserId, actuals.Version, actuals.Code, actuals.Name,
            [.. actuals.Fields.Where(f => f.Field != "InvoiceNumber"), new("Comment", "Comment", FieldTypes.Text, 100)], true));

        Assert.True(removed.Success, removed.Message);
        var v2 = _service.Parsers().Single(p => p.Code == ActualsLayout.Parser);
        Assert.DoesNotContain(v2.Fields, f => f.Field == "InvoiceNumber");
        Assert.Equal("T20", v2.Fields.Single(f => f.Field == "Comment").Slot);   // slot usuniętego pola zostaje przy jego danych

        var restored = _service.SaveParser(new ParserInput(v2.ParserId, v2.Version, v2.Code, v2.Name, [.. v2.Fields, invoice with { Slot = null }], true));

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(invoice.Slot, _service.Parsers().Single(p => p.Code == ActualsLayout.Parser).Fields.Single(f => f.Field == "InvoiceNumber").Slot);
    }

    [SqlFact]
    public void Raw_only_source_can_have_any_columns()
    {
        Use();
        Assert.True(_service.SaveDefinition(Definition("FORECAST_PAF", "FORECAST_PAF", parser: SourceParsers.None)).Success);
    }

    [SqlFact]
    public void Change_keeps_history_and_stale_edit_is_a_conflict()
    {
        Use();
        _service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF"));
        var v1 = Assert.Single(_service.Definitions());
        _services.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF", v1.DefinitionId, 1) with { Active = false }).Success);
        var stale = _service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF", v1.DefinitionId, 1));

        Assert.False(stale.Success);
        Assert.Contains("odśwież", stale.Message);
        var history = _service.DefinitionHistory(v1.DefinitionId);
        Assert.Equal(2, history.Count);
        Assert.False(_service.Definitions().Single().Active);
    }

    [SqlFact]
    public void Asterisk_at_end_of_prefix_is_removed_and_inside_is_rejected()
    {
        Use();
        Assert.True(_service.SaveDefinition(Definition("ACTUALS", "ACTUALS_*")).Success);
        Assert.Equal("ACTUALS_", Assert.Single(_service.Definitions()).Prefix);

        var inside = _service.SaveDefinition(Definition("X", "ACT*UALS"));
        Assert.False(inside.Success);
        Assert.Contains(inside.Issues, i => i.Message.Contains("znaki * i ?"));
    }

    [SqlFact]
    public void SharePoint_link_is_saved_as_webdav_path()
    {
        Use();
        _service.SaveLocation(new LocationInput(null, null, "RABIT", "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659", true));
        Assert.Equal(@"\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659",
            Assert.Single(_service.Locations()).Path);
    }

    [SqlFact]
    public void Location_requires_name_and_path()
    {
        Use();
        var result = _service.SaveLocation(new LocationInput(null, null, " ", "", true));
        Assert.False(result.Success);
        Assert.Equal(2, result.Issues.Count);
    }

    [SqlFact]
    public void Deleted_definition_disappears_keeps_history_and_frees_prefix()
    {
        Use();
        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);
        var v1 = _service.Definitions().Single();

        var stale = _service.DeleteDefinition(v1.DefinitionId, 7);
        var deleted = _service.DeleteDefinition(v1.DefinitionId, v1.Version);

        Assert.False(stale.Success);
        Assert.Contains("odśwież", stale.Message);
        Assert.True(deleted.Success);
        Assert.Empty(_service.Definitions());
        var history = Assert.Single(_service.DefinitionHistory(v1.DefinitionId));
        Assert.Equal(_services.User.Account, history.SupersededBy);
        Assert.Contains(_journal.Recent(5), e => e.Message == "Definicja źródła ACTUALS_PAF (prefiks ACTUALS_PAF) usunięta");
        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);   // kod i prefiks wolne
        Assert.False(_service.DeleteDefinition(v1.DefinitionId, v1.Version).Success);              // już usunięta
    }
}
