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

    private static DefinitionInput Definition(string code, string prefix, long? id = null, int? version = null, string parser = SourceParsers.Actuals) =>
        new(id, version, code, prefix, "test", parser == SourceParsers.Actuals ? SourceParsers.ActualsColumns : ["A", "B"], parser, Active: true);

    [Fact]
    public void Columns_are_one_per_line_or_header_row_pasted_from_excel()
    {
        var pasted = string.Join("\t", SourceParsers.ActualsColumns) + "\r\n";   // wiersz nagłówków skopiowany z Excela

        Assert.Equal(SourceParsers.ActualsColumns, SourceConfigService.ParseColumns(pasted));
        Assert.Equal(SourceParsers.ActualsColumns, SourceConfigService.ParseColumns(string.Join("\r\n", SourceParsers.ActualsColumns.Select(c => $"  {c} ")) + "\r\n\r\n"));
        Assert.Equal("7c59f446fe9c3d5e", SourceConfigService.Signature(SourceConfigService.ParseColumns(pasted)));
        Assert.Equal("", SourceConfigService.Signature([]));
    }

    [SqlFact]
    public void Changed_columns_are_saved_as_new_version_with_new_signature()
    {
        Use();
        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);
        var saved = Assert.Single(_service.Definitions());
        var pasted = "Dodatkowa\t" + string.Join("\t", SourceParsers.ActualsColumns);

        Assert.True(_service.SaveDefinition(new DefinitionInput(saved.DefinitionId, saved.Version, saved.Code, saved.Prefix, saved.ReportType,
            [pasted], saved.Parser, saved.Active)).Success);

        var changed = Assert.Single(_service.Definitions());
        Assert.Equal(2, changed.Version);
        Assert.Equal(["Dodatkowa", .. SourceParsers.ActualsColumns], changed.Columns);
        Assert.Equal(HeaderSignature.Compute(["Dodatkowa", .. SourceParsers.ActualsColumns]), changed.Signature);
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
    public void Actuals_parser_requires_actuals_columns()
    {
        Use();
        var result = _service.SaveDefinition(Definition("X", "X") with { Columns = ["WBS Element"] });
        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Message.StartsWith("Parser ACTUALS wymaga kolumn"));
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
