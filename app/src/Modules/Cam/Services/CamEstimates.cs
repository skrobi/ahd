using PzlEv.Modules.Cam.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Cam.Services;

/// <summary>Stawka godziny roku: wartość (waluta programu) i rok stawki, gdy pochodzi z innego roku (brak stawki na ten rok).</summary>
public sealed record CamRate(decimal Value, int? FromYear);

/// <summary>
/// Co trafia do Finansów: ETC godzin, koszt robocizny (godziny × stawka MPK roku), ETC materiałów, EAC = ACWP + ETC, VAC
/// względem BAC; IEAC (CPI), IEAC (CPI × SPI), TCPI do EAC (null – brak danych EV); MissingRates – lata bez stawki.
/// </summary>
public sealed record CamSummary(decimal EtcHours, decimal? LaborCost, decimal Material, decimal? Eac, decimal? Vac, decimal? Cpi, decimal? Spi,
    decimal? IeacCpi, decimal? IeacComposite, decimal? Tcpi, IReadOnlyList<int> MissingRates)
{
    /// <summary>TCPI różni się od CPI o więcej niż 0,10 – EAC mało wiarygodny (do uzasadnienia).</summary>
    public bool TcpiGap => Tcpi is { } t && Cpi is { } c && Math.Abs(t - c) > 0.10m;
}

/// <summary>
/// Estymacja WP (docs/funkcjonalnosc.md, moduł CAM; tasks/F11.2) – bez bazy i interfejsu: propozycje systemu w latach,
/// stawki według roku, wyliczenia dla Finansów i kontrola wiarygodności EAC, walidacja przed wysłaniem.
/// </summary>
public static class CamEstimates
{
    /// <summary>Minimalna długość uzasadnienia, gdy estymacja różni się od propozycji albo koniec jest po baseline.</summary>
    public const int JustificationLength = 10;

    /// <summary>
    /// Kwota rozłożona liniowo na dni robocze pn–pt od from do to (włącznie) według lat; to przed from albo brak dni roboczych –
    /// całość w roku from. Zaokrąglenie do decimals miejsc, reszta w ostatnim roku (suma = total).
    /// </summary>
    public static IReadOnlyDictionary<int, decimal> Split(decimal total, DateOnly from, DateOnly to, int decimals)
    {
        var all = WorkingDays.Between(from, to);
        if (total == 0)
            return new Dictionary<int, decimal>();
        if (to < from || all == 0)
            return new Dictionary<int, decimal> { [from.Year] = Math.Round(total, decimals) };
        var result = new Dictionary<int, decimal>();
        var rest = total;
        for (var year = from.Year; year <= to.Year; year++)
        {
            var start = year == from.Year ? from : new DateOnly(year, 1, 1);
            var end = year == to.Year ? to : new DateOnly(year, 12, 31);
            var value = year == to.Year ? rest : Math.Round(total * WorkingDays.Between(start, end) / all, decimals);
            rest -= value;
            if (value != 0)
                result[year] = Math.Round(value, decimals);
        }
        return result;
    }

    /// <summary>Start WP: ostatnio wpisana prognoza (Forecast Start), bez niej najwcześniejsza DATA_PLAN zleceń, potem baseline.</summary>
    public static DateOnly? Start(CamWorkPackage wp) => Date(wp.ForecastStart) ?? Date(wp.PlanStart) ?? Date(wp.BaselineStart);

    /// <summary>Koniec WP do rozkładu: ostatnio wpisana prognoza (Forecast Koniec), bez niej najpóźniejsza DATA_PLAN zleceń, potem baseline; null – brak dat.</summary>
    public static DateOnly? Finish(CamWorkPackage wp) => Date(wp.ForecastFinish) ?? Date(wp.PlanFinish) ?? Date(wp.BaselineFinish);

    /// <summary>
    /// Propozycja godzin w latach: WP produkcyjny – ETC z AHD (TECH_DO_PON ÷ produktywność IPT + DJK) według planowanego roku
    /// poniesienia (vAHDD R; rok miniony – rok bieżący; operacje bez roku – rozkład jak niżej); bez produkcji – BAC godzin − godziny
    /// poniesione (nie mniej niż 0) rozłożone na dni robocze od daty stanu do końca WP. Pełne godziny.
    /// </summary>
    public static IReadOnlyDictionary<int, decimal> HoursProposal(CamWorkPackage wp, DateOnly today)
    {
        if (wp.Production && wp.ProductionEtcByYear is { Count: > 0 } byYear)
        {
            var withoutYear = Split(byYear.GetValueOrDefault(0), today, Finish(wp) ?? today, 0);
            return byYear.Where(x => x.Key != 0).Select(x => (Year: Math.Max(x.Key, today.Year), x.Value)).Concat(withoutYear.Select(x => (Year: x.Key, x.Value)))
                .GroupBy(x => x.Year).Select(g => (g.Key, Value: Math.Round(g.Sum(x => x.Value), 0))).Where(x => x.Value != 0)
                .ToDictionary(x => x.Key, x => x.Value);
        }
        var total = wp.Production ? wp.ProductionEtcHours ?? 0 : Math.Max(0, wp.BacHours - (wp.ActualHours ?? 0));
        return Split(total, today, Finish(wp) ?? today, 0);
    }

    /// <summary>
    /// Propozycja materiałów w latach (waluta programu): części niedostarczone vAPD według roku planowanej dostawy (bez daty albo
    /// rok miniony – rok bieżący); bez danych vAPD – BAC materiałów − materiały poniesione (ACWP rodzaju Materials), rozłożone jak godziny.
    /// </summary>
    public static IReadOnlyDictionary<int, decimal> MaterialProposal(CamWorkPackage wp, DateOnly today)
    {
        if (wp.UndeliveredByYear.Count > 0)
            return wp.UndeliveredByYear.GroupBy(x => Math.Max(x.Key, today.Year))
                .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Value), 2));
        var rest = Math.Max(0, wp.BacMaterial - wp.AcwpMaterial);
        return Split(rest, today, Finish(wp) ?? today, 2);
    }

    /// <summary>
    /// Nowa estymacja w cyklu z propozycji systemu (wartość CAM = propozycja; daty – Start / Finish): lata od
    /// bieżącego do końca WP i lat z propozycjami.
    /// </summary>
    public static CamEstimate New(CamWorkPackage wp, string cycle, DateOnly today)
    {
        var hours = HoursProposal(wp, today);
        var materials = MaterialProposal(wp, today);
        var last = new[] { (Finish(wp) ?? today).Year, today.Year }.Concat(hours.Keys).Concat(materials.Keys).Max();
        var lines = new List<CamEstimateLine>();
        for (var year = today.Year; year <= last; year++)
        {
            lines.Add(new CamEstimateLine(CamLineKinds.Hours, year, hours.GetValueOrDefault(year), hours.GetValueOrDefault(year)));
            lines.Add(new CamEstimateLine(CamLineKinds.Material, year, materials.GetValueOrDefault(year), materials.GetValueOrDefault(year)));
        }
        return new CamEstimate(null, wp.Project, wp.Wp, cycle, wp.Cam, CamStatuses.ToDo, Start(wp),
            Finish(wp), wp.Currency, null, null, lines);
    }

    /// <summary>Stawka godziny roku: stawka tego roku, a bez niej ostatnia wcześniejsza (FromYear – jej rok); brak – null.</summary>
    public static CamRate? Rate(CamWorkPackage wp, int year)
    {
        if (wp.HourRates.TryGetValue(year, out var rate))
            return new CamRate(rate, null);
        var earlier = wp.HourRates.Keys.Where(y => y < year).DefaultIfEmpty().Max();
        return earlier == 0 ? null : new CamRate(wp.HourRates[earlier], earlier);
    }

    /// <summary>Wyliczenia estymacji dla Finansów i kontrola wiarygodności EAC (docs/ev-obliczenia.md, rozdz. 3).</summary>
    public static CamSummary Summary(CamWorkPackage wp, CamEstimate estimate)
    {
        var missing = new List<int>();
        decimal labor = 0;
        foreach (var line in estimate.Of(CamLineKinds.Hours).Where(l => l.Value != 0))
        {
            if (Rate(wp, line.Year) is { } rate)
                labor += line.Value * rate.Value;
            else
                missing.Add(line.Year);
        }
        decimal? laborCost = missing.Count == 0 ? labor : null;
        var material = estimate.Total(CamLineKinds.Material);
        var eac = laborCost is { } l ? wp.Acwp + l + material : (decimal?)null;
        var cpi = Ratio(wp.Bcwp, wp.Acwp);
        var spi = Ratio(wp.Bcwp, wp.Bcws);
        var ieacCpi = cpi is { } c && c != 0 && wp.Bcwp is { } ev ? wp.Acwp + (wp.Bac - ev) / c : (decimal?)null;
        var ieacComposite = cpi is { } c2 && spi is { } s && c2 * s != 0 && wp.Bcwp is { } ev2 ? wp.Acwp + (wp.Bac - ev2) / (c2 * s) : (decimal?)null;
        var tcpi = eac is { } e && e - wp.Acwp != 0 && wp.Bcwp is { } ev3 ? (wp.Bac - ev3) / (e - wp.Acwp) : (decimal?)null;
        return new CamSummary(estimate.Total(CamLineKinds.Hours), laborCost, material, eac, eac is { } e2 ? wp.Bac - e2 : null, cpi, spi,
            ieacCpi, ieacComposite, tcpi, missing);
    }

    /// <summary>Estymacja różni się od propozycji systemu (wartość ≠ propozycja w którymkolwiek roku) albo koniec jest po baseline.</summary>
    public static bool Changed(CamWorkPackage wp, CamEstimate estimate) =>
        estimate.Lines.Any(l => l.Value != (l.Proposal ?? 0)) || ShiftedFinish(wp, estimate);

    /// <summary>Prognozowany koniec po końcu baseline.</summary>
    public static bool ShiftedFinish(CamWorkPackage wp, CamEstimate estimate) =>
        estimate.ForecastFinish is { } finish && Date(wp.BaselineFinish) is { } baseline && finish > baseline;

    /// <summary>
    /// Błędy blokujące wysłanie: ujemne wartości, godziny albo materiały w roku po prognozowanym końcu, koniec przed startem,
    /// brak uzasadnienia przy zmianach względem propozycji albo końcu po baseline. Pusta lista – można wysłać.
    /// </summary>
    public static IReadOnlyList<string> Validate(CamWorkPackage wp, CamEstimate estimate)
    {
        var errors = new List<string>();
        if (estimate.Lines.Any(l => l.Value < 0))
            errors.Add("wartości nie mogą być ujemne");
        if (estimate.ForecastStart is { } start && estimate.ForecastFinish is { } end && end < start)
            errors.Add("prognozowany koniec przed startem");
        if (estimate.ForecastFinish is { } finish && estimate.Lines.Where(l => l.Value > 0 && l.Year > finish.Year).Select(l => l.Year).Distinct().ToList() is { Count: > 0 } late)
            errors.Add($"godziny albo materiały w roku po prognozowanym końcu ({string.Join(", ", late)})");
        if (Changed(wp, estimate) && (estimate.Justification?.Trim().Length ?? 0) < JustificationLength)
            errors.Add($"uzasadnienie zmian względem propozycji{(ShiftedFinish(wp, estimate) ? " i końca po baseline" : "")} (min. {JustificationLength} znaków)");
        return errors;
    }

    /// <summary>Opis zmian do historii: suma godzin i materiałów, prognoza końca – względem poprzedniej wersji (null – nowa).</summary>
    public static string Describe(CamEstimate estimate, CamEstimate? previous)
    {
        static string N(decimal v) => Math.Round(v, 0).ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));
        var parts = new List<string>();
        var hours = estimate.Total(CamLineKinds.Hours);
        var material = estimate.Total(CamLineKinds.Material);
        if (previous is null || previous.Total(CamLineKinds.Hours) != hours)
            parts.Add(previous is null ? $"ETC {N(hours)} h" : $"ETC {N(previous.Total(CamLineKinds.Hours))} → {N(hours)} h");
        if (previous is null || previous.Total(CamLineKinds.Material) != material)
            parts.Add(previous is null ? $"materiały {N(material)} {estimate.Currency}" : $"materiały {N(previous.Total(CamLineKinds.Material))} → {N(material)} {estimate.Currency}");
        if (previous is not null && previous.ForecastFinish != estimate.ForecastFinish)
            parts.Add($"koniec {Text(previous.ForecastFinish)} → {Text(estimate.ForecastFinish)}");
        return parts.Count == 0 ? "bez zmian wartości" : string.Join(" · ", parts);
    }

    /// <summary>Czego brakuje do wysłania (lista zadań): daty, godziny, materiały (gdy WP ma materiały), uzasadnienie.</summary>
    public static IReadOnlyList<(string Item, bool Done)> Checklist(CamWorkPackage wp, CamEstimate estimate)
    {
        var saved = estimate.EstimateId is not null;
        var items = new List<(string, bool)>
        {
            ("Daty", saved && estimate.ForecastFinish is not null),
            ("Godziny", saved),
        };
        if (wp.BacMaterial != 0 || wp.AcwpMaterial != 0 || wp.UndeliveredByYear.Count > 0)
            items.Add(("Materiały", saved));
        if (Changed(wp, estimate))
            items.Add(("Uzasadnienie", (estimate.Justification?.Trim().Length ?? 0) >= JustificationLength));
        return items;
    }

    private static decimal? Ratio(decimal? a, decimal? b) => a is { } x && b is { } y && y != 0 ? x / y : null;

    private static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private static string Text(DateOnly? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
}
