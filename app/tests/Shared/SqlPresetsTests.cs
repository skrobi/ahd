using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Migracja 002 (dane startowe): presety zgodne z logiką aplikacji i bez dublowania danych zapisanych wcześniej.</summary>
public sealed class SqlPresetsTests : IDisposable
{
    private const string RabitPath = @"\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659";

    private readonly TestServices _services = new();
    private TestDatabase _database = null!;
    private SourceConfigService _sources = null!;
    private SqlDictionaryStore _dictionaries = null!;
    private SqlJournal _journal = null!;

    private void Use(bool presets)
    {
        _database = new TestDatabase(presets);
        _journal = new SqlJournal(_database.Sql, _services.Clock, _services.User);
        _sources = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), _journal);
        _dictionaries = new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, GlobalDictionaries.Tables);
    }

    public void Dispose() => _database?.Dispose();

    [SqlFact]
    public void Presets_contain_actuals_sources_active_rabit_location_calendar_and_cost_category()
    {
        Use(presets: true);

        var definitions = _sources.Definitions();
        Assert.Equal(["ACTUALS_CES", "ACTUALS_PAF"], definitions.Select(d => d.Code));
        Assert.All(definitions, d =>
        {
            Assert.Equal(d.Code, d.Prefix);
            Assert.Equal(SourceParsers.ActualsColumns, d.Columns);
            Assert.Equal(HeaderSignature.Compute(SourceParsers.ActualsColumns), d.Signature);
            Assert.Equal((SourceParsers.Actuals, SourceConfigService.ActualsParserVersion, true, 1), (d.Parser, d.ParserVersion, d.Active, d.Version));
        });
        var location = Assert.Single(_sources.Locations());
        Assert.Equal(("RABIT E456659", RabitPath, true), (location.Name, location.Path, location.Active));

        var expected = IsoCalendar.Rows(2026).Concat(IsoCalendar.Rows(2027)).ToList();
        var calendar = _dictionaries.Current(GlobalDictionaries.Calendar)
            .OrderBy(r => int.Parse(r.Values["Rok"]!)).ThenBy(r => int.Parse(r.Values["Tydzień"]!)).ToList();
        Assert.Equal(expected.Count, calendar.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], calendar[i].Values);

        var costCategory = _dictionaries.Current(GlobalDictionaries.CostCategory).ToDictionary(r => r.Values["Numer elementu kosztowego"]!);
        Assert.Equal(33, costCategory.Count);
        Assert.Equal("Direct Materials", costCategory["0051105550"].Values["Cost Category"]);
        Assert.Null(costCategory["0057100000"].Values["Cost Category"]);
        Assert.Equal("Manufacturing and QA LL labor", costCategory["9229D550"].Values["Cost Category"]);
        Assert.Empty(_dictionaries.Current(GlobalDictionaries.DepartmentRates));

        Assert.Contains(_journal.Recent(5), e => e.Message ==
            "Migracja 002 – dane startowe: definicje źródeł 2, lokalizacje RABIT 1, kalendarz okresów 105 tyg., Cost Category 33 wierszy");

        // presety są zwykłymi wierszami – zmiana w Administracji tworzy nową wersję
        var ces = definitions.Single(d => d.Code == "ACTUALS_CES");
        Assert.True(_sources.SaveDefinition(new DefinitionInput(ces.DefinitionId, ces.Version, ces.Code, ces.Prefix, ces.ReportType, ces.Columns, ces.Parser, Active: false)).Success);
        Assert.Equal(2, _sources.Definitions().Single(d => d.Code == "ACTUALS_CES").Version);
    }

    [SqlFact]
    public void Presets_keep_data_entered_earlier_and_enable_location_from_old_start_data()
    {
        Use(presets: false);
        Assert.True(_sources.SaveDefinition(new DefinitionInput(null, null, "ACTUALS_PAF", "ACTUALS_PAF", "moja definicja",
            SourceParsers.ActualsColumns, SourceParsers.Actuals, Active: true)).Success);
        Assert.True(_sources.SaveLocation(new LocationInput(null, null, "RABIT E456659", RabitPath, Active: false)).Success);   // dawne dane startowe aplikacji
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CostCategory);
        var own = new Dictionary<string, string?> { ["Numer elementu kosztowego"] = "0051105550", ["Opis"] = "własny", ["Obszar"] = null, ["Cost Category"] = "Inna" };
        Assert.True(_dictionaries.Save(GlobalDictionaries.CostCategory, null, [new RowChange(RowChangeKind.Added, null, null, spec.KeyOf(own), own)]).Success);

        Assert.Equal(["002_dane_startowe.sql"], SqlMigrations.Apply(_database.Sql));

        var paf = _sources.Definitions().Single(d => d.Code == "ACTUALS_PAF");
        Assert.Equal(("moja definicja", 1), (paf.ReportType, paf.Version));
        Assert.Contains(_sources.Definitions(), d => d.Code == "ACTUALS_CES");
        var location = Assert.Single(_sources.Locations());
        Assert.Equal((2, true), (location.Version, location.Active));
        var costCategory = _dictionaries.Current(GlobalDictionaries.CostCategory);
        Assert.Equal(33, costCategory.Count);
        Assert.Equal("Inna", costCategory.Single(r => r.Values["Numer elementu kosztowego"] == "0051105550").Values["Cost Category"]);
        Assert.Contains(_journal.Recent(5), e => e.Message ==
            "Migracja 002 – dane startowe: definicje źródeł 1, lokalizacje RABIT 1, kalendarz okresów 105 tyg., Cost Category 32 wierszy");
    }
}
