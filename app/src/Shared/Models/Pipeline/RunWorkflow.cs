
namespace PzlEv.Shared.Models.Pipeline;

/// <summary>Akcja etapu przebiegu (słownik „Etapy przebiegów”, kolumna Akcja).</summary>
public static class RunActions
{
    public const string Import = "Import źródeł";
    public const string Pin = "Przypięcie stanu";
    public const string Checks = "Kontrole";
    public const string Approval = "Zatwierdzenie";

    /// <summary>
    /// Klocek systemowy: analityk zatwierdza dane ACTUALS przypięte w uruchomieniu – struktura programu pokazuje koszty z ostatniego
    /// uruchomienia z zakończonym tym etapem (docs/pipeline-fazy.md, rozdz. 4.1).
    /// </summary>
    public const string ApproveActuals = "Zatwierdzenie ACTUALS";

    /// <summary>
    /// Klocek systemowy: Finanse zatwierdzają ETC wprowadzone przez CAM-ów (estymacje programu w cyklu – kontrakt ICamReview, moduł CAM):
    /// opcjonalnie e-mail do CAM-ów z zaległościami (Powiadomienie), przegląd wymaganych WP, zatwierdź / zwróć / otwórz ponownie, akceptacja
    /// stanu z komentarzem (także z brakami). Warunek klocka wyznacza cykl estymacji programu: każdy przebieg – tygodniowy, przebieg zamykający
    /// – miesięczny (RunDefinitions.CamWeekly).
    /// </summary>
    public const string ApproveEtc = "Zatwierdzenie ETC";

    /// <summary>
    /// Klocek systemowy: łączenie źródeł (docs/pipeline-fazy.md, P3) – koszt danych uruchomienia (tabela „ev”) przypisany do WP, CAM
    /// i Cost Category jak w strukturze programu (kontrakt ICostAssignment), suma kontrolna (ERROR), koszt bez przypisania – WARNING
    /// w środku okresu (potwierdzenie „Kontynuuj bez tych elementów”), ERROR w przebiegu zamykającym (O33); wynik zapisany w uruchomieniu.
    /// </summary>
    public const string Reconcile = "Łączenie źródeł";

    /// <summary>
    /// Klocek systemowy: analityk zatwierdza zamknięcie okresu z komentarzem – uruchomienie zostaje zamrożone (przebieg tygodnia tylko
    /// do odczytu, bez nowych uruchomień; „Otwórz zamknięcie” z komentarzem zdejmuje blokadę). Zawsze ostatni, zawsze z warunkiem
    /// „Przebieg zamykający” – w innym tygodniu pominięty.
    /// </summary>
    public const string ClosePeriod = "Zamknięcie okresu";

    public static IReadOnlyList<string> All { get; } = [Import, Pin, Checks, Approval, ApproveActuals, ApproveEtc, Reconcile, ClosePeriod];

    /// <summary>Klocek systemowy – najwyżej jeden w szablonie.</summary>
    public static bool IsSystem(string action) => action is ApproveActuals or ApproveEtc or Reconcile or ClosePeriod;

    /// <summary>Klocek z warunkiem wykonania (RunConditions) i powiadomieniem CAM-ów – Zatwierdzenie ETC.</summary>
    public static bool HasCondition(string action) => action is ApproveEtc;
}

/// <summary>Warunek wykonania etapu szablonu (słownik „Etapy przebiegów”, kolumna Warunek): każdy przebieg albo tylko przebieg zamykający okres.</summary>
public static class RunConditions
{
    public const string Always = "Każdy przebieg";

    /// <summary>Tylko tydzień zamknięcia okresu w kalendarzu okresów; w innym tygodniu etap jest pomijany (zakończony bez akcji).</summary>
    public const string Closing = "Przebieg zamykający";

    public static IReadOnlyList<string> All { get; } = [Always, Closing];
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
/// <remarks>Condition – warunek wykonania (RunConditions); Notify – Zatwierdzenie ETC: e-mail do CAM-ów z zaległymi estymacjami na początku etapu.</remarks>
public sealed record RunStepDefinition(int No, string Name, string Action, string WarningGate, bool Approval, IReadOnlyList<RunElementSetting> Elements,
    string? Description = null, string Condition = RunConditions.Always, bool Notify = false)
{
    /// <summary>Etap wykonywany w tygodniu przebiegu (warunek „Przebieg zamykający” – tylko w tygodniu zamknięcia okresu).</summary>
    public bool RunsIn(bool closingWeek) => (Condition != RunConditions.Closing && Action != RunActions.ClosePeriod) || closingWeek;

    /// <summary>
    /// Etap kończy się wstrzymaniem do decyzji człowieka (zatwierdzenie ręczne albo akcja Zatwierdzenie / Zatwierdzenie ACTUALS / ETC /
    /// Zamknięcie okresu).
    /// </summary>
    public bool NeedsApproval => Approval || Action is RunActions.Approval or RunActions.ApproveActuals or RunActions.ApproveEtc or RunActions.ClosePeriod;
}

/// <summary>
/// Definicja przebiegu (workflow) – szablon etapów (ekran Przebiegi): nazwa, typ programu, dla którego jest domyślna (program bez
/// wybranego przebiegu; „Wszystkie” – każdy typ bez własnej), etapy w kolejności. BuiltIn – definicja wbudowana (bez szablonów).
/// </summary>
public sealed record RunDefinition(string Code, string ProjectType, IReadOnlyList<RunStepDefinition> Steps, string? Description = null, bool BuiltIn = false);

/// <summary>Wiersze słowników definicji przebiegów (moduł Przebiegi): szablony, etapy, elementy kontrolne etapów.</summary>
public sealed record RunDefinitionHeaderRow(string Code, string ProjectType, bool IsActive, string? Description);

public sealed record RunStepRowData(string Definition, int No, string Name, string Action, string WarningGate, bool Approval, string? Description,
    string? Condition = null, bool? Notify = null);

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

    /// <summary>Klocek systemowy „Zatwierdzenie ACTUALS” (edytor szablonów) – najwyżej jeden w szablonie, po przypięciu stanu.</summary>
    public static RunStepDefinition ActualsApproval(int no) =>
        new(no, "Zatwierdzenie danych ACTUALS", RunActions.ApproveActuals, RunGates.Info, true, [],
            "Analityk zatwierdza dane ACTUALS przebiegu (komentarz) – od zatwierdzenia struktura programu pokazuje koszty z tego przebiegu.");

    /// <summary>
    /// Klocek systemowy „Zatwierdzenie ETC” (edytor szablonów) – najwyżej jeden: Finanse zatwierdzają ETC CAM-ów; warunek – cykl estymacji programu
    /// (domyślnie przebieg zamykający – miesięczny), notify – e-mail do CAM-ów z zaległościami na początku etapu.
    /// </summary>
    public static RunStepDefinition EtcApproval(int no, string condition = RunConditions.Closing, bool notify = true) =>
        new(no, "Zatwierdzenie ETC", RunActions.ApproveEtc, RunGates.Info, true, [],
            "Finanse zatwierdzają ETC wprowadzone przez CAM-ów (estymacje programu w cyklu): powiadomienie CAM-ów z zaległościami, zatwierdź / zwróć / otwórz ponownie, akceptacja z komentarzem.",
            condition, notify);

    /// <summary>Klocek systemowy „Łączenie źródeł” (edytor szablonów) – najwyżej jeden; WARNING czeka na potwierdzenie.</summary>
    public static RunStepDefinition Reconciliation(int no) =>
        new(no, "Łączenie źródeł", RunActions.Reconcile, RunGates.Confirm, false, [],
            "Koszt uruchomienia przypisany do WP, CAM i Cost Category jak w strukturze programu; suma kontrolna; koszt bez przypisania – w środku okresu ostrzeżenie (Kontynuuj bez tych elementów), w przebiegu zamykającym błąd.");

    /// <summary>
    /// Klocek systemowy „Zamknięcie okresu” (edytor szablonów) – najwyżej jeden, ostatni etap; warunek zawsze „Przebieg zamykający”.
    /// </summary>
    public static RunStepDefinition PeriodClose(int no) =>
        new(no, "Zamknięcie okresu", RunActions.ClosePeriod, RunGates.Info, true, [],
            "Analityk zatwierdza zamknięcie okresu (komentarz) – uruchomienie zostaje zamrożone: przebieg tygodnia tylko do odczytu, bez nowych uruchomień; „Otwórz zamknięcie” z komentarzem zdejmuje blokadę.",
            RunConditions.Closing);

    /// <summary>
    /// Cykl estymacji CAM programu z jego przebiegu: klocek „Zatwierdzenie ETC” z warunkiem „Każdy przebieg” – tygodniowy (estymacja co tydzień),
    /// inaczej (przebieg zamykający albo bez klocka) – miesięczny (okres zamknięcia).
    /// </summary>
    public static bool CamWeekly(RunDefinition definition) =>
        definition.Steps.Any(s => s.Action == RunActions.ApproveEtc && s.Condition != RunConditions.Closing);

    /// <summary>
    /// Etapy definicji wbudowanej i szablonów startowych (jak migracja 053): stałe, Walidacja ACTUALS, Walidacja EV (bramka – potwierdzenie
    /// WARNING), zatwierdzenie analityka. Zatwierdzenie ACTUALS – klocek dokładany w edytorze szablonów.
    /// </summary>
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
                        .Select(e => new RunElementSetting(e.Element.Trim(), e.IsActive)).ToList(), s.Description,
                    s.Condition == RunConditions.Closing || s.Action == RunActions.ClosePeriod ? RunConditions.Closing : RunConditions.Always, s.Notify == true))
                .ToList(), header.Description);

    /// <summary>
    /// Kontrola szablonu przed zapisem: nazwa; etapy stałe (Import źródeł, Przypięcie stanu) – po jednym, na początku; klocki systemowe
    /// (Zatwierdzenie ACTUALS, Zatwierdzenie ETC, Zamknięcie okresu) – najwyżej po jednym, Zamknięcie okresu – ostatni; nazwy etapów
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
        foreach (var system in steps.Where(s => RunActions.IsSystem(s.Action)).GroupBy(s => s.Action).Where(g => g.Count() > 1))
            issues.Add(Issue.Error($"Klocek „{system.Key}” – najwyżej jeden w szablonie"));
        if (steps.Count > 0 && steps.Any(s => s.Action == RunActions.ClosePeriod) && steps[^1].Action != RunActions.ClosePeriod)
            issues.Add(Issue.Error($"Klocek „{RunActions.ClosePeriod}” – tylko jako ostatni etap (zamraża uruchomienie)"));
        foreach (var step in steps)
        {
            var at = $"etap {step.No}";
            if (string.IsNullOrWhiteSpace(step.Name))
                issues.Add(Issue.Error("Etap bez nazwy", at));
            else if (steps.Count(s => Is(s.Name, step.Name)) > 1 && steps.First(s => Is(s.Name, step.Name)) != step)
                issues.Add(Issue.Error($"Nazwa etapu „{step.Name}” powtórzona", at));
            if (step.Action == RunActions.Checks && !step.Elements.Any(e => e.Active))
                issues.Add(Issue.Error($"Etap „{step.Name}” bez reguł – zaznacz reguły albo usuń etap", at));
            if (step.Condition == RunConditions.Closing && !RunActions.HasCondition(step.Action) && step.Action != RunActions.ClosePeriod)
                issues.Add(Issue.Error($"Etap „{step.Name}” ({step.Action}) – warunek „{RunConditions.Closing}” tylko w klocku {RunActions.ApproveEtc}", at));
            if (step.Notify && step.Action != RunActions.ApproveEtc)
                issues.Add(Issue.Error($"Etap „{step.Name}” ({step.Action}) – powiadomienie CAM tylko w klocku {RunActions.ApproveEtc}", at));
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
