using System.Collections.ObjectModel;
using PzlEv.Modules.Cam.Models;
using PzlEv.Modules.Cam.Services;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Cam.ViewModels;

/// <summary>Wiersz roku w formularzu (godziny albo materiały): propozycja systemu, wartość CAM (tekst – liczba po polsku), różnica, stawka i koszt.</summary>
public sealed class YearRowViewModel(EstimateEditorViewModel owner, char kind, int year, decimal? proposal, decimal value) : ObservableObject
{
    private string _valueText = PolishNumber.ToDisplay(value, kind == CamLineKinds.Hours ? 0 : 2);
    private decimal _value = value;

    public char Kind { get; } = kind;

    public int Year { get; } = year;

    public decimal? Proposal { get; } = proposal;

    /// <summary>Edycja wartości – jak formularz (po wysłaniu zablokowana).</summary>
    public bool Editable => owner.Editable;

    public void EditableChanged() => OnPropertyChanged(nameof(Editable));

    public decimal Value => _value;

    public string ProposalText => Proposal is { } p ? PolishNumber.ToDisplay(p, Kind == CamLineKinds.Hours ? 0 : 2) : "—";

    /// <summary>Wartość CAM; tekst nie-liczba albo ujemny – poprzednia wartość zostaje (komunikat w Error).</summary>
    public string ValueText
    {
        get => _valueText;
        set
        {
            if (!SetProperty(ref _valueText, value))
                return;
            Error = PolishNumber.TryParse(string.IsNullOrWhiteSpace(value) ? "0" : value, out var parsed) && parsed >= 0 ? null : "liczba ≥ 0";
            if (Error is null)
                _value = parsed;
            OnPropertyChanged(nameof(Error));
            OnPropertyChanged(nameof(DiffText));
            OnPropertyChanged(nameof(Changed));
            owner.Recalculate();
        }
    }

    public string? Error { get; private set; }

    public bool Changed => _value != (Proposal ?? 0);

    public string DiffText => Changed ? (_value - (Proposal ?? 0) > 0 ? "+" : "") + PolishNumber.ToDisplay(_value - (Proposal ?? 0), Kind == CamLineKinds.Hours ? 0 : 2) : "0";

    public string RateText { get; set; } = "";

    public bool RateFallback { get; set; }

    public string CostText { get; set; } = "";

    public void Refresh(string rate, bool fallback, string cost)
    {
        (RateText, RateFallback, CostText) = (rate, fallback, cost);
        OnPropertyChanged(nameof(RateText));
        OnPropertyChanged(nameof(RateFallback));
        OnPropertyChanged(nameof(CostText));
    }
}

/// <summary>Kafelek „Co trafi do Finansów” albo pozycja punktu odniesienia (Tone: "", "ok", "warn", "crit").</summary>
public sealed record SummaryTile(string Label, string Value, string Tone = "");

/// <summary>
/// Formularz estymacji WP (docs/funkcjonalnosc.md, moduł CAM): 1 daty prognozy, 2 godziny w latach, 3 materiały w latach,
/// 4 uzasadnienie i wysyłka z podsumowaniem dla Finansów; punkt odniesienia i historia. Zapis – rodzic (CamViewModel).
/// </summary>
public sealed class EstimateEditorViewModel : ObservableObject
{
    private DateTime? _forecastStart;
    private DateTime? _forecastFinish;
    private string _justification;
    private string _message = "";
    private string _errors = "";

    public EstimateEditorViewModel(CamWorkPackage wp, CamEstimate estimate, IReadOnlyList<CamEstimateEvent> history, CamEstimate? lastApproved,
        Func<EstimateEditorViewModel, bool, Task> save)
    {
        Wp = wp;
        Estimate = estimate;
        Editable = CamStatuses.Editable(estimate.Status);
        _forecastStart = estimate.ForecastStart?.ToDateTime(TimeOnly.MinValue);
        _forecastFinish = estimate.ForecastFinish?.ToDateTime(TimeOnly.MinValue);
        _justification = estimate.Justification ?? "";
        foreach (var line in estimate.Of(CamLineKinds.Hours))
            Hours.Add(new YearRowViewModel(this, CamLineKinds.Hours, line.Year, line.Proposal, line.Value));
        foreach (var line in estimate.Of(CamLineKinds.Material))
            Materials.Add(new YearRowViewModel(this, CamLineKinds.Material, line.Year, line.Proposal, line.Value));
        History = history.Select(h => $"{h.At.LocalDateTime:yyyy-MM-dd HH:mm} · {h.By} · {h.Status}: {h.Summary}" + (h.Comment is null ? "" : $" – {h.Comment}")).ToList();
        Context =
        [
            new SummaryTile("BAC godziny", Number(wp.BacHours, 0) + " h"),
            new SummaryTile("Poniesione godziny", wp.ActualHours is { } h ? $"{Number(h, 0)} h" + (wp.BacHours != 0 ? $" ({Number(h / wp.BacHours * 100, 0)}%)" : "") : "—"),
            new SummaryTile("BAC materiały", $"{Number(wp.BacMaterial, 0)} {wp.Currency}"),
            new SummaryTile("Poniesione materiały", $"{Number(wp.AcwpMaterial, 0)} {wp.Currency}"),
            new SummaryTile("Stawka MPK " + (wp.Mpk ?? "—"), wp.HourRates.Count == 0 ? "brak w „Stawki wydziałów”" : string.Join(" · ", wp.HourRates.OrderBy(r => r.Key).Select(r => $"{r.Key}: {Number(r.Value, 2)}"))),
            new SummaryTile("Baseline", $"{wp.BaselineStart ?? "—"} → {wp.BaselineFinish ?? "—"}"),
            new SummaryTile("Plan zleceń AHD (DATA_PLAN)", $"{wp.PlanStart ?? "—"} → {wp.PlanFinish ?? "—"}"),
            new SummaryTile("Poprzednia zatwierdzona", lastApproved is null ? "—" : $"{Number(lastApproved.Total(CamLineKinds.Hours), 0)} h · {Number(lastApproved.Total(CamLineKinds.Material), 0)} {lastApproved.Currency} · cykl {lastApproved.Cycle}"),
        ];
        // Wartość, której nie da się odczytać, zostawia w wierszu poprzednią liczbę – zapis odmówiony, żeby nie wysłać innej wartości niż widoczna.
        Task Save(bool submit)
        {
            if (!HasInvalidValues)
                return save(this, submit);
            Message = "Nie zapisano – popraw wartości oznaczone w tabeli.";
            return Task.CompletedTask;
        }
        SaveDraft = new AsyncRelayCommand(() => Save(false), () => Editable);
        Submit = new AsyncRelayCommand(() => Save(true), () => Editable);
        Recalculate();
    }

    public CamWorkPackage Wp { get; }

    public CamEstimate Estimate { get; private set; }

    public bool Editable { get; private set; }

    public string Title => Wp.Name;

    public string Subtitle => $"{Wp.Project} · {Wp.Wp} · {(Wp.Production ? "produkcyjny (AHD)" : "bez harmonogramu")} · MPK {Wp.Mpk ?? "—"} · waluta {Wp.Currency ?? "—"}";

    public string Status => Estimate.Status;

    public string? ReviewNote => Estimate.Status == CamStatuses.Returned ? Estimate.ReviewNote : null;

    public bool HasReviewNote => ReviewNote is not null;

    public string HoursSource => Wp.Production
        ? "Propozycja: ETC z AHD (TECH_DO_PON ÷ produktywność IPT + DJK) według planowanego roku poniesienia (R); rok miniony – rok bieżący, operacje bez roku – rozłożone na dni robocze do prognozowanego końca."
        : "Propozycja: BAC godzin − poniesione, rozłożone na dni robocze do prognozowanego końca.";

    public string MaterialSource => Wp.UndeliveredByYear.Count > 0
        ? "Propozycja: części niedostarczone vAPD według roku planowanej dostawy (USD po kursie bieżącym). Materiały spoza vAPD wpisz ręcznie."
        : "Propozycja: BAC materiałów − poniesione, rozłożone do prognozowanego końca.";

    public ObservableCollection<YearRowViewModel> Hours { get; } = [];

    public ObservableCollection<YearRowViewModel> Materials { get; } = [];

    public IReadOnlyList<SummaryTile> Context { get; }

    public IReadOnlyList<string> History { get; }

    public ObservableCollection<SummaryTile> Summary { get; } = [];

    public DateTime? ForecastStart
    {
        get => _forecastStart;
        set { if (SetProperty(ref _forecastStart, value)) Recalculate(); }
    }

    /// <summary>Prognozowany koniec; koniec w późniejszym roku niż ostatni wiersz – dochodzą wiersze lat do roku końca (bez propozycji, 0).</summary>
    public DateTime? ForecastFinish
    {
        get => _forecastFinish;
        set
        {
            if (!SetProperty(ref _forecastFinish, value))
                return;
            if (value is { } finish)
            {
                AddYears(Hours, CamLineKinds.Hours, finish.Year);
                AddYears(Materials, CamLineKinds.Material, finish.Year);
            }
            Recalculate();
        }
    }

    /// <summary>W wierszu roku jest wartość, której nie da się odczytać jako liczby ≥ 0.</summary>
    public bool HasInvalidValues => Hours.Concat(Materials).Any(r => r.Error is not null);

    private void AddYears(ObservableCollection<YearRowViewModel> rows, char kind, int lastYear)
    {
        for (var year = rows.Count == 0 ? lastYear : rows[^1].Year + 1; year <= lastYear; year++)
            rows.Add(new YearRowViewModel(this, kind, year, null, 0));
    }

    public string FinishNote { get; private set; } = "";

    public bool FinishShifted { get; private set; }

    public string Justification
    {
        get => _justification;
        set { if (SetProperty(ref _justification, value)) Recalculate(); }
    }

    public string JustificationLabel { get; private set; } = "";

    public string Errors { get => _errors; private set => SetProperty(ref _errors, value); }

    public string Message { get => _message; set => SetProperty(ref _message, value); }

    public System.Windows.Input.ICommand SaveDraft { get; }

    public System.Windows.Input.ICommand Submit { get; }

    /// <summary>Estymacja z wartości formularza (do zapisu i wyliczeń).</summary>
    public CamEstimate Current() => Estimate with
    {
        ForecastStart = _forecastStart is { } s ? DateOnly.FromDateTime(s) : null,
        ForecastFinish = _forecastFinish is { } f ? DateOnly.FromDateTime(f) : null,
        Justification = string.IsNullOrWhiteSpace(_justification) ? null : _justification.Trim(),
        Lines = [.. Hours.Select(r => new CamEstimateLine(r.Kind, r.Year, r.Proposal, r.Value)), .. Materials.Select(r => new CamEstimateLine(r.Kind, r.Year, r.Proposal, r.Value))],
    };

    /// <summary>Po zapisie: estymacja z bazy (status, wersja), blokada edycji po wysłaniu.</summary>
    public void Saved(CamEstimate estimate)
    {
        Estimate = estimate;
        Editable = CamStatuses.Editable(estimate.Status);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Editable));
        foreach (var row in Hours.Concat(Materials))
            row.EditableChanged();
        OnPropertyChanged(nameof(ReviewNote));
        OnPropertyChanged(nameof(HasReviewNote));
        Recalculate();
    }

    public void Recalculate()
    {
        if (Summary is null)
            return;   // wywołanie z konstruktora wierszy
        var current = Current();
        foreach (var row in Hours)
        {
            var rate = CamEstimates.Rate(Wp, row.Year);
            row.Refresh(rate is null ? "brak" : Number(rate.Value, 2) + (rate.FromYear is { } y ? $" (z {y})" : ""), rate?.FromYear is not null || rate is null,
                rate is null ? "—" : Number(row.Value * rate.Value, 0));
        }
        var summary = CamEstimates.Summary(Wp, current);
        Summary.Clear();
        Summary.Add(new SummaryTile("ETC godziny", Number(summary.EtcHours, 0) + " h"));
        Summary.Add(new SummaryTile("ETC robocizna", summary.LaborCost is { } labor ? $"{Number(labor, 0)} {Wp.Currency}" : $"brak stawki: {string.Join(", ", summary.MissingRates)}",
            summary.LaborCost is null ? "warn" : ""));
        Summary.Add(new SummaryTile("ETC materiały", $"{Number(summary.Material, 0)} {Wp.Currency}"));
        Summary.Add(new SummaryTile("EAC WP (poniesione + ETC)", summary.Eac is { } eac ? $"{Number(eac, 0)} {Wp.Currency}" : "—"));
        Summary.Add(new SummaryTile($"Wobec BAC {Number(Wp.Bac, 0)}", summary.Vac is { } vac ? $"{(vac > 0 ? "+" : "")}{Number(vac, 0)} {Wp.Currency}" : "—",
            summary.Vac is < 0 ? "crit" : "ok"));
        FinishShifted = CamEstimates.ShiftedFinish(Wp, current);
        FinishNote = FinishShifted ? $"Koniec po baseline ({Wp.BaselineFinish}) – podaj przyczynę w uzasadnieniu." : "Prognoza zgodna z baseline albo wcześniejsza.";
        var changed = CamEstimates.Changed(Wp, current);
        JustificationLabel = changed ? "Uzasadnienie zmian (wymagane – są zmiany względem propozycji)" : "Uzasadnienie (opcjonalne)";
        var errors = CamEstimates.Validate(Wp, current).ToList();
        if (HasInvalidValues)
            errors.Insert(0, "popraw wartości oznaczone w tabeli");
        Errors = errors.Count == 0 ? "" : "Do wysłania brakuje: " + string.Join("; ", errors) + ".";
        OnPropertyChanged(nameof(FinishNote));
        OnPropertyChanged(nameof(FinishShifted));
        OnPropertyChanged(nameof(JustificationLabel));
    }

    private static string Number(decimal value, int decimals) => PolishNumber.ToDisplay(Math.Round(value, decimals), decimals);
}
