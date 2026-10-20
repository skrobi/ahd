using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.Projects;
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

    /// <summary>Grupa z formułami do porównania – nazwy pul zamiast identyfikatorów (identyfikatory w bazie są inne niż w danych testu).</summary>
    private static string Describe(CasBurden.Group g, IReadOnlyList<CasBurden.Pool> pools)
    {
        var names = pools.ToDictionary(p => p.Id, p => p.Name);
        return $"{g.Name}: {string.Join("; ", g.Formulas.Select(f => $"{names[f.Key]} = {CasFormula.Display(f.Value, id => names[id])}").Order(StringComparer.Ordinal))}";
    }

    [SqlFact]
    public void Presets_contain_actuals_sources_active_rabit_location_calendar_and_cost_category()
    {
        Use(presets: true);

        var definitions = _sources.Definitions();
        Assert.Equal(["ACTUALS_CES", "ACTUALS_PAF"], definitions.Select(d => d.Code));
        Assert.All(definitions, d =>
        {
            Assert.Equal(d.Code, d.Prefix);
            Assert.Equal((ActualsLayout.Parser, true, 1), (d.Parser, d.Active, d.Version));   // układ pilnuje parser ACTUALS (004)
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
        Assert.Equal(35, costCategory.Count);   // 040 – konta materiałowe 0051102550, 0051103550
        Assert.Equal("Direct Materials", costCategory["0051105550"].Values["Cost Category"]);
        Assert.Null(costCategory["0057100000"].Values["Cost Category"]);
        Assert.Equal("Manufacturing and QA LL labor", costCategory["9229D550"].Values["Cost Category"]);
        foreach (var element in new[] { "0051105550", "0051102550", "0051103550" })
            Assert.Equal(GlobalDictionaries.MaterialKind, costCategory[element].Values[GlobalDictionaries.CostKindColumn]);
        Assert.Equal(costCategory["0051105550"].Values[GlobalDictionaries.BurdenGroupColumn], costCategory["0051103550"].Values[GlobalDictionaries.BurdenGroupColumn]);
        Assert.Empty(_dictionaries.Current(GlobalDictionaries.DepartmentRates));

        // 034: narzuty CAS z arkusza finansów – pule, grupy i formuły z klocków (identyfikatory pul), wskaźniki 2025–2026,
        // grupa elementów kosztowych (identyfikator grupy, nowa wersja pozycji)
        IEnumerable<DictRow> Rows(string dictionary) => _dictionaries.Current(dictionary).Select(r => new DictRow(r.RowId, r.Version, r.Values));
        var pools = CasBurden.PoolsFrom(Rows(GlobalDictionaries.CasPools));
        Assert.Equal(CasCostReportTests.FinancePools.Select(p => (p.Name, p.Order, p.IsFccom)), pools.Select(p => (p.Name, p.Order, p.IsFccom)));
        var groups = CasBurden.GroupsFrom(Rows(GlobalDictionaries.CasBurdenGroups), Rows(GlobalDictionaries.CasBurdenFormulas));
        Assert.Equal(CasCostReportTests.FinanceGroups.Select(g => Describe(g, CasCostReportTests.FinancePools)).Order(), groups.Select(g => Describe(g, pools)).Order());
        Assert.All(groups, g => Assert.NotNull(g.Description));
        var rates = CasBurden.RatesFrom(Rows(GlobalDictionaries.CasBurdenRates));
        Assert.Equal(34, rates.Count);   // 17 pul × 2025, 2026; DECIMAL(38,18)
        Assert.Equal((new DateOnly(2026, 1, 1), (DateOnly?)new DateOnly(2026, 12, 31), 0.000057456819309837m),
            rates.Where(r => r.Pool == pools.Single(p => p.Name == "FCCOM G&A").Id && r.From.Year == 2026).Select(r => (r.From, r.To, r.Value)).Single());
        string? GroupOf(string element) => groups.SingleOrDefault(g => CasBurden.Ref(g.Id) == costCategory[element].Values[GlobalDictionaries.BurdenGroupColumn])?.Name;
        Assert.Equal(("MFG labor", 2), (GroupOf("0092210550"), costCategory["0092210550"].Version));
        Assert.Equal("Manufacturing and QA labor", costCategory["0092210550"].Values["Cost Category"]);
        Assert.Equal(CasCostReportTests.FinanceMappingNames.Count + 1, costCategory.Values.Count(r => r.Values[GlobalDictionaries.BurdenGroupColumn] is not null));   // 0057510550 – spoza danych startowych; 040 – 0051102550, 0051103550 jak 0051105550
        Assert.All(CasCostReportTests.FinanceMappingNames.Where(m => costCategory.ContainsKey(m.Key)), m => Assert.Equal(m.Value, GroupOf(m.Key)));
        Assert.Null(costCategory["0057100000"].Values[GlobalDictionaries.BurdenGroupColumn]);

        Assert.Contains(_journal.Recent(50), e => e.Message ==
            "Migracja 002 – dane startowe: definicje źródeł 2, lokalizacje RABIT 1, kalendarz okresów 105 tyg., Cost Category 33 wierszy");

        // presety są zwykłymi wierszami – zmiana w Administracji tworzy nową wersję
        var ces = definitions.Single(d => d.Code == "ACTUALS_CES");
        Assert.True(_sources.SaveDefinition(new DefinitionInput(ces.DefinitionId, ces.Version, ces.Code, ces.Prefix, ces.ReportType, ces.Parser, Active: false)).Success);
        Assert.Equal(2, _sources.Definitions().Single(d => d.Code == "ACTUALS_CES").Version);
    }

    [SqlFact]
    public void Presets_keep_data_entered_earlier_and_enable_location_from_old_start_data()
    {
        Use(presets: false);
        Assert.True(_sources.SaveDefinition(new DefinitionInput(null, null, "ACTUALS_PAF", "ACTUALS_PAF", "moja definicja",
            ActualsLayout.Parser, Active: true)).Success);
        Assert.True(_sources.SaveLocation(new LocationInput(null, null, "RABIT E456659", RabitPath, Active: false)).Success);   // dawne dane startowe aplikacji
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CostCategory);
        var own = new Dictionary<string, string?>
        {
            ["Numer elementu kosztowego"] = "0051105550", ["Opis"] = "własny", ["Obszar"] = null, ["Cost Category"] = "Inna", [GlobalDictionaries.BurdenGroupColumn] = "999",
        };
        Assert.True(_dictionaries.Save(GlobalDictionaries.CostCategory, null, [new RowChange(RowChangeKind.Added, null, null, spec.KeyOf(own), own)]).Success);
        var fxSpec = GlobalDictionaries.Get(GlobalDictionaries.FxRates);
        var ownFx = new Dictionary<string, string?> { ["Waluta"] = "USD", ["Od"] = "2024-01-01", ["Do"] = "2024-01-31", ["Kurs"] = "3.5", [GlobalDictionaries.CasFxColumn] = "3.6" };
        Assert.True(_dictionaries.Save(GlobalDictionaries.FxRates, null, [new RowChange(RowChangeKind.Added, null, null, fxSpec.KeyOf(ownFx), ownFx)]).Success);

        Assert.Equal(["002_dane_startowe.sql", "023_dane_produkcji.sql", "030_dane_narzuty_cas.sql", "032_dane_formuly_narzutow_cas.sql", "034_dane_narzuty_cas.sql", "040_dane_rodzaj_kosztu.sql", "046_dane_kursy_usd.sql", "052_dane_reguly_i_przebiegi.sql"],
            SqlMigrations.Apply(_database.Sql));

        var paf = _sources.Definitions().Single(d => d.Code == "ACTUALS_PAF");
        Assert.Equal(("moja definicja", 1), (paf.ReportType, paf.Version));
        Assert.Contains(_sources.Definitions(), d => d.Code == "ACTUALS_CES");
        var location = Assert.Single(_sources.Locations());
        Assert.Equal((2, true), (location.Version, location.Active));
        var costCategory = _dictionaries.Current(GlobalDictionaries.CostCategory);
        Assert.Equal(35, costCategory.Count);   // 040 – konta materiałowe 0051102550, 0051103550
        var mine = costCategory.Single(r => r.Values["Numer elementu kosztowego"] == "0051105550");
        Assert.Equal(("Inna", "999", GlobalDictionaries.MaterialKind, 2),
            (mine.Values["Cost Category"], mine.Values[GlobalDictionaries.BurdenGroupColumn], mine.Values[GlobalDictionaries.CostKindColumn], mine.Version));   // 034 – grupa ustawiona wcześniej zostaje; 040 – rodzaj Materials (nowa wersja)
        Assert.Contains(_journal.Recent(50), e => e.Message ==
            "Migracja 002 – dane startowe: definicje źródeł 1, lokalizacje RABIT 1, kalendarz okresów 105 tyg., Cost Category 32 wierszy");
        // 046 – kursy USD 2024-01 … 2026-10 (przybliżone); miesiąc z kursem wpisanym wcześniej zostaje bez zmian.
        var usd = _dictionaries.Current(GlobalDictionaries.FxRates).Where(r => r.Values["Waluta"] == "USD").OrderBy(r => r.Values["Od"], StringComparer.Ordinal).ToList();
        Assert.Equal(34, usd.Count);
        Assert.Equal(("2024-01-01", "3.6"), (usd[0].Values["Od"], usd[0].Values[GlobalDictionaries.CasFxColumn]?.TrimEnd('0')));
        Assert.Equal(("2026-10-01", "2026-10-31"), (usd[^1].Values["Od"], usd[^1].Values["Do"]));
        Assert.All(usd.Skip(1), r =>
        {
            Assert.Equal(r.Values["Kurs"], r.Values[GlobalDictionaries.CasFxColumn]);
            Assert.InRange(decimal.Parse(r.Values["Kurs"]!, System.Globalization.CultureInfo.InvariantCulture), 3.5m, 4.5m);
        });
        Assert.Empty(DictionaryValidator.Validate(fxSpec, usd.Select(r => new DictRow(r.RowId, r.Version, r.Values)).ToList()).Where(i => i.Level == PzlEv.Shared.Models.Pipeline.CheckLevel.Error));   // okresy się nie nakładają
    }

    /// <summary>Migracja 052: reguły startowe w słowniku „Reguły walidacji” i szablony przebiegów Wewnętrzny i CAS jak w aplikacji.</summary>
    [SqlFact]
    public void Presets_contain_starter_rules_and_run_templates_and_drop_reconciliation_elements()
    {
        Use(presets: false);
        var dictionaries = new DictionaryService(_dictionaries, _journal);
        var store = new PzlEv.Modules.Runs.Data.SqlRunStore(_database.Sql);
        var templates = new PzlEv.Modules.Runs.Services.RunTemplateService(dictionaries, store);
        // Przed migracją 052: własny szablon CAS z elementem „Uzgodnienie: *” (reguły uzgodnień sprzed reguł SQL).
        var cas = PzlEv.Shared.Models.Pipeline.RunDefinitions.Starters[1];
        var own = cas with { Steps = [.. cas.Steps.Take(2), new(3, "Kontrole", PzlEv.Shared.Models.Pipeline.RunActions.Checks, PzlEv.Shared.Models.Pipeline.RunGates.Info, false,
            [new("WP bez MPK")])] };
        Assert.Equal(SaveStatus.Saved, templates.Save(own, isActive: true).Status);
        var elements = PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.Get(GlobalDictionaries.RunStepElements);
        var current = dictionaries.Load(elements);
        Assert.Equal(SaveStatus.Saved, dictionaries.Save(elements, [.. current, new DictRow(null, null, new Dictionary<string, string?>
            { ["Definicja"] = "CAS", ["Etap"] = "Kontrole", ["Element"] = "Uzgodnienie: *", ["Aktywny"] = "tak", ["Opis"] = null })], [], confirmWarnings: true).Status);

        SqlMigrations.Apply(_database.Sql, presets: true);

        static string Lines(string? text) => (text ?? "").Replace("\r\n", "\n");
        var rules = store.Rules();
        Assert.False(templates.Rules().Starters);                                        // reguły w słowniku, nie z aplikacji
        Assert.Equal(PzlEv.Shared.Models.Pipeline.RunRules.Starters.Select(r => (r.Name, r.Level, r.IsActive, Lines(r.Sql), r.ResultTemplate, r.Description)),
            rules.Select(r => (r.Name, r.Level, r.IsActive, Lines(r.Sql), r.ResultTemplate, r.Description)));
        var loaded = templates.Load();
        var internalTemplate = loaded.Single(t => t.Definition.Code == PzlEv.Shared.Models.Pipeline.RunDefinitions.InternalCode);
        Assert.True(internalTemplate.IsActive);
        Assert.Equal(PzlEv.Shared.Models.Pipeline.RunDefinitions.StandardSteps.Select(s => (s.No, s.Name, s.Action, string.Join("|", s.Elements.Select(e => e.Element)))),
            internalTemplate.Definition.Steps.Select(s => (s.No, s.Name, s.Action, string.Join("|", s.Elements.Select(e => e.Element)))));
        Assert.Empty(PzlEv.Shared.Models.Pipeline.RunDefinitions.Validate(internalTemplate.Definition, [.. rules.Select(r => r.Name)]));
        var ownCas = loaded.Single(t => t.Definition.Code == "CAS").Definition;           // szablon użytkownika bez zmian
        Assert.Equal(3, ownCas.Steps.Count);
        Assert.Equal(["WP bez MPK"], ownCas.Steps[2].Elements.Select(e => e.Element));     // „Uzgodnienie: *” – usunięte
    }
}
