using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Przebieg etapowy: wybór definicji, bramki, kolejny etap, potwierdzenie, ponowne wykonanie, reguły etapu.</summary>
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
        Assert.Equal(RunStepStatuses.NeedsAction, RunWorkflowEngine.Gate(RunDefinitions.ActualsApproval(3), 0, 0));   // zatwierdzenie danych ACTUALS
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
        Assert.Equal(RunStatuses.Errors, RunWorkflowEngine.PinStatus([Step(1, RunStepStatuses.Done, errors: 1), Step(2, RunStepStatuses.Done, warnings: 1)]));   // błędy zaakceptowane
        Assert.StartsWith("wymaga akcji: etap 2 „Etap 2” – wymaga akcji; błędy 0, ostrzeżenia 1", RunWorkflowEngine.Summary(stopped));
        Assert.StartsWith("zakończony z ostrzeżeniami: wszystkie etapy (2) zakończone", RunWorkflowEngine.Summary(done));
    }

    [Fact]
    public void Confirm_step_needing_action_or_failed_and_rerun_marks_later_steps_stale()
    {
        RunStepRow[] steps = [Step(1, RunStepStatuses.Done), Step(2, RunStepStatuses.Done), Step(3, RunStepStatuses.NeedsAction), Step(4, RunStepStatuses.Waiting)];

        Assert.True(RunWorkflowEngine.CanConfirm(steps, 3));
        Assert.False(RunWorkflowEngine.CanConfirm(steps, 2));
        Assert.False(RunWorkflowEngine.CanConfirm(steps, 4));
        Assert.True(RunWorkflowEngine.CanConfirm([Step(1, RunStepStatuses.Failed, errors: 1), Step(2, RunStepStatuses.Waiting)], 1));   // akceptacja błędów
        Assert.False(RunWorkflowEngine.CanConfirm([Step(1, RunStepStatuses.Running)], 1));
        Assert.True(RunWorkflowEngine.CanRerun(steps, 2));
        Assert.False(RunWorkflowEngine.CanRerun(steps, 4));                              // jeszcze nie wykonany
        Assert.False(RunWorkflowEngine.CanRerun([Step(1, RunStepStatuses.Failed), Step(2, RunStepStatuses.Done)], 2));   // wcześniejszy z błędem
        Assert.Equal([(2, RunStepStatuses.Ready), (3, RunStepStatuses.Stale), (4, RunStepStatuses.Waiting)], RunWorkflowEngine.Rerun(steps, 2));
        RunStepRow[] rerun = [Step(1, RunStepStatuses.Done), Step(2, RunStepStatuses.Done), Step(3, RunStepStatuses.Stale)];
        Assert.Equal(3, RunWorkflowEngine.Next(rerun)!.StepNo);                         // nieaktualny – wykonywany dalej
    }

    [Fact]
    public void Step_rules_are_active_elements_with_active_rules_and_missing_names_are_reported()
    {
        RunRule[] library =
        [
            new("Netowanie", CheckLevel.Error, true, "SELECT 1"), new("Wyłączona", CheckLevel.Warning, false, "SELECT 1"), new("Odznaczona", CheckLevel.Warning, true, "SELECT 1"),
        ];
        var step = Def(RunGates.Confirm, false, RunActions.Checks,
            new RunElementSetting("netowanie "), new RunElementSetting("Wyłączona"), new RunElementSetting("Odznaczona", Active: false), new RunElementSetting("Usunięta"));

        var (rules, missing) = RunWorkflowEngine.Rules(step, library);

        Assert.Equal(["Netowanie"], rules.Select(r => r.Name));
        Assert.Equal(["Usunięta"], missing);
    }

    private static RunDefinitionHeaderRow Header(string code, string type, bool active = true) => new(code, type, active, null);

    [Fact]
    public void Program_gets_assigned_template_else_default_for_type_else_built_in()
    {
        RunDefinitionHeaderRow[] headers =
        [
            Header(RunDefinitions.InternalCode, RunRules.AllTypes), Header("CAS", "CAS"), Header("Specjalny", "SAC"), Header("Bez etapów", "SAC"),
            Header("Wyłączony", "WEWNETRZNY", active: false),
        ];
        RunStepRowData[] steps =
        [
            .. new[] { RunDefinitions.InternalCode, "CAS", "Specjalny", "Wyłączony" }.Select(d => new RunStepRowData(d, 2, "Kontrole", RunActions.Checks, RunGates.Confirm, false, null)),
            new("CAS", 1, "Przypięcie", RunActions.Pin, RunGates.Info, false, null),
        ];
        RunStepElementRow[] elements = [new("CAS", "Kontrole", "Skok przyrostu kosztu", true), new("CAS", "Inny etap", "x", true)];

        var cas = RunDefinitions.For(headers, steps, elements, null, "CAS");

        Assert.Equal("CAS", cas.Code);
        Assert.Equal([("Przypięcie", RunActions.Pin), ("Kontrole", RunActions.Checks)], cas.Steps.Select(s => (s.Name, s.Action)));
        Assert.Equal(new RunElementSetting("Skok przyrostu kosztu"), Assert.Single(cas.Steps[1].Elements));
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
        var rule = RunRules.EvStarters[0].Name;
        var broken = new RunDefinition(" ", RunRules.AllTypes,
        [
            RunDefinitions.FixedSteps[1] with { No = 1 },
            RunDefinitions.FixedSteps[0] with { No = 2 },
            new(3, "Walidacja", RunActions.Checks, RunGates.Confirm, false, [new(rule, Active: false)]),
            new(4, "walidacja", RunActions.Checks, RunGates.Confirm, false, [new("Nieznana"), new(rule), new(rule.ToUpperInvariant())]),
            new(5, "Zatwierdzenie", RunActions.Approval, RunGates.Confirm, true, [new(rule)]),
            RunDefinitions.ActualsApproval(6),
            RunDefinitions.ActualsApproval(7) with { Name = "Zatwierdzenie ACTUALS 2" },
            RunDefinitions.EtcApproval(8),
            RunDefinitions.EtcApproval(9) with { Name = "Estymacje CAM 2" },
        ]);

        var issues = RunDefinitions.Validate(broken).Select(i => i.Message).ToList();

        Assert.Contains("Szablon bez nazwy", issues);
        Assert.Contains($"Etap stały „{RunActions.Import}” – dokładnie jeden, jako etap 1", issues);
        Assert.Contains($"Etap stały „{RunActions.Pin}” – dokładnie jeden, jako etap 2", issues);
        Assert.Contains("Etap „Walidacja” bez reguł – zaznacz reguły albo usuń etap", issues);
        Assert.Contains("Nazwa etapu „walidacja” powtórzona", issues);
        Assert.Contains("Nieznana reguła „Nieznana” – nie ma jej w regułach walidacji", issues);
        Assert.Contains($"Reguła „{rule}” dwa razy w etapie „walidacja”", issues);
        Assert.Contains("Etap „Zatwierdzenie” (Zatwierdzenie) nie ma reguł – reguły tylko w etapach walidacji", issues);
        Assert.Contains($"Klocek „{RunActions.ApproveActuals}” – najwyżej jeden w szablonie", issues);
        Assert.Contains($"Klocek „{RunActions.ApproveEtc}” – najwyżej jeden w szablonie", issues);
        Assert.Empty(RunDefinitions.Validate(RunDefinitions.Starters[0] with
            { Steps = [.. RunDefinitions.StandardSteps.Take(3), RunDefinitions.ActualsApproval(4), RunDefinitions.EtcApproval(5)] }));
        Assert.Equal(RunStepStatuses.NeedsAction, RunWorkflowEngine.Gate(RunDefinitions.EtcApproval(5), 0, 0));   // czeka na finansistę
        var misplaced = RunDefinitions.Validate(RunDefinitions.Starters[0] with
            { Steps = [.. RunDefinitions.StandardSteps.Take(4), RunDefinitions.StandardSteps[4] with { Condition = RunConditions.Closing, Notify = true }] }).Select(i => i.Message).ToList();
        Assert.Contains("Etap „Zatwierdzenie analityka” (Zatwierdzenie) – warunek „Przebieg zamykający” tylko w klocku Zatwierdzenie ETC", misplaced);
        Assert.Contains("Etap „Zatwierdzenie analityka” (Zatwierdzenie) – powiadomienie CAM tylko w klocku Zatwierdzenie ETC", misplaced);
        Assert.Empty(RunDefinitions.Validate(RunDefinitions.Starters[0] with { Steps = [.. RunDefinitions.StandardSteps.Take(3), RunDefinitions.ActualsApproval(4)] }));
        Assert.Empty(RunDefinitions.Validate(broken with { Code = "X", Steps = [.. RunDefinitions.FixedSteps, new(3, "W", RunActions.Checks, RunGates.Confirm, false, [new("Własna")])] },
            ["Własna"]));   // reguła ze słownika „Reguły walidacji”
    }

    [Fact]
    public void Template_rows_round_trip_and_replace_keeps_row_ids_of_same_keys()
    {
        var starter = RunDefinitions.Starters[1] with
        {
            Steps = [.. RunDefinitions.Starters[1].Steps.Select(s => s.No == 3 ? s with { Elements = [new("Skok przyrostu kosztu"), new("Odznaczona", Active: false)] } : s)],
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

    /// <summary>Klocek „Zatwierdzenie ETC” w słowniku „Etapy przebiegów” (kolumny Warunek, Powiadomienie CAM) i cykl estymacji programu z jego warunku.</summary>
    [Fact]
    public void Etc_approval_condition_and_notification_round_trip_and_set_weekly_or_monthly_cycle()
    {
        var weekly = new RunDefinition("Tygodniowy", RunRules.AllTypes,
            [.. RunDefinitions.FixedSteps, RunDefinitions.ActualsApproval(3), RunDefinitions.EtcApproval(4, RunConditions.Always, notify: true)]);
        var header = new RunDefinitionHeaderRow(weekly.Code, weekly.ProjectType, true, null);
        var values = RunDefinitionRows.StepValues(weekly);
        var rows = values.Select(v => new DictRow(null, null, v)).ToList();

        var read = RunDefinitions.Build(header, RunDefinitionRows.Steps(rows), []);

        Assert.Equal([null, null, null, null], values.Select(v => v["Warunek"]));                    // „Każdy przebieg” – puste
        Assert.Equal([null, null, null, "tak"], values.Select(v => v["Powiadomienie CAM"]));          // tylko w Zatwierdzeniu ETC
        Assert.Equal((RunConditions.Always, true), (read.Steps[3].Condition, read.Steps[3].Notify));
        Assert.True(RunDefinitions.CamWeekly(read));
        var monthly = weekly with { Steps = [.. RunDefinitions.FixedSteps, RunDefinitions.EtcApproval(3)] };
        Assert.Equal((RunConditions.Closing, true, false), (monthly.Steps[2].Condition, monthly.Steps[2].Notify, RunDefinitions.CamWeekly(monthly)));   // domyślnie – miesięczny, z powiadomieniem
        Assert.False(RunDefinitions.CamWeekly(RunDefinitions.BuiltIn));                                                                                // bez klocka – miesięczny
        Assert.Equal((true, false, true), (monthly.Steps[2].RunsIn(closingWeek: true), monthly.Steps[2].RunsIn(closingWeek: false), read.Steps[3].RunsIn(false)));
        Assert.Empty(RunDefinitions.Validate(weekly));
        Assert.False(RunDefinitions.Build(header, RunDefinitionRows.Steps([.. rows.Select(r => new DictRow(null, null,
            r.Values.ToDictionary(v => v.Key, v => v.Key == "Powiadomienie CAM" && v.Value is not null ? "nie" : v.Value)))]), []).Steps[3].Notify);
    }

    [Fact]
    public void Period_close_is_last_runs_only_in_closing_week_and_waits_for_approval()
    {
        var definition = new RunDefinition("Zamykający", RunRules.AllTypes, [.. RunDefinitions.FixedSteps, RunDefinitions.ActualsApproval(3), RunDefinitions.PeriodClose(4)]);
        var header = new RunDefinitionHeaderRow(definition.Code, definition.ProjectType, true, null);
        var values = RunDefinitionRows.StepValues(definition);

        var read = RunDefinitions.Build(header, RunDefinitionRows.Steps([.. values.Select(v => new DictRow(null, null, v))]), []);
        // Wiersz bez warunku (np. wpisany ręcznie w słowniku) – klocek i tak tylko w przebiegu zamykającym.
        var bare = RunDefinitions.Build(header, RunDefinitionRows.Steps([.. values.Select(v => new DictRow(null, null,
            v.ToDictionary(c => c.Key, c => c.Key == "Warunek" ? null : c.Value)))]), []);

        Assert.Empty(RunDefinitions.Validate(definition));
        Assert.Equal((RunActions.ClosePeriod, RunConditions.Closing, true), (read.Steps[3].Action, read.Steps[3].Condition, read.Steps[3].NeedsApproval));
        Assert.Equal((true, false), (bare.Steps[3].RunsIn(closingWeek: true), bare.Steps[3].RunsIn(closingWeek: false)));
        Assert.True(RunActions.IsSystem(RunActions.ClosePeriod));
        var reconcile = RunDefinitions.Reconciliation(3);
        Assert.Equal((RunActions.Reconcile, RunGates.Confirm, false, true), (reconcile.Action, reconcile.WarningGate, reconcile.NeedsApproval, RunActions.IsSystem(reconcile.Action)));
        Assert.Equal((RunStepStatuses.NeedsAction, RunStepStatuses.Failed, RunStepStatuses.Done),
            (RunWorkflowEngine.Gate(reconcile, 0, 1), RunWorkflowEngine.Gate(reconcile, 1, 0), RunWorkflowEngine.Gate(reconcile, 0, 0)));   // WARNING – potwierdzenie
        Assert.Empty(RunDefinitions.Validate(definition with { Steps = [.. RunDefinitions.FixedSteps, RunDefinitions.Reconciliation(3), RunDefinitions.PeriodClose(4)] }));
        Assert.Contains(RunDefinitions.Validate(definition with { Steps = [.. RunDefinitions.FixedSteps, RunDefinitions.PeriodClose(3), RunDefinitions.ActualsApproval(4)] }),
            i => i.Message.StartsWith("Klocek „Zamknięcie okresu” – tylko jako ostatni etap"));
        Assert.Contains(RunDefinitions.Validate(definition with { Steps = [.. RunDefinitions.FixedSteps, RunDefinitions.PeriodClose(3), RunDefinitions.PeriodClose(4) with { Name = "Drugie" }] }),
            i => i.Message == "Klocek „Zamknięcie okresu” – najwyżej jeden w szablonie");
    }

    [Fact]
    public void Built_in_definition_checks_actuals_then_ev_starter_rules_and_ends_with_approval()
    {
        var builtIn = RunDefinitions.BuiltIn;

        Assert.Equal(RunRules.ActualsStarters.Select(r => r.Name), builtIn.Steps[2].Elements.Select(e => e.Element));
        Assert.Equal(RunRules.EvStarters.Select(r => r.Name), builtIn.Steps[3].Elements.Select(e => e.Element));
        Assert.DoesNotContain(builtIn.Steps.SelectMany(s => s.Elements), e => e.Element == RunRules.ReconciliationExample.Name);   // przykład – poza szablonami
        Assert.Empty(RunDefinitions.Validate(builtIn));
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
            Row(("Definicja", "A"), ("Etap", "Kontrole"), ("Element", "Nieznana"), ("Aktywny", "tak"), ("Opis", null)),
            Row(("Definicja", "A"), ("Etap", "Kontrole"), ("Element", " "), ("Aktywny", "tak"), ("Opis", null)),
        ]);

        Assert.Single(definitions, i => i.Level == CheckLevel.Error && i.Message.StartsWith("Aktywny szablon „A” jest już domyślny dla typu programu CAS"));
        Assert.Single(steps, i => i.Level == CheckLevel.Error && i.Message.StartsWith("Etap „kontrole” już jest"));
        Assert.Equal(1, elements.Count(i => i.Level == CheckLevel.Error));   // pusty element; nieznana reguła – zapis szablonu (RunDefinitions.Validate)
    }
}
