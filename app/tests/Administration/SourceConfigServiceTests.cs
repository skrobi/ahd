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

    private IReadOnlyList<ParserField> ActualsFields => _service.Parsers().Single(p => p.Code == ActualsLayout.Parser).Fields;

    /// <summary>Definicja z parserem ACTUALS (standardowy układ i mapowanie) albo bez parsera.</summary>
    private DefinitionInput Definition(string code, string prefix, long? id = null, int? version = null, string parser = ActualsLayout.Parser) =>
        parser == ActualsLayout.Parser
            ? new(id, version, code, prefix, "test", ActualsLayout.Columns, parser, Active: true, ActualsLayout.Mapping(ActualsFields))
            : new(id, version, code, prefix, "test", ["A", "B"], parser, Active: true);

    private List<(string Name, string Type, int Length)> TableColumns(string table)
    {
        using var connection = _database!.Sql.Open();
        return Dapper.SqlMapper.Query<(string, string, short)>(connection,
                "SELECT c.name, t.name, c.max_length FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@table) ORDER BY c.column_id",
                new { table = _database.Sql.Table(table) })
            .Select(c => (c.Item1, c.Item2, c.Item3 == -1 ? -1 : c.Item2.StartsWith('n') ? c.Item3 / 2 : (int)c.Item3))
            .ToList();
    }

    [Fact]
    public void Columns_are_one_per_line_or_header_row_pasted_from_excel()
    {
        var pasted = string.Join("\t", ActualsLayout.Columns) + "\r\n";   // wiersz nagłówków skopiowany z Excela

        Assert.Equal(ActualsLayout.Columns, SourceConfigService.ParseColumns(pasted));
        Assert.Equal(ActualsLayout.Columns, SourceConfigService.ParseColumns(string.Join("\r\n", ActualsLayout.Columns.Select(c => $"  {c} ")) + "\r\n\r\n"));
        Assert.Equal("7c59f446fe9c3d5e", SourceConfigService.Signature(SourceConfigService.ParseColumns(pasted)));
        Assert.Equal("", SourceConfigService.Signature([]));
    }

    [SqlFact]
    public void Changed_columns_are_saved_as_new_version_with_new_signature()
    {
        Use();
        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);
        var saved = Assert.Single(_service.Definitions());
        var pasted = "Dodatkowa\t" + string.Join("\t", ActualsLayout.Columns);

        Assert.True(_service.SaveDefinition(new DefinitionInput(saved.DefinitionId, saved.Version, saved.Code, saved.Prefix, saved.ReportType,
            [pasted], saved.Parser, saved.Active, saved.Mapping)).Success);

        var changed = Assert.Single(_service.Definitions());
        Assert.Equal(2, changed.Version);
        Assert.Equal(["Dodatkowa", .. ActualsLayout.Columns], changed.Columns);
        Assert.Equal(HeaderSignature.Compute(["Dodatkowa", .. ActualsLayout.Columns]), changed.Signature);
        Assert.NotEqual(saved.Signature, changed.Signature);
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
    public void Definition_with_parser_needs_mapping_of_its_columns_to_parser_fields()
    {
        Use();
        string[] Errors(DefinitionInput input) => _service.SaveDefinition(input).Issues.Select(i => i.Message).ToArray();
        var definition = Definition("X", "X");

        Assert.Contains("Mapowanie: przypisz kolumnom pliku pola parsera ACTUALS (albo wybierz parser „brak” – tylko wiersze surowe)",
            Errors(definition with { Mapping = [] }));
        Assert.Contains("Mapowanie: kolumny Brak nie ma w oczekiwanych kolumnach", Errors(definition with { Mapping = [new("Brak", "WbsElement", true)] }));
        Assert.Contains("Mapowanie: pola Nieznane nie ma w parserze ACTUALS", Errors(definition with { Mapping = [new("WBS Element", "Nieznane", true)] }));
        Assert.Contains("Mapowanie: pole WbsElement przypisane kilku kolumnom (WBS Element, Project Definition)",
            Errors(definition with { Mapping = [new("WBS Element", "WbsElement", true), new("Project Definition", "WbsElement", false)] }));
        Assert.Contains("Parser NIE_MA nie istnieje albo jest nieaktywny (Administracja → Parsery)", Errors(definition with { Parser = "NIE_MA" }));

        Assert.True(_service.SaveDefinition(definition with { Mapping = [new("wbs element", "WbsElement", true)] }).Success);   // jedno pole wystarczy
        var saved = Assert.Single(_service.Definitions());
        Assert.Equal([new ColumnMapping("WBS Element", "WbsElement", true)], saved.Mapping);   // pisownia kolumny z listy oczekiwanych
        Assert.Equal(1, saved.ParserVersion);
    }

    [SqlFact]
    public void New_parser_creates_its_table_and_new_or_longer_fields_change_it()
    {
        Use();
        var v1 = _service.SaveParser(new ParserInput(null, null, "forecast", "Prognoza PAF",
        [
            new("Wbs", "WBS Element", FieldTypes.Text, 50),
            new("Amount", "Kwota", FieldTypes.Decimal),
            new("Year", "", FieldTypes.Integer),
            new("Day", "Data", FieldTypes.Date),
        ], true));

        Assert.True(v1.Success, string.Join("; ", v1.Issues.Select(i => i.Message)) + v1.Message);
        var parser = Assert.Single(_service.Parsers(), p => p.Code == "FORECAST");
        Assert.Equal(("FORECAST", 1, "Year"), (parser.Table, parser.Version, parser.Fields[2].Label));   // pusta nazwa w pliku = nazwa pola
        Assert.Equal(
            [("FileId", "bigint", 8), ("RowNumber", "int", 4), ("ParserVersion", "int", 4), ("Wbs", "nvarchar", 50), ("Amount", "decimal", 13),
             ("Year", "int", 4), ("Day", "date", 3)],
            TableColumns("can.FORECAST"));

        var v2 = _service.SaveParser(new ParserInput(parser.ParserId, 1, "FORECAST", "Prognoza PAF",
            [new("Wbs", "WBS Element", FieldTypes.Text, 80), .. parser.Fields.Skip(1), new("Invoice", "Invoice Number", FieldTypes.Text, 30)], true));

        Assert.True(v2.Success, v2.Message);
        Assert.Equal("Zapisano parser FORECAST – kolumna Wbs wydłużona do 80 znaków; nowa kolumna Invoice (tekst).", v2.Message);
        Assert.Contains(("Invoice", "nvarchar", 30), TableColumns("can.FORECAST"));
        Assert.Contains(("Wbs", "nvarchar", 80), TableColumns("can.FORECAST"));
        Assert.Equal(2, _service.ParserHistory(parser.ParserId).Count);
        Assert.Contains(_journal.Recent(3), e => e.Message.StartsWith("Parser FORECAST zmieniony (5 pól; "));
    }

    [SqlFact]
    public void Changing_type_of_existing_column_is_rejected_and_nothing_is_saved()
    {
        Use();
        Assert.True(_service.SaveParser(new ParserInput(null, null, "P1", "test", [new("Amount", "Kwota", FieldTypes.Decimal)], true)).Success);
        var parser = Assert.Single(_service.Parsers(), p => p.Code == "P1");

        var changed = _service.SaveParser(new ParserInput(parser.ParserId, 1, "P1", "test", [new("Amount", "Kwota", FieldTypes.Text, 20)], true));

        Assert.False(changed.Success);
        Assert.StartsWith("Pole Amount: kolumna w bazie ma typ decimal – zmiana na „tekst” niemożliwa", changed.Message);
        Assert.Equal(1, _service.Parsers().Single(p => p.Code == "P1").Version);   // transakcja wycofana
    }

    [SqlFact]
    public void Parser_fields_are_validated()
    {
        Use();
        var result = _service.SaveParser(new ParserInput(null, null, "P2", "",
            [new("Wbs Element", "", FieldTypes.Text), new("FileId", "", FieldTypes.Integer), new("A", "", "liczba"), new("a", "", FieldTypes.Text, 5000)], true));

        Assert.False(result.Success);
        Assert.Equal(
        [
            "Nazwa: pole wymagane",
            "Pole w bazie „Wbs Element”: litera, potem litery, cyfry i _ (bez spacji), inne niż FileId, RowNumber, ParserVersion",
            "Pole w bazie „FileId”: litera, potem litery, cyfry i _ (bez spacji), inne niż FileId, RowNumber, ParserVersion",
            "Pole A: typ wymagany (tekst, kwota / liczba, liczba całkowita, data)",
            "Pole a: długość tekstu od 1 do 4000",
            "Pole A występuje kilka razy",
        ], result.Issues.Select(i => i.Message));
        Assert.DoesNotContain(_service.Parsers(), p => p.Code == "P2");
    }

    [SqlFact]
    public void Field_used_in_definition_mapping_cannot_be_removed_from_parser()
    {
        Use();
        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);
        var actuals = _service.Parsers().Single(p => p.Code == ActualsLayout.Parser);

        var result = _service.SaveParser(new ParserInput(actuals.ParserId, actuals.Version, actuals.Code, actuals.Name,
            actuals.Fields.Where(f => f.Field != "WbsElement").ToList(), true));

        Assert.False(result.Success);
        Assert.Contains("Pole WbsElement jest zmapowane w definicji ACTUALS_PAF (kolumna WBS Element) – najpierw zmień mapowanie",
            result.Issues.Select(i => i.Message));
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
