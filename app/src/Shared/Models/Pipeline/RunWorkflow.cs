using PzlEv.Shared.Models;

namespace PzlEv.Shared.Models.Pipeline;

/// <summary>Akcja etapu przebiegu (słownik „Etapy przebiegów”, kolumna Akcja).</summary>
public static class RunActions
{
    public const string Import = "Import źródeł";
    public const string Pin = "Przypięcie stanu";
    public const string Checks = "Kontrole";
    public const string Approval = "Zatwierdzenie";

    public static IReadOnlyList<string> All { get; } = [Import, Pin, Checks, Approval];
}

/// <summary>Bramka etapu przy wynikach WARNING (ERROR zawsze blokuje).</summary>
public static class RunGates
{
    /// <summary>WARNING wstrzymuje przebieg do potwierdzenia z komentarzem.</summary>
    public const string Confirm = "Potwierdzenie";

    /// <summary>WARNING tylko informuje – przebieg idzie dalej.</summary>
    public const string Info = "Informacja";

    public static IReadOnlyList<string> All { get; } = [Confirm, Info];
}

/// <summary>Status etapu przebiegu (docs/pipeline-fazy.md, rozdz. 1.2).</summary>
public static class RunStepStatuses
{
    public const string Waiting = "Oczekuje";
    public const string Ready = "Do wykonania";
    public const string Running = "W toku";
    public const string Done = "Zakończony";
    public const string NeedsAction = "Wymaga akcji";
    public const string Failed = "Błąd";
    public const string Stale = "Nieaktualny";
}

/// <summary>Element kontrolny etapu: reguła walidacji (nazwa – słownik „Reguły walidacji”) zaznaczona w etapie.</summary>
public sealed record RunElementSetting(string Element, bool Active = true);

/// <summary>Etap definicji przebiegu: numer, nazwa, akcja, bramka WARNING, zatwierdzenie ręczne, elementy kontrolne (akcja Kontrole).</summary>
public sealed record RunStepDefinition(int No, string Name, string Action, string WarningGate, bool Approval, IReadOnlyList<RunElementSetting> Elements,
    string? Description = null)
{
    /// <summary>Etap kończy się wstrzymaniem do decyzji człowieka (zatwierdzenie ręczne albo akcja Zatwierdzenie).</summary>
    public bool NeedsApproval => Approval || Action == RunActions.Approval;
}

/// <summary>
/// Definicja przebiegu (workflow) – szablon etapów (ekran Przebiegi): nazwa, typ programu, dla którego jest domyślna (program bez
/// wybranego przebiegu; „Wszystkie” – każdy typ bez własnej), etapy w kolejności. BuiltIn – definicja wbudowana (bez szablonów).
/// </summary>
public sealed record RunDefinition(string Code, string ProjectType, IReadOnlyList<RunStepDefinition> Steps, string? Description = null, bool BuiltIn = false);

/// <summary>Wiersze słowników definicji przebiegów (moduł Przebiegi): szablony, etapy, elementy kontrolne etapów.</summary>
public sealed record RunDefinitionHeaderRow(string Code, string ProjectType, bool IsActive, string? Description);

public sealed record RunStepRowData(string Definition, int No, string Name, string Action, string WarningGate, bool Approval, string? Description);

public sealed record RunStepElementRow(string Definition, string Step, string Element, bool IsActive);

/// <summary>
/// Definicje przebiegów (docs/pipeline-fazy.md, rozdz. 4.1): przebieg programu – szablon wybrany w programie (Parametry programu →
/// „Przebieg”), bez wyboru – szablon domyślny dla typu programu; bez szablonów – definicja wbudowana. Etapy Import źródeł i Przypięcie
/// stanu są stałe (konfiguracja programu) – zawsze pierwsze.
/// </summary>
public static class RunDefinitions
{
    public const string BuiltInCode = "Standardowy (wbudowany)";

    /// <summary>Szablony startowe: programy wewnętrzne (i SAC – „Wszystkie”) oraz CAS.</summary>
    public const string InternalCode = "Wewnętrzny";
    public const string CasCode = "CAS";

    /// <summary>Etap stały (klocek predefiniowany): import źródeł i przypięcie stanu – korzystają z konfiguracji programu, zawsze na początku.</summary>
    public static bool IsFixed(string action) => action is RunActions.Import or RunActions.Pin;

    /// <summary>Etapy stałe na początku każdej definicji.</summary>
    public static IReadOnlyList<RunStepDefinition> FixedSteps { get; } =
    [
        new(1, "Import źródeł", RunActions.Import, RunGates.Info, false, [], "Import plików ze słownika programu „Źródła danych”."),
        new(2, "Przypięcie stanu", RunActions.Pin, RunGates.Info, false, [], "Dane ACTUALS programu w chwili przebiegu; porównanie z poprzednim przebiegiem."),
    ];

    /// <summary>Etapy definicji wbudowanej i szablonów startowych: stałe, Walidacja ACTUALS, Walidacja EV (bramka – potwierdzenie WARNING), zatwierdzenie analityka.</summary>
    public static IReadOnlyList<RunStepDefinition> StandardSteps { get; } =
    [
        .. FixedSteps,
        new(3, RunRules.ActualsArea, RunActions.Checks, RunGates.Confirm, false,
            [.. RunRules.ActualsStarters.Select(r => new RunElementSetting(r.Name))],
            "Spójność danych ACTUALS na całej puli (przed narzutami, bez Wykluczeń) – reguły walidacji w SQL (ekran Przebiegi)."),
        new(4, RunRules.EvArea, RunActions.Checks, RunGates.Confirm, false,
            [.. RunRules.EvStarters.Select(r => new RunElementSetting(r.Name))],
            "Dane jak w strukturze (bez rozliczeniowych, po Wykluczeniach, CAS po narzutach) i słowniki programu – reguły walidacji w SQL."),
        new(5, "Zatwierdzenie analityka", RunActions.Approval, RunGates.Confirm, true, [], "Analityk potwierdza wynik przebiegu (komentarz w dzienniku)."),
    ];

    /// <summary>Definicja wbudowana – obowiązuje, dopóki nie ma aktywnego szablonu (ekran Przebiegi).</summary>
    public static RunDefinition BuiltIn { get; } = new(BuiltInCode, RunRules.AllTypes, StandardSteps,
        "Definicja wbudowana – obowiązuje, dopóki na ekranie Przebiegi nie ma aktywnego szablonu przebiegu.", BuiltIn: true);

    /// <summary>Szablony startowe (ekran Przebiegi, „Utwórz szablony startowe”): Wewnętrzny – domyślny dla wszystkich typów, CAS – dla CAS.</summary>
    public static IReadOnlyList<RunDefinition> Starters { get; } =
    [
        new(InternalCode, RunRules.AllTypes, StandardSteps, "Przebieg programów wewnętrznych i SAC."),
        new(CasCode, "CAS", StandardSteps, "Przebieg programów CAS (koszty po narzutach CAS)."),
    ];

    /// <summary>
    /// Definicja przebiegu programu: aktywny szablon z etapami wybrany w programie (assigned); bez wyboru albo wybrany nieaktywny /
    /// bez etapów – szablon domyślny dla typu programu (typ przed „Wszystkie”); brak – definicja wbudowana.
    /// </summary>
    public static RunDefinition For(IReadOnlyList<RunDefinitionHeaderRow> headers, IReadOnlyList<RunStepRowData> steps, IReadOnlyList<RunStepElementRow> elements,
        string? assigned, string projectType)
    {
        var header = Available(headers, steps).FirstOrDefault(h => Is(h.Code, assigned ?? "")) ?? DefaultFor(headers, steps, projectType);
        return header is null ? BuiltIn : Build(header, steps, elements);
    }

    /// <summary>Szablon domyślny dla typu programu (aktywny, z etapami): typ programu przed „Wszystkie”; null – brak (definicja wbudowana).</summary>
    public static RunDefinitionHeaderRow? DefaultFor(IReadOnlyList<RunDefinitionHeaderRow> headers, IReadOnlyList<RunStepRowData> steps, string projectType) =>
        DefaultFor(Available(headers, steps), projectType);

    /// <summary>Szablon domyślny dla typu programu spośród szablonów do wyboru (Available).</summary>
    public static RunDefinitionHeaderRow? DefaultFor(IReadOnlyList<RunDefinitionHeaderRow> available, string projectType) =>
        available.Where(h => Is(h.ProjectType, projectType) || Is(h.ProjectType, RunRules.AllTypes))
            .OrderByDescending(h => Is(h.ProjectType, projectType)).ThenBy(h => h.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

    /// <summary>Szablony do wyboru w programie: aktywne, z etapami.</summary>
    public static IReadOnlyList<RunDefinitionHeaderRow> Available(IReadOnlyList<RunDefinitionHeaderRow> headers, IReadOnlyList<RunStepRowData> steps) =>
        headers.Where(h => h.IsActive && steps.Any(s => Is(s.Definition, h.Code))).OrderBy(h => h.Code, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Definicja ze słowników: etapy w kolejności, elementy etapu (po nazwie etapu).</summary>
    public static RunDefinition Build(RunDefinitionHeaderRow header, IReadOnlyList<RunStepRowData> steps, IReadOnlyList<RunStepElementRow> elements) =>
        new(header.Code, header.ProjectType,
            steps.Where(s => Is(s.Definition, header.Code)).OrderBy(s => s.No)
                .Select(s => new RunStepDefinition(s.No, s.Name, s.Action, s.WarningGate, s.Approval,
                    elements.Where(e => Is(e.Definition, header.Code) && Is(e.Step, s.Name))
                        .Select(e => new RunElementSetting(e.Element.Trim(), e.IsActive)).ToList(), s.Description))
                .ToList(), header.Description);

    /// <summary>
    /// Kontrola szablonu przed zapisem: nazwa; etapy stałe (Import źródeł, Przypięcie stanu) – po jednym, na początku; nazwy etapów
    /// niepuste i bez powtórzeń; etap kontroli bez aktywnego elementu – ERROR; elementy tylko w etapach kontroli; element – reguła
    /// walidacji (rules – nazwy reguł; null – reguły startowe).
    /// </summary>
    public static IReadOnlyList<Issue> Validate(RunDefinition definition, IReadOnlyCollection<string>? rules = null)
    {
        var known = new HashSet<string>(rules ?? RunRules.Starters.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        var issues = new List<Issue>();
        if (string.IsNullOrWhiteSpace(definition.Code))
            issues.Add(Issue.Error("Szablon bez nazwy"));
        var steps = definition.Steps.OrderBy(s => s.No).ToList();
        foreach (var (action, index) in new[] { (RunActions.Import, 0), (RunActions.Pin, 1) })
        {
            var count = steps.Count(s => s.Action == action);
            if (count != 1 || steps.Count <= index || steps[index].Action != action)
                issues.Add(Issue.Error($"Etap stały „{action}” – dokładnie jeden, jako etap {index + 1}"));
        }
        foreach (var step in steps)
        {
            var at = $"etap {step.No}";
            if (string.IsNullOrWhiteSpace(step.Name))
                issues.Add(Issue.Error("Etap bez nazwy", at));
            else if (steps.Count(s => Is(s.Name, step.Name)) > 1 && steps.First(s => Is(s.Name, step.Name)) != step)
                issues.Add(Issue.Error($"Nazwa etapu „{step.Name}” powtórzona", at));
            if (step.Action == RunActions.Checks && !step.Elements.Any(e => e.Active))
                issues.Add(Issue.Error($"Etap „{step.Name}” bez reguł – zaznacz reguły albo usuń etap", at));
            if (step.Action != RunActions.Checks && step.Elements.Count > 0)
                issues.Add(Issue.Error($"Etap „{step.Name}” ({step.Action}) nie ma reguł – reguły tylko w etapach walidacji", at));
            foreach (var element in step.Elements.GroupBy(e => e.Element.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                issues.Add(Issue.Error($"Reguła „{element.Key}” dwa razy w etapie „{step.Name}”", at));
            foreach (var element in step.Elements.Where(e => !known.Contains(e.Element.Trim())))
                issues.Add(Issue.Error($"Nieznana reguła „{element.Element}” – nie ma jej w regułach walidacji", at));
        }
        return issues;
    }

    private static bool Is(string? value, string expected) => string.Equals(value?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
