using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models.Sources;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Administration;

public class SourceConfigServiceTests
{
    private readonly TestServices _services = new();
    private readonly SourceConfigService _service;

    public SourceConfigServiceTests()
    {
        _service = new SourceConfigService(new InMemorySourceConfigStore(_services.Database, _services.Clock, _services.User), _services.Journal);
    }

    private static DefinitionInput Definition(string code, string prefix, long? id = null, int? version = null, string parser = SourceParsers.Actuals) =>
        new(id, version, code, prefix, "test", parser == SourceParsers.Actuals ? SourceParsers.ActualsColumns : ["A", "B"],
            "", [], "", "", "", parser, Active: true);

    [Fact]
    public void Seed_creates_actuals_definitions_and_inactive_rabit_location()
    {
        Assert.Equal(3, SourceConfigSeed.EnsureSeeded(_service));
        Assert.Equal(["ACTUALS_CES", "ACTUALS_PAF"], _service.Definitions().Select(d => d.Code));
        Assert.All(_service.Definitions(), d => Assert.Equal("7c59f446fe9c3d5e", d.Signature));
        var location = Assert.Single(_service.Locations());
        Assert.False(location.Active);
        Assert.Equal(0, SourceConfigSeed.EnsureSeeded(_service));
    }

    [Fact]
    public void Prefix_must_be_unique_ignoring_case()
    {
        Assert.True(_service.SaveDefinition(Definition("ACTUALS_PAF", "ACTUALS_PAF")).Success);
        var result = _service.SaveDefinition(Definition("ACTUALS_PAF2", "actuals_paf"));
        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Message.Contains("Prefiks actuals_paf jest już używany"));
    }

    [Fact]
    public void Actuals_parser_requires_actuals_columns()
    {
        var result = _service.SaveDefinition(Definition("X", "X") with { Columns = ["WBS Element"] });
        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Message.StartsWith("Parser ACTUALS wymaga kolumn"));
    }

    [Fact]
    public void Raw_only_source_can_have_any_columns()
    {
        Assert.True(_service.SaveDefinition(Definition("FORECAST_PAF", "FORECAST_PAF", parser: SourceParsers.None)).Success);
    }

    [Fact]
    public void Change_keeps_history_and_stale_edit_is_a_conflict()
    {
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

    [Fact]
    public void Location_requires_name_and_path()
    {
        var result = _service.SaveLocation(new LocationInput(null, null, " ", "", true));
        Assert.False(result.Success);
        Assert.Equal(2, result.Issues.Count);
    }
}
