using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Przebieg etapowy: wybór definicji, bramki, kolejny etap, potwierdzenie, ponowne wykonanie, ustawienia reguł etapu.</summary>
public sealed class RunWorkflowTests
{
    private static RunStepRow Step(int no, string status, int errors = 0, int warnings = 0) =>
        new(1, no, $"Etap {no}", RunActions.Checks, status, null, null, null, errors, warnings, null);

    private static RunStepDefinition Def(string gate = RunGates.Confirm, bool approval = false, string action = RunActions.Checks, params RunElementSetting[] elements) =>
        new(1, "Etap", action, gate, approval, elements);

    [Fact]
    public void Gate_stops_on_error_waits_for_confirmation_of_warnings_and_approval_otherwise_done()
    {
        Assert.Equal(RunStepStatuses.Failed, RunWorkflowEngine.Gate(Def(), errors: 1, warnings: 0));
        Assert.Equal(RunStepStatuses.NeedsAction, RunWorkflowEngine.Gate(Def(), 0, 2));
        Assert.Equal(RunStepStatuses.Done, RunWorkflowEngine.Gate(Def(RunGates.Info), 0, 2));
        Assert.Equal(RunStepStatuses.NeedsAction, RunWorkflowEngine.Gate(Def(RunGates.Info, approval: true), 0, 0));
        Assert.Equal(RunStepStatuses.NeedsAction, RunWorkflowEngine.Gate(Def(RunGates.Info, action: RunActions.Approval), 0, 0));
        Assert.Equal(RunStepStatuses.Done, RunWorkflowEngine.Gate(Def(), 0, 0));
    }

    [Fact]
    public void Next_step_runs_only_after_all_earlier_steps_are_done_and_pin_status_follows_steps()
    {
        RunStepRow[] waiting = [Step(1, RunStepStatuses.Done), Step(2, RunStepStatuses.Ready), Step(3, RunStepStatuses.Waiting)];
        RunStepRow[] stopped = [Step(1, RunStepStatuses.Done), Step(2, RunStepStatuses.NeedsAction, warnings: 1), Step(3, RunStepStatuses.Waiting)];
        RunStepRow[] failed = [Step(1, RunStepStatuses.Failed, errors: 2), Step(2, RunStepStatuses.Waiting)];
        RunStepRow[] done = [Step(1, RunStepStatuses.Done), Step(2, RunStepStatuses.Done, warnings: 1)];

        Assert.Equal(2, RunWorkflowEngine.Next(waiting)!.StepNo);
        Assert.Null(RunWorkflowEngine.Next(stopped));
        Assert.Null(RunWorkflowEngine.Next(failed));
        Assert.Null(RunWorkflowEngine.Next(done));
        Assert.Equal(RunStatuses.NeedsAction, RunWorkflowEngine.PinStatus(stopped));
        Assert.Equal(RunStatuses.Blocked, RunWorkflowEngine.PinStatus(failed));
        Assert.Equal(RunStatuses.Warnings, RunWorkflowEngine.PinStatus(done));
        Assert.Equal(RunStatuses.Ok, RunWorkflowEngine.PinStatus([Step(1, RunStepStatuses.Done)]));
        Assert.StartsWith("wymaga akcji: etap 2 „Etap 2” – wymaga akcji; błędy 0, ostrzeżenia 1", RunWorkflowEngine.Summary(stopped));
        Assert.StartsWith("zakończony z ostrzeżeniami: wszystkie etapy (2) zakończone", RunWorkflowEngine.Summary(done));
    }

    [Fact]
    public void Confirm_only_step_needing_action_and_rerun_marks_later_steps_stale()
    {
        RunStepRow[] steps = [Step(1, RunStepStatuses.Done), Step(2, RunStepStatuses.Done), Step(3, RunStepStatuses.NeedsAction), Step(4, RunStepStatuses.Waiting)];

        Assert.True(RunWorkflowEngine.CanConfirm(steps, 3));
        Assert.False(RunWorkflowEngine.CanConfirm(steps, 2));
        Assert.True(RunWorkflowEngine.CanRerun(steps, 2));
        Assert.False(RunWorkflowEngine.CanRerun(steps, 4));                              // jeszcze nie wykonany
        Assert.False(RunWorkflowEngine.CanRerun([Step(1, RunStepStatuses.Failed), Step(2, RunStepStatuses.Done)], 2));   // wcześniejszy z błędem
        Assert.Equal([(2, RunStepStatuses.Ready), (3, RunStepStatuses.Stale), (4, RunStepStatuses.Waiting)], RunWorkflowEngine.Rerun(steps, 2));
        RunStepRow[] rerun = [Step(1, RunStepStatuses.Done), Step(2, RunStepStatuses.Done), Step(3, RunStepStatuses.Stale)];
        Assert.Equal(3, RunWorkflowEngine.Next(rerun)!.StepNo);                         // nieaktualny – wykonywany dalej
    }

    [Fact]
    public void Step_settings_enable_only_its_elements_with_element_then_dictionary_then_catalog_values()
    {
        var step = Def(RunGates.Confirm, false, RunActions.Checks,
            new RunElementSetting(RunCheckRules.CostKindDecrease.Name, Level: CheckLevel.Warning),
            new RunElementSetting(RunCheckRules.WpWithoutBudget.Name, Active: false),
            new RunElementSetting(RunCheckRules.NewCostKind.Name));
        var defaults = RunCheckRules.All.ToDictionary(r => r.Code, RunCheckRules.Default);
        defaults[RunCheckRules.NewCostKind.Code] = new RunCheckSetting(true, CheckLevel.Error, 50m);   // „Kontrole przebiegu”
        defaults[RunCheckRules.CostKindDecrease.Code] = new RunCheckSetting(false, CheckLevel.Error, 5m);

        var custom = RunWorkflowEngine.Settings(step, defaults, builtIn: false);
        var builtIn = RunWorkflowEngine.Settings(step, defaults, builtIn: true);

        Assert.Equal(new RunCheckSetting(true, CheckLevel.Warning, 5m), custom[RunCheckRules.CostKindDecrease.Code]);
        Assert.Equal(new RunCheckSetting(true, CheckLevel.Error, 50m), custom[RunCheckRules.NewCostKind.Code]);
        Assert.False(custom[RunCheckRules.WpWithoutBudget.Code].Active);
        Assert.False(custom[RunCheckRules.CostCenterWithoutRate.Code].Active);                   // spoza etapu
        Assert.False(builtIn[RunCheckRules.CostKindDecrease.Code].Active);                // wbudowana – słownik „Kontrole przebiegu” wyłącza
        Assert.Equal(RunCheckRules.All.Count, custom.Count);
    }

    [Fact]
    public void Step_reconciliations_take_all_of_type_or_named_rules_with_overrides_and_report_missing()
    {
        RunReconciliationRow[] library =
        [
            new(RunCheckRules.AllTypes, "Budżet", RunCheckRules.AnyRun, true, "[Koszt brutto]", "<=", "[BAC]", null, "WARNING"),
            new("CAS", "Tylko CAS", RunCheckRules.AnyRun, true, "[BAC]", ">=", "0", null, "ERROR"),
            new(RunCheckRules.AllTypes, "Wyłączona", RunCheckRules.AnyRun, false, "[BAC]", ">=", "0", null, "ERROR"),
        ];

        var all = RunWorkflowEngine.Reconciliations(Def(elements: new RunElementSetting(RunDefinitions.AllReconciliations)), library, "SAC", false);
        var named = RunWorkflowEngine.Reconciliations(Def(elements: [new RunElementSetting("Uzgodnienie: Tylko CAS", Level: CheckLevel.Warning, Threshold: 2m),
            new RunElementSetting("Uzgodnienie: Brak")]), library, "SAC", false);

        Assert.Equal(["Budżet"], all.Rules.Select(r => r.Name));
        var rule = Assert.Single(named.Rules);
        Assert.Equal(("Tylko CAS", "WARNING", 2m, true), (rule.Name, rule.Level, rule.Tolerance!.Value, rule.IsActive));
        Assert.Equal(["Brak"], named.Missing);
    }

    private static RunDefinitionHeaderRow Header(string code, string type, bool active = true) => new(code, type, active, null);

    [Fact]
    public void Program_gets_assigned_template_else_default_for_type_else_built_in()
    {
        RunDefinitionHeaderRow[] headers =
        [
            Header(RunDefinitions.InternalCode, RunCheckRules.AllTypes), Header("CAS", "CAS"), Header("Specjalny", "SAC"), Header("Bez etapów", "SAC"),
            Header("Wyłączony", "WEWNETRZNY", active: false),
        ];
        RunStepRowData[] steps =
        [
            .. new[] { RunDefinitions.InternalCode, "CAS", "Specjalny", "Wyłączony" }.Select(d => new RunStepRowData(d, 2, "Kontrole", RunActions.Checks, RunGates.Confirm, false, null)),
            new("CAS", 1, "Przypięcie", RunActions.Pin, RunGates.Info, false, null),
        ];
        RunStepElementRow[] elements = [new("CAS", "Kontrole", RunCheckRules.CostKindDecrease.Name, true, "WARNING", 3m), new("CAS", "Inny etap", "x", true, null, null)];

        var cas = RunDefinitions.For(headers, steps, elements, null, "CAS");

        Assert.Equal("CAS", cas.Code);
        Assert.Equal([("Przypięcie", RunActions.Pin), ("Kontrole", RunActions.Checks)], cas.Steps.Select(s => (s.Name, s.Action)));
        Assert.Equal(new RunElementSetting(RunCheckRules.CostKindDecrease.Name, true, CheckLevel.Warning, 3m), Assert.Single(cas.Steps[1].Elements));
        Assert.Equal("Specjalny", RunDefinitions.For(headers, steps, elements, "specjalny", "CAS").Code);              // wybrany w programie
        Assert.Equal("Specjalny", RunDefinitions.For(headers, steps, elements, null, "SAC").Code);                     // typ przed „Wszystkie”
        Assert.Equal(RunDefinitions.InternalCode, RunDefinitions.For(headers, steps, elements, null, "WEWNETRZNY").Code);
        Assert.Equal(RunDefinitions.InternalCode, RunDefinitions.For(headers, steps, elements, "Wyłączony", "WEWNETRZNY").Code);   // nieaktywny – domyślny
        Assert.Equal("CAS", RunDefinitions.For(headers, steps, elements, "Bez etapów", "CAS").Code);                   // bez etapów – domyślny
        Assert.Equal(["CAS", "Specjalny", RunDefinitions.InternalCode], RunDefinitions.Available(headers, steps).Select(h => h.Code));
        Assert.True(RunDefinitions.For([], [], [], "X", "SAC").BuiltIn);
        var restored = RunWorkflowEngine.Deserialize(RunWorkflowEngine.Serialize(cas))!;
        Assert.Equal(cas.Steps.Count, restored.Steps.Count);
        Assert.Equal(cas.Steps[1].Elements, restored.Steps[1].Elements);
    }

    [Fact]
    public void Template_structure_requires_fixed_steps_first_and_checks_in_validation_steps()
    {
        Assert.Empty(RunDefinitions.Validate(RunDefinitions.Starters[0]));
        var broken = new RunDefinition(" ", RunCheckRules.AllTypes,
        [
            RunDefinitions.FixedSteps[1] with { No = 1 },
            RunDefinitions.FixedSteps[0] with { No = 2 },
            new(3, "Walidacja", RunActions.Checks, RunGates.Confirm, false, [new(RunCheckRules.CostKindDecrease.Name, Active: false)]),
            new(4, "walidacja", RunActions.Checks, RunGates.Confirm, false, [new("Nieznana"), new("Uzgodnienie: *"), new("uzgodnienie: *")]),
            new(5, "Zatwierdzenie", RunActions.Approval, RunGates.Confirm, true, [new(RunCheckRules.CostKindDecrease.Name)]),
        ]);

        var issues = RunDefinitions.Validate(broken).Select(i => i.Message).ToList();

        Assert.Contains("Szablon bez nazwy", issues);
        Assert.Contains($"Etap stały „{RunActions.Import}” – dokładnie jeden, jako etap 1", issues);
        Assert.Contains($"Etap stały „{RunActions.Pin}” – dokładnie jeden, jako etap 2", issues);
        Assert.Contains("Etap „Walidacja” bez kontroli – dodaj kontrole albo usuń etap", issues);
        Assert.Contains("Nazwa etapu „walidacja” powtórzona", issues);
        Assert.Contains("Nieznana kontrola „Nieznana”", issues);
        Assert.Contains("Kontrola „Uzgodnienie: *” dwa razy w etapie „walidacja”", issues);
        Assert.Contains("Etap „Zatwierdzenie” (Zatwierdzenie) nie ma kontroli – kontrole tylko w etapach walidacji", issues);
    }

    [Fact]
    public void Template_rows_round_trip_and_replace_keeps_row_ids_of_same_keys()
    {
        var starter = RunDefinitions.Starters[1] with
        {
            Steps = [.. RunDefinitions.Starters[1].Steps.Select(s => s.No == 3 ? s with { Elements = [new(RunCheckRules.CostKindDecrease.Name, true, CheckLevel.Warning, 2.5m)] } : s)],
        };
        var header = RunDefinitionRows.Headers([new DictRow(1, 1, RunDefinitionRows.Header(starter, isActive: true))]).Single();
        var steps = RunDefinitionRows.Steps(RunDefinitionRows.StepValues(starter).Select(v => new DictRow(null, null, v)));
        var elements = RunDefinitionRows.Elements(RunDefinitionRows.ElementValues(starter).Select(v => new DictRow(null, null, v)));

        var built = RunDefinitions.Build(header, steps, elements);

        Assert.Equal((starter.Code, "CAS", true), (header.Code, header.ProjectType, header.IsActive));
        Assert.Equal(starter.Steps.Select(s => (s.No, s.Name, s.Action, s.WarningGate, s.Approval)), built.Steps.Select(s => (s.No, s.Name, s.Action, s.WarningGate, s.Approval)));
        Assert.Equal(starter.Steps.SelectMany(s => s.Elements), built.Steps.SelectMany(s => s.Elements));

        var spec = GlobalDictionaries.Get(GlobalDictionaries.RunSteps);
        DictRow Step(long id, string definition, int no, string name) => new(id, 1, new Dictionary<string, string?>
            { ["Definicja"] = definition, ["Kolejność"] = no.ToString(), ["Etap"] = name, ["Akcja"] = RunActions.Checks, ["Bramka WARNING"] = RunGates.Confirm, ["Zatwierdzenie"] = "nie", ["Opis"] = null });
        DictRow[] current = [Step(1, "CAS", 1, "A"), Step(2, "CAS", 2, "B"), Step(3, "CAS", 3, "C"), Step(4, "Inny", 1, "A")];
        var (working, removed) = RunDefinitionRows.Replace(spec, current, "cas", [Step(0, "CAS", 1, "X").Values, Step(0, "CAS", 2, "B").Values]);

        Assert.Equal([4L, 1L, 2L], working.Select(r => r.RowId ?? 0));
        Assert.Equal("X", working[1]["Etap"]);
        Assert.Equal([3L], removed.Select(r => r.RowId ?? 0));
    }

    [Fact]
    public void Built_in_definition_covers_every_validation_and_catalog_rule_in_check_steps_and_ends_with_approval()
    {
        var builtIn = RunDefinitions.BuiltIn;
        var rules = builtIn.Steps.SelectMany(s => s.Elements).Select(e => e.Element).ToList();

        Assert.Equal(RunValidationRules.Starters.Select(r => r.Name).Concat(RunCheckRules.All.Select(r => r.Name)).Order(),
            rules.Where(r => !RunDefinitions.IsReconciliation(r)).Order());
        Assert.Contains(RunDefinitions.AllReconciliations, rules);
        Assert.Equal([RunActions.Import, RunActions.Pin], builtIn.Steps.Take(2).Select(s => s.Action));
        Assert.True(builtIn.Steps[^1].NeedsApproval);
    }

    [Fact]
    public void Definition_dictionaries_validate_default_type_step_names_and_elements()
    {
        DictRow Row(params (string Column, string? Value)[] values) => new(null, null, values.ToDictionary(v => v.Column, v => v.Value));
        var definitions = DictionaryValidator.Validate(GlobalDictionaries.Get(GlobalDictionaries.RunDefinitionHeaders),
        [
            Row(("Definicja", "A"), ("Typ programu", "CAS"), ("Program", null), ("Przebieg", "Każdy"), ("Aktywna", "tak"), ("Opis", null)),
            Row(("Definicja", "B"), ("Typ programu", "CAS"), ("Program", null), ("Przebieg", "Każdy"), ("Aktywna", "tak"), ("Opis", null)),
            Row(("Definicja", "C"), ("Typ programu", "CAS"), ("Program", null), ("Przebieg", "Każdy"), ("Aktywna", "nie"), ("Opis", null)),
        ]);
        var steps = DictionaryValidator.Validate(GlobalDictionaries.Get(GlobalDictionaries.RunSteps),
        [
            Row(("Definicja", "A"), ("Kolejność", "1"), ("Etap", "Kontrole"), ("Akcja", RunActions.Checks), ("Bramka WARNING", RunGates.Confirm), ("Zatwierdzenie", "nie"), ("Opis", null)),
            Row(("Definicja", "A"), ("Kolejność", "2"), ("Etap", "kontrole"), ("Akcja", RunActions.Checks), ("Bramka WARNING", RunGates.Confirm), ("Zatwierdzenie", "nie"), ("Opis", null)),
        ]);
        var elements = DictionaryValidator.Validate(GlobalDictionaries.Get(GlobalDictionaries.RunStepElements),
        [
            Row(("Definicja", "A"), ("Etap", "Kontrole"), ("Element", "Nieznana"), ("Aktywny", "tak"), ("Poziom", null), ("Próg", null), ("Opis", null)),
            Row(("Definicja", "A"), ("Etap", "Kontrole"), ("Element", "Uzgodnienie: Budżet"), ("Aktywny", "tak"), ("Poziom", null), ("Próg", "-1"), ("Opis", null)),
            Row(("Definicja", "A"), ("Etap", "Kontrole"), ("Element", RunCheckRules.CostKindDecrease.Name), ("Aktywny", "tak"), ("Poziom", "WARNING"), ("Próg", "5"), ("Opis", null)),
        ]);

        Assert.Single(definitions, i => i.Level == CheckLevel.Error && i.Message.StartsWith("Aktywny szablon „A” jest już domyślny dla typu programu CAS"));
        Assert.Single(steps, i => i.Level == CheckLevel.Error && i.Message.StartsWith("Etap „kontrole” już jest"));
        Assert.Contains(elements, i => i.Level == CheckLevel.Error && i.Message == "Próg nie może być ujemny");
        Assert.Equal(1, elements.Count(i => i.Level == CheckLevel.Error));   // nieznana kontrola – zapis szablonu (RunDefinitions.Validate)
    }
}
