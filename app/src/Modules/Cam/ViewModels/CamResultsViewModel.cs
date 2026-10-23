using PzlEv.Modules.Cam.Services;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Cam.ViewModels;

/// <summary>Wiersz tabeli wyników EV (WP albo suma): teksty wartości w walucie wyników i poziomy odchyleń CPI / SPI / VAC.</summary>
public sealed record CamResultRow(string Program, string Wp, string Name, string Finish, bool FinishShifted, string Complete, string Spent, string Bac,
    string Bcws, string Bcwp, string Acwp, string Cv, string Sv, string Cpi, string? CpiTone, string Spi, string? SpiTone, string Etc, string Eac, string Ieac,
    string Vac, bool VacNegative, bool IsTotal = false);

/// <summary>Odchylenie WP do wyjaśnienia (CPI / SPI poniżej progów).</summary>
public sealed record CamDeviation(string Level, string Tone, string Title, string Text);

/// <summary>
/// Wyniki EV CAM-a (tasks/F11.5) – tylko odczyt: WP ze wszystkich programów w jednej walucie, zakres razem / robocizna / materiały,
/// sumy z wskaźnikami liczonymi z sum, odchylenia według progów (CamResults).
/// </summary>
public sealed class CamResultsViewModel : ObservableObject
{
    private IReadOnlyList<CamWorkPackage> _wps = [];
    private CamScope _scope = CamScope.All;
    private CamCurrency _currency = CamCurrency.Pln;
    private bool _currencyChosen;

    public IReadOnlyList<CamResultRow> Rows { get; private set; } = [];

    public IReadOnlyList<SummaryTile> Tiles { get; private set; } = [];

    public IReadOnlyList<CamDeviation> Deviations { get; private set; } = [];

    public string Note { get; private set; } = "";

    public bool ScopeAll { get => _scope == CamScope.All; set { if (value) SetScope(CamScope.All); } }

    public bool ScopeLabor { get => _scope == CamScope.Labor; set { if (value) SetScope(CamScope.Labor); } }

    public bool ScopeMaterial { get => _scope == CamScope.Material; set { if (value) SetScope(CamScope.Material); } }

    public bool CurrencyProgram { get => _currency == CamCurrency.Program; set { if (value) SetCurrency(CamCurrency.Program); } }

    public bool CurrencyPln { get => _currency == CamCurrency.Pln; set { if (value) SetCurrency(CamCurrency.Pln); } }

    public bool CurrencyUsd { get => _currency == CamCurrency.Usd; set { if (value) SetCurrency(CamCurrency.Usd); } }

    public bool CanUseProgramCurrency => CamResults.SingleCurrency(_wps);

    public void Load(IReadOnlyList<CamWorkPackage> wps)
    {
        _wps = wps;
        // Domyślnie waluta programu (gdy wspólna); waluta wybrana przez użytkownika zostaje przy odświeżeniu i zmianie CAM-a.
        if (!_currencyChosen && CamResults.SingleCurrency(wps))
            _currency = CamCurrency.Program;
        else if (_currency == CamCurrency.Program)
            _currency = CamCurrency.Pln;
        Recalculate();
    }

    private void SetScope(CamScope scope)
    {
        _scope = scope;
        Recalculate();
    }

    private void SetCurrency(CamCurrency currency)
    {
        _currencyChosen = true;
        _currency = currency == CamCurrency.Program && !CamResults.SingleCurrency(_wps) ? CamCurrency.Pln : currency;
        Recalculate();
    }

    private void Recalculate()
    {
        static string K(decimal? v) => v is { } x ? PolishNumber.ToDisplay(Math.Round(x / 1000, 1), 1) : "—";
        static string Signed(decimal? v) => v is { } x ? (x > 0 ? "+" : "") + K(x) : "—";
        static string Index(decimal? v) => v is { } x ? PolishNumber.ToDisplay(Math.Round(x, 2), 2) : "—";
        static string Percent(decimal? v) => v is { } x ? PolishNumber.ToDisplay(Math.Round(x, 0), 0) + "%" : "—";
        CamResultRow Row(string program, string wp, string name, string finish, bool shifted, CamValues v, bool total = false) =>
            new(program, wp, name, finish, shifted, Percent(v.PercentComplete), Percent(v.PercentSpent), K(v.Bac), K(v.Bcws), K(v.Bcwp), K(v.Acwp),
                Signed(v.Cv), Signed(v.Sv), Index(v.Cpi), CamResults.Tone(v.Cpi), Index(v.Spi), CamResults.Tone(v.Spi), K(v.Etc), K(v.Eac), K(v.Ieac),
                Signed(v.Vac), v.Vac is < 0, total);

        var rows = new List<CamResultRow>();
        var deviations = new List<CamDeviation>();
        foreach (var wp in _wps.OrderBy(w => w.Project, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Wp, StringComparer.OrdinalIgnoreCase))
        {
            if (CamResults.Factor(wp, _currency) is not { } factor || CamResults.Of(wp, _scope, factor) is not { } values)
                continue;
            var shifted = string.CompareOrdinal(wp.ForecastFinish, wp.BaselineFinish) > 0 && wp.BaselineFinish is not null;
            rows.Add(Row(wp.Project, wp.Wp, wp.Name, $"{wp.BaselineFinish ?? "—"} → {wp.ForecastFinish ?? wp.BaselineFinish ?? "—"}", shifted, values));
            var worst = new[] { values.Cpi, values.Spi }.Where(x => x is not null).DefaultIfEmpty().Min();
            if (CamResults.Tone(worst) is "warn" or "crit")
                deviations.Add(new CamDeviation(CamResults.Tone(worst) == "crit" ? "WYJAŚNIJ" : "OSTRZEŻENIE", CamResults.Tone(worst)!,
                    $"{wp.Wp} ({wp.Project}) – CPI {Index(values.Cpi)}, SPI {Index(values.Spi)}",
                    CamResults.Tone(worst) == "crit" ? "Poniżej progu 0,90 – wyjaśnienie odchylenia (przyczyna, wpływ, działania) w uzasadnieniu estymacji."
                        : "Poniżej progu 0,95 – sprawdź estymację i prognozę końca."));
        }
        var (total, skipped) = CamResults.Total(_wps, _scope, _currency);
        if (rows.Count > 0)
            rows.Add(Row("Razem", $"{rows.Count} WP", "", "", false, total, total: true));
        Rows = rows;
        Deviations = deviations;
        var unit = _currency switch { CamCurrency.Pln => "tys. PLN", CamCurrency.Usd => "tys. USD", _ => $"tys. {_wps.FirstOrDefault()?.Currency}" };
        Tiles =
        [
            new SummaryTile("BAC", K(total.Bac) + " " + unit),
            new SummaryTile("BCWP (EV)", K(total.Bcwp), CamResults.Tone(total.Spi) ?? ""),
            new SummaryTile("ACWP", K(total.Acwp)),
            new SummaryTile("CPI", Index(total.Cpi), CamResults.Tone(total.Cpi) ?? ""),
            new SummaryTile("SPI", Index(total.Spi), CamResults.Tone(total.Spi) ?? ""),
            new SummaryTile("EAC", K(total.Eac)),
            new SummaryTile("VAC", Signed(total.Vac), total.Vac is < 0 ? "crit" : "ok"),
            new SummaryTile("TCPI do EAC", Index(total.Tcpi), total.Tcpi is { } t && total.Cpi is { } c && Math.Abs(t - c) > 0.10m ? "crit" : ""),
        ];
        var notes = new List<string> { $"Kwoty w {unit}; wskaźniki z sum (nie średnie); ETC – zatwierdzone estymacje i ręczne ETC w „Harmonogram i budżet”, bez nich – z produkcji." };
        if (_scope != CamScope.All)
            notes.Add("BCWS, BCWP i wskaźniki – tylko w zakresie „Razem” (metoda EV robocizny i materiałów osobno – O35).");
        if (_wps.Any(w => w.Bcwp is null))
            notes.Add("WP bez danych produkcji nie mają BCWP (metoda EV WP bez harmonogramu – O35).");
        if (skipped > 0)
            notes.Add($"{skipped} WP bez kursu waluty – poza sumą.");
        foreach (var note in _wps.Select(w => w.DataNote).OfType<string>().Distinct())
            notes.Add(note);
        Note = string.Join(" ", notes);
        OnPropertyChanged(string.Empty);
    }
}
