using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Szablony przebiegów na bazie: szablony startowe, zapis zmian z historią, usunięcie, ustawienia domyślne kontroli.</summary>
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

        // Bez etapu „Spójność ze słownikami”, walidacja z jedną kontrolą – etapy o tych samych numerach zachowują historię.
        var cas = templates[0].Definition;
        var stepIds = dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunSteps)).Where(r => r["Definicja"] == "CAS").ToDictionary(r => r["Kolejność"]!, r => r.RowId);
        var changed = cas with
        {
            Steps = [.. cas.Steps.Where(s => s.Name != RunCheckRules.EvArea)
                .Select(s => s.No == 3 ? s with { Elements = [new(RunCheckRules.CostKindDecrease.Name, Level: CheckLevel.Warning)] } : s)],
        };
        Assert.Equal(SaveStatus.Saved, service.Save(changed, isActive: true).Status);
        var reloaded = service.Load().Single(t => t.Definition.Code == "CAS").Definition;
        Assert.Equal([1, 2, 3, 4], reloaded.Steps.Select(s => s.No));
        Assert.Equal(new RunElementSetting(RunCheckRules.CostKindDecrease.Name, true, CheckLevel.Warning), Assert.Single(reloaded.Steps[2].Elements));
        Assert.Equal(stepIds["1"], dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunSteps)).Single(r => r["Definicja"] == "CAS" && r["Kolejność"] == "1").RowId);
        Assert.Equal(SaveStatus.Rejected, service.Save(changed with { Steps = changed.Steps.Skip(1).ToList() }, true).Status);   // bez importu źródeł

        Assert.Equal(SaveStatus.Saved, service.Delete("CAS").Status);
        Assert.Equal([RunDefinitions.InternalCode], service.Load().Select(t => t.Definition.Code));
        Assert.Empty(dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunStepElements)).Where(r => r["Definicja"] == "CAS"));
    }

    [SqlFact]
    public void Check_defaults_store_only_differences_from_catalog()
    {
        _database = new TestDatabase();
        var dictionaries = new DictionaryService(new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, GlobalDictionaries.Tables),
            new SqlJournal(_database.Sql, _services.Clock, _services.User));
        var service = new RunTemplateService(dictionaries, new SqlRunStore(_database.Sql));
        var settings = RunCheckRules.All.ToDictionary(r => r.Code, RunCheckRules.Default);
        settings[RunCheckRules.CostKindJump.Code] = new RunCheckSetting(true, CheckLevel.Error, 4m);

        Assert.Equal(SaveStatus.Saved, service.SaveCheckDefaults(settings).Status);

        Assert.Equal(new RunCheckSetting(true, CheckLevel.Error, 4m), service.CheckDefaults()[RunCheckRules.CostKindJump.Code]);
        Assert.Single(dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunChecks)));
        settings[RunCheckRules.CostKindJump.Code] = RunCheckRules.Default(RunCheckRules.CostKindJump);
        Assert.Equal(SaveStatus.Saved, service.SaveCheckDefaults(settings).Status);
        Assert.Empty(dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunChecks)));
    }

    [SqlFact]
    public void Validation_rules_start_from_starters_save_with_history_and_feed_the_run()
    {
        _database = new TestDatabase();
        var dictionaries = new DictionaryService(new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, GlobalDictionaries.Tables),
            new SqlJournal(_database.Sql, _services.Clock, _services.User));
        var store = new SqlRunStore(_database.Sql);
        var service = new RunTemplateService(dictionaries, store);

        var (rules, starters) = service.ValidationRules();
        Assert.True(starters);
        Assert.Equal(RunValidationRules.Starters, store.ValidationRules());

        var changed = rules.Select(r => r.Name == "Wiersze bez elementu WBS" ? r with { Level = CheckLevel.Error, Threshold = 5m } : r)
            .Where(r => r.Name != "Waluta inna niż waluta kosztów").ToList();
        Assert.Equal(SaveStatus.Saved, service.SaveValidations(changed).Status);

        var saved = store.ValidationRules();
        Assert.False(service.ValidationRules().Starters);
        Assert.Equal(changed.Count, saved.Count);
        Assert.Equal((CheckLevel.Error, 5m), saved.Where(r => r.Name == "Wiersze bez elementu WBS").Select(r => (r.Level, r.Threshold!.Value)).Single());
        Assert.Equal(RunValidationRules.Starter("Netowanie do 0 w okresach zamkniętych"), saved.Single(r => r.Name.StartsWith("Netowanie")));
        Assert.Equal(SaveStatus.Rejected, service.SaveValidations([changed[0] with { Condition = "SUMA([Kwota] > 0" }]).Status);   // błąd składni
    }
}
