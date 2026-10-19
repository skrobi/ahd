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

/// <summary>
/// Element kontrolny etapu: reguła katalogu (RunCheckRules – nazwa) albo reguła uzgodnień („Uzgodnienie: nazwa”; „Uzgodnienie: *” –
/// wszystkie aktywne reguły uzgodnień typu programu i wariantu); Level / Threshold null – ustawienie domyślne (słownik „Kontrole
/// przebiegu”, a bez wiersza – katalog; reguła uzgodnień – jej poziom i tolerancja).
/// </summary>
public sealed record RunElementSetting(string Element, bool Active = true, CheckLevel? Level = null, decimal? Threshold = null);

/// <summary>Etap definicji przebiegu: numer, nazwa, akcja, bramka WARNING, zatwierdzenie ręczne, elementy kontrolne (akcja Kontrole).</summary>
public sealed record RunStepDefinition(int No, string Name, string Action, string WarningGate, bool Approval, IReadOnlyList<RunElementSetting> Elements,
    string? Description = null)
{
    /// <summary>Etap kończy się wstrzymaniem do decyzji człowieka (zatwierdzenie ręczne albo akcja Zatwierdzenie).</summary>
    public bool NeedsApproval => Approval || Action == RunActions.Approval;
}

/// <summary>
/// Definicja przebiegu (workflow): kod, zakres (typ programu, program – null: wszystkie programy typu, wariant przebiegu), etapy
/// w kolejności. BuiltIn – definicja wbudowana (bez definicji w słownikach).
/// </summary>
public sealed record RunDefinition(string Code, string ProjectType, string? Project, string RunKind, IReadOnlyList<RunStepDefinition> Steps,
    string? Description = null, bool BuiltIn = false);

/// <summary>Wiersze słowników definicji przebiegów (moduł Przebiegi): definicje, etapy, elementy kontrolne etapów.</summary>
public sealed record RunDefinitionHeaderRow(string Code, string ProjectType, string? Project, string RunKind, bool IsActive, string? Description);

public sealed record RunStepRowData(string Definition, int No, string Name, string Action, string WarningGate, bool Approval, string? Description);

public sealed record RunStepElementRow(string Definition, string Step, string Element, bool IsActive, string? Level, decimal? Threshold);

/// <summary>
/// Definicje przebiegów (docs/pipeline-fazy.md, rozdz. 4.1): wybór definicji dla programu i wariantu przebiegu, definicja wbudowana
/// (bez definicji w słownikach – zakres kontroli jak dotąd, rozłożony na etapy).
/// </summary>
public static class RunDefinitions
{
    public const string ReconciliationPrefix = "Uzgodnienie: ";

    /// <summary>Wszystkie aktywne reguły uzgodnień typu programu i wariantu przebiegu.</summary>
    public const string AllReconciliations = ReconciliationPrefix + "*";

    public const string BuiltInCode = "Standardowy (wbudowany)";

    /// <summary>Elementy do wyboru w kolumnie Element: reguły katalogu, wszystkie reguły uzgodnień, reguły uzgodnień po nazwie.</summary>
    public static IReadOnlyList<string> Elements(IEnumerable<string> reconciliations) =>
        [.. RunCheckRules.All.Select(r => r.Name), AllReconciliations, .. reconciliations.Distinct(StringComparer.OrdinalIgnoreCase).Order().Select(n => ReconciliationPrefix + n)];

    public static bool IsReconciliation(string element) => element.StartsWith(ReconciliationPrefix, StringComparison.OrdinalIgnoreCase);

    public static string ReconciliationName(string element) => element[ReconciliationPrefix.Length..].Trim();

    /// <summary>Definicja wbudowana: import, przypięcie, trzy etapy kontroli (bramka – potwierdzenie WARNING), zatwierdzenie analityka.</summary>
    public static RunDefinition BuiltIn { get; } = new(BuiltInCode, RunCheckRules.AllTypes, null, RunCheckRules.AnyRun,
    [
        new(1, "Import źródeł", RunActions.Import, RunGates.Info, false, [], "Import plików ze słownika programu „Źródła danych”."),
        new(2, "Przypięcie stanu", RunActions.Pin, RunGates.Info, false, [], "Dane ACTUALS programu w chwili przebiegu; porównanie z poprzednim przebiegiem."),
        new(3, "Walidacja ACTUALS", RunActions.Checks, RunGates.Confirm, false,
            [.. new[] { RunCheckRules.Volume, RunCheckRules.Deviations, RunCheckRules.NewCostElements, RunCheckRules.NetZero, RunCheckRules.ClosedPeriods, RunCheckRules.Quality }
                .SelectMany(check => RunCheckRules.All.Where(r => r.Check == check)).Select(r => new RunElementSetting(r.Name))],
            "Kontrole danych ACTUALS względem poprzedniego przebiegu i okresów zamkniętych."),
        new(4, "Spójność ze słownikami", RunActions.Checks, RunGates.Confirm, false,
            [.. RunCheckRules.All.Where(r => r.Check == RunCheckRules.Dictionaries).Select(r => new RunElementSetting(r.Name))],
            "Cost Category, MPK i stawki, WP i budżet."),
        new(5, "Rodzaje kosztu i uzgodnienia", RunActions.Checks, RunGates.Confirm, false,
            [.. RunCheckRules.All.Where(r => r.Check == RunCheckRules.CostKinds).Select(r => new RunElementSetting(r.Name)), new RunElementSetting(AllReconciliations)],
            "Rodzaje kosztu (Hours / Materials / Other) i reguły uzgodnień."),
        new(6, "Zatwierdzenie analityka", RunActions.Approval, RunGates.Confirm, true, [], "Analityk potwierdza wynik przebiegu (komentarz w dzienniku)."),
    ], "Definicja wbudowana – obowiązuje, dopóki dla programu nie ma aktywnej definicji w słowniku „Definicje przebiegów”.", BuiltIn: true);

    /// <summary>
    /// Definicja przebiegu programu: aktywna definicja z etapami – program przed typem programu przed „Wszystkie”, wariant przebiegu
    /// (zamykający / w środku okresu) przed „Każdy”; brak – definicja wbudowana.
    /// </summary>
    public static RunDefinition For(IReadOnlyList<RunDefinitionHeaderRow> headers, IReadOnlyList<RunStepRowData> steps, IReadOnlyList<RunStepElementRow> elements,
        string project, string projectType, bool isClosing)
    {
        static bool Is(string? value, string expected) => string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        var kind = isClosing ? RunCheckRules.ClosingRun : RunCheckRules.MidPeriodRun;
        int Score(RunDefinitionHeaderRow h) =>
            (string.IsNullOrWhiteSpace(h.Project) ? Is(h.ProjectType, projectType) ? 20 : Is(h.ProjectType, RunCheckRules.AllTypes) ? 10 : -1000
                : Is(h.Project, project) ? 30 : -1000)
            + (Is(h.RunKind, kind) ? 2 : Is(h.RunKind, RunCheckRules.AnyRun) ? 1 : -1000);
        var header = headers.Where(h => h.IsActive && steps.Any(s => Is(s.Definition, h.Code)))
            .Select(h => (Header: h, Score: Score(h))).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Header.Code, StringComparer.OrdinalIgnoreCase).Select(x => x.Header).FirstOrDefault();
        return header is null ? BuiltIn : Build(header, steps, elements);
    }

    /// <summary>Definicja ze słowników: etapy w kolejności, elementy etapu (po nazwie etapu).</summary>
    public static RunDefinition Build(RunDefinitionHeaderRow header, IReadOnlyList<RunStepRowData> steps, IReadOnlyList<RunStepElementRow> elements)
    {
        static bool Is(string? value, string expected) => string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        return new RunDefinition(header.Code, header.ProjectType, string.IsNullOrWhiteSpace(header.Project) ? null : header.Project.Trim(), header.RunKind,
            steps.Where(s => Is(s.Definition, header.Code)).OrderBy(s => s.No)
                .Select(s => new RunStepDefinition(s.No, s.Name, s.Action, s.WarningGate, s.Approval,
                    elements.Where(e => Is(e.Definition, header.Code) && Is(e.Step, s.Name))
                        .Select(e => new RunElementSetting(e.Element.Trim(), e.IsActive, RunCheckRules.LevelOf(e.Level), e.Threshold)).ToList(), s.Description))
                .ToList(), header.Description);
    }
}
