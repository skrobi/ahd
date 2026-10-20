using System.Collections.ObjectModel;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Process;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Ekran programu – zakładka Wskaźniki: status danych (ACTUALS struktury, RABIT, produkcja, waluta), wskaźniki, odczyt kosztów ACTUALS.</summary>
public sealed partial class ProjectDetailViewModel
{
    /// <summary>Z kiedy są dane RABIT (ACTUALS) – data raportu w RABIT, import, ostatnie sprawdzenie (nagłówek).</summary>
    public string DataInfo => _actuals?.Summary ?? "Dane RABIT: wczytywanie…";

    public string DataInfoDetails => _actuals?.Details ?? "";

    /// <summary>Skąd są koszty struktury: zatwierdzony przebieg (stan danych z przypięcia) albo ostatni import – niezatwierdzone.</summary>
    public string StructureDataInfo => _costs is null ? "ACTUALS struktury: wczytywanie…" : ApprovedActuals.Summary(_approved);

    public string StructureDataDetails => ApprovedActuals.Details(_approved);

    /// <summary>Koszty struktury z ostatniego importu (brak zatwierdzonego przebiegu) – tag ostrzegawczy.</summary>
    public bool StructureDataUnapproved => _costs is not null && _approved is null;

    /// <summary>Waluta projektu w statusie (zakładka Wskaźniki): kod z danych ACTUALS i zestaw walutowy („Parametry projektu”).</summary>
    public string CurrencyInfo
    {
        get
        {
            var set = ProjectSettings.SetOf(_costsField);
            return _currencyProblem is not null ? $"Waluta: ⚠ kilka walut ({set.CurrencyColumn})"
                : $"Waluta: {_currency ?? "—"} ({set.CurrencyColumn} / {set.AmountColumn})";
        }
    }

    public string CurrencyDetails => _currencyProblem
        ?? $"Zestaw walutowy ACTUALS (Konfiguracja → „Parametry programu”): kwota {ProjectSettings.SetOf(_costsField).AmountColumn}, waluta z kolumny "
           + $"{ProjectSettings.SetOf(_costsField).CurrencyColumn} ostatniego importu ACTUALS programu{(_currency is null ? " – brak danych" : "")}. "
           + "W tej walucie: ACWP, raport kosztów, materiały z vAPD (USD przeliczone kursem bieżącym), BAC Material, BAC Cost, ETC Cost i EAC; "
           + "kwoty „Harmonogram i budżet” zapisują walutę programu (wpisane w innej walucie – przeliczone).";


    /// <summary>Dane produkcyjne (PZLPROD na żywo): chwila odczytu albo powód braku (status).</summary>
    public string ProductionInfo => _production is null ? "Produkcja: wczytywanie…"
        : _production.Error is null ? $"Produkcja (PZLPROD) {_production.ReadAt:yyyy-MM-dd HH:mm}" : "Produkcja: brak danych";

    public string ProductionDetails => _production is null ? ""
        : (_production.Error ?? $"Operational EV i Actual Material odczytane na żywo z PZLPROD (LOG.vAHDD, LOG.vAPD) o {_production.ReadAt:yyyy-MM-dd HH:mm} – " +
            $"elementów z danymi: {_production.ByPspnr.Count}. Ponowny odczyt: „Odśwież P1S i koszty”.")
          + (_production.Note is { } note ? $"\nUwaga: {note}" : "")
          + "\nZapytanie: sql/pzlprod/produkcja.sql; parametry – słowniki „Wskaźniki DJK” i „Parametry produkcji”; wydzielenia (np. Paint) – węzły wirtualne nakładki z kodem P1S ELEMENT_P1S.NAZWA.CPLGR.";

    /// <summary>Wskaźniki w grupach jak w tabeli struktury: Operational EV, Materials, Financial EV.</summary>
    public ObservableCollection<KpiGroup> Kpis { get; } = [];

    /// <summary>Wskaźniki EV – liczone w przebiegach (docs/ev-obliczenia.md); do tego czasu kafelki bez wartości.</summary>
    private static readonly IReadOnlyList<KpiTile> EvKpis =
        new[] { "BCWS", "BCWP", "CPI", "SPI", "EAC" }.Select(k => new KpiTile(k, "—", "po obliczeniu EV w przebiegu", Pending: true)).ToList();

    /// <summary>Daty danych ACTUALS (w tle); błąd odczytu – informacja w statusie (zakładka Wskaźniki), ekran działa dalej.</summary>
    private ActualsFreshness ReadActuals()
    {
        try
        {
            return _service.ActualsFreshness();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Daty danych ACTUALS programu {Code}", Code);
            return new ActualsFreshness([], ex.Message);
        }
    }

    /// <summary>ACWP i Actual DLH po elemencie CES, błąd odczytu kosztów, pole kwoty ACTUALS i zatwierdzony przebieg (stan danych).</summary>
    private sealed record CostData(ElementActuals Actuals, string? Error, string Field, string? Currency = null, string? CurrencyProblem = null,
        ApprovedActuals? Approved = null);

    /// <summary>
    /// ACWP po elemencie CES (w tle) z pola kwoty ACTUALS projektu i Actual DLH (Total Quantity) w stanie danych ostatniego przebiegu
    /// z zatwierdzonymi danymi ACTUALS (bez niego – ostatni import); cached – koszty w pamięci, użyte przy tym samym polu i tym samym
    /// zatwierdzonym przebiegu – jedno wywołanie procedury. Brak danych lub procedury – pusta lista i powód (struktura bez kosztów);
    /// brak pola godzin w parserze – kolumna Actual DLH pusta.
    /// </summary>
    private CostData ReadCosts(StepTimer steps, CostData? cached)
    {
        var field = ProjectSettings.DefaultActualsAmount;
        try
        {
            field = steps.Time("pole kwoty ACTUALS", () => _service.ActualsAmount(Code));
            var approved = steps.Time("zatwierdzony przebieg (ACTUALS)", ReadApproved);
            if (cached is { } c && c.Field == field && c.Approved == approved)
                return c;
            var actuals = steps.Time("koszty ACWP i godziny Actual DLH (ACTUALS)", () => _service.CostsAndHoursByElement(Code, field, approved?.AsOf));
            var (currency, problem) = steps.Time("waluta programu (ACTUALS)", () => _service.ProjectCurrency(Code, field));
            return new CostData(actuals, null, field, currency, problem, approved);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Koszty programu {Code}", Code);
            return new CostData(ElementActuals.Empty, $"koszty (ACWP) niedostępne: {ex.Message}", field);
        }
    }

    /// <summary>Zatwierdzony przebieg; błąd odczytu – null (koszty z ostatniego importu, tag „niezatwierdzone”).</summary>
    private ApprovedActuals? ReadApproved()
    {
        try
        {
            return _service.ApprovedActuals(Code);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Zatwierdzony przebieg programu {Code}", Code);
            return null;
        }
    }

    private void ShowKpis(StructureSummary summary)
    {
        static string Amount(decimal value) => PzlEv.Shared.Utils.Files.PolishNumber.ToDisplay(Math.Round(value, 2));
        Kpis.Clear();
        Kpis.Add(new KpiGroup(StructureColumns.OperationalEv,
            [new KpiTile("BAC HOURS", Amount(summary.BacHours), "budżet godzin – Harmonogram i budżet")]));
        Kpis.Add(new KpiGroup(StructureColumns.Materials,
            [new KpiTile("BAC MATERIAL", Amount(summary.BacMaterial), "budżet materiałów")]));
        Kpis.Add(new KpiGroup(StructureColumns.FinancialEv,
        [
            new KpiTile("BAC", Amount(summary.Bac), "budżet kosztowy – Harmonogram i budżet"),
            _costsError is null
                ? new KpiTile("ACWP", Amount(summary.Acwp), ProjectProcess.AcwpDescription(_currency)
                    ?? $"koszt rzeczywisty narastająco – {ProjectSettings.SetOf(_costsField).AmountColumn}, {_currency ?? ProjectSettings.CurrencyOf(_costsField)}; "
                       + $"{(_approved is { } a ? $"dane zatwierdzone w przebiegu {a.RunCode} #{a.PinNo}" : "ostatni import ACTUALS (niezatwierdzone)")}, bez wykluczeń")
                : new KpiTile("ACWP", "—", _costsError, Pending: true),
            new KpiTile("Koszt bez WP", Amount(summary.AcwpWithoutWp + summary.AcwpOutside),
                $"bez przypisania do WP: {Amount(summary.AcwpWithoutWp)} (brak WP / kilka WP – O45); elementy CES spoza nakładki: {Amount(summary.AcwpOutside)}"),
            .. EvKpis,
        ]));
    }
}
