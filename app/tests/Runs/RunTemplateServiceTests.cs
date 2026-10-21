using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Szablony przebiegów i reguły walidacji na bazie: szablony startowe, zapis zmian z historią, usunięcie; reguły – startowe, zapis, odrzucenie.</summary>
public sealed class RunTemplateServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private TestDatabase? _database;

    public void Dispose() => _database?.Dispose();

    [SqlFact]
    public void Starters_are_created_once_edited_with_history_and_deleted()
    {
        _database = new TestDatabase();
        var dictionaries = new DictionaryService(new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, GlobalDictionaries.Tables),
            new SqlJournal(_database.Sql, _services.Clock, _services.User));
        var store = new SqlRunStore(_database.Sql);
        var service = new RunTemplateService(dictionaries, store);

        Assert.StartsWith("Utworzono szablony", service.CreateStarters());
        Assert.Equal("Szablony startowe już są.", service.CreateStarters());
        var templates = service.Load();
        Assert.Equal([RunDefinitions.CasCode, RunDefinitions.InternalCode], templates.Select(t => t.Definition.Code));
        Assert.All(templates, t => Assert.Equal(RunDefinitions.StandardSteps.Count, t.Definition.Steps.Count));
        var (headers, steps, elements) = store.DefinitionRows();
        Assert.Equal(RunDefinitions.CasCode, RunDefinitions.For(headers, steps, elements, null, "CAS").Code);
        Assert.Equal(RunDefinitions.InternalCode, RunDefinitions.For(headers, steps, elements, null, "SAC").Code);

        // Bez etapu „Walidacja EV”, walidacja z jedną regułą – etapy o tych samych numerach zachowują historię.
        var cas = templates[0].Definition;
        var stepIds = dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunSteps)).Where(r => r["Definicja"] == "CAS").ToDictionary(r => r["Kolejność"]!, r => r.RowId);
        var rule = RunRules.EvStarters[^1].Name;
        var changed = cas with
        {
            Steps = [.. cas.Steps.Where(s => s.Name != RunRules.EvArea).Select(s => s.No == 3 ? s with { Elements = [new(rule)] } : s)],
        };
        Assert.Equal(SaveStatus.Saved, service.Save(changed, isActive: true).Status);
        var reloaded = service.Load().Single(t => t.Definition.Code == "CAS").Definition;
        Assert.Equal([1, 2, 3, 4], reloaded.Steps.Select(s => s.No));
        Assert.Equal(new RunElementSetting(rule), Assert.Single(reloaded.Steps[2].Elements));
        Assert.Equal(stepIds["1"], dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunSteps)).Single(r => r["Definicja"] == "CAS" && r["Kolejność"] == "1").RowId);
        Assert.Equal(SaveStatus.Rejected, service.Save(changed with { Steps = changed.Steps.Skip(1).ToList() }, true).Status);   // bez importu źródeł

        Assert.Equal(SaveStatus.Saved, service.Delete("CAS").Status);
        Assert.Equal([RunDefinitions.InternalCode], service.Load().Select(t => t.Definition.Code));
        Assert.Empty(dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunStepElements)).Where(r => r["Definicja"] == "CAS"));
    }

    [SqlFact]
    public void Rules_start_from_starters_save_with_history_reject_data_changes_and_report_rules_missing_in_templates()
    {
        _database = new TestDatabase();
        var dictionaries = new DictionaryService(new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, GlobalDictionaries.Tables),
            new SqlJournal(_database.Sql, _services.Clock, _services.User));
        var store = new SqlRunStore(_database.Sql);
        var service = new RunTemplateService(dictionaries, store);
        service.CreateStarters();

        var (rules, starters) = service.Rules();
        Assert.True(starters);
        Assert.Equal(RunRules.Starters, store.Rules());

        var changed = rules.Select(r => r.Name == "Wiersze bez elementu WBS" ? r with { Level = CheckLevel.Error, ResultTemplate = null } : r)
            .Where(r => r.Name != "Waluta inna niż waluta kosztów").ToList();
        var outcome = service.SaveRules(changed);

        Assert.Equal(SaveStatus.Saved, outcome.Status);
        Assert.Contains("Szablony przebiegów używają reguł, których już nie ma: Waluta inna niż waluta kosztów", outcome.Message);
        var saved = store.Rules();
        Assert.False(service.Rules().Starters);
        Assert.Equal(changed.Count, saved.Count);
        Assert.Equal((CheckLevel.Error, (string?)null), saved.Where(r => r.Name == "Wiersze bez elementu WBS").Select(r => (r.Level, r.ResultTemplate)).Single());
        Assert.Equal(RunRules.Starter("Netowanie do 0 w okresach zamkniętych"), saved.Single(r => r.Name.StartsWith("Netowanie")));
        var rowId = dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunValidations)).Single(r => r["Nazwa"] == "WP bez MPK").RowId;
        Assert.Equal(SaveStatus.Saved, service.SaveRules([.. saved.Select(r => r.Name == "WP bez MPK" ? r with { IsActive = false } : r)]).Status);
        Assert.Equal(rowId, dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunValidations)).Single(r => r["Nazwa"] == "WP bez MPK").RowId);   // historia

        // Zapytanie zmieniające dane – odrzucone przy zapisie, słownik bez zmian.
        var rejected = service.SaveRules([.. saved, new RunRule("Zła", CheckLevel.Error, true, "DELETE FROM biezace")]);
        Assert.Equal(SaveStatus.Rejected, rejected.Status);
        Assert.Contains(rejected.Issues, i => i.Message.Contains("Zapytanie musi zaczynać się od SELECT albo WITH"));
        Assert.Equal(SaveStatus.Rejected, service.SaveRules([new RunRule("Zła", CheckLevel.Error, true, "SELECT * INTO kopia FROM biezace")]).Status);
        Assert.Equal(saved.Count, store.Rules().Count);
    }
}
