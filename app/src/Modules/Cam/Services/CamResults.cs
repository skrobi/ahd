using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Cam.Services;

/// <summary>Zakres kosztów wyników: razem, robocizna (bez materiałów), materiały.</summary>
public enum CamScope
{
    All,
    Labor,
    Material,
}

/// <summary>
/// Wartości EV WP albo sumy w jednej walucie: BAC, BCWS, BCWP (tylko zakres Razem – metoda EV robocizny i materiałów osobno: O35),
/// ACWP, ETC (koszt), pochodne (docs/ev-obliczenia.md, rozdz. 3; wskaźnik bez mianownika – null).
/// </summary>
public sealed record CamValues(decimal Bac, decimal? Bcws, decimal? Bcwp, decimal Acwp, decimal? Etc)
{
    public decimal? Eac => Etc is { } etc ? Acwp + etc : null;
    public decimal? Vac => Eac is { } eac ? Bac - eac : null;
    public decimal? Cv => Bcwp is { } ev ? ev - Acwp : null;
    public decimal? Sv => Bcwp is { } ev && Bcws is { } pv ? ev - pv : null;
    public decimal? Cpi => Bcwp is { } ev && Acwp != 0 ? ev / Acwp : null;
    public decimal? Spi => Bcwp is { } ev && Bcws is { } pv && pv != 0 ? ev / pv : null;
    public decimal? Ieac => Cpi is { } cpi && cpi != 0 && Bcwp is { } ev ? Acwp + (Bac - ev) / cpi : null;
    public decimal? Tcpi => Eac is { } eac && eac - Acwp != 0 && Bcwp is { } ev ? (Bac - ev) / (eac - Acwp) : null;
    public decimal? PercentComplete => Bcwp is { } ev && Bac != 0 ? ev / Bac * 100 : null;
    public decimal? PercentSpent => Bac != 0 ? Acwp / Bac * 100 : null;

    public static CamValues Zero { get; } = new(0, null, null, 0, null);

    /// <summary>Suma (null + wartość = wartość; wartość nieznana w części – zostaje znana część).</summary>
    public CamValues Add(CamValues other) => new(Bac + other.Bac, Plus(Bcws, other.Bcws), Plus(Bcwp, other.Bcwp), Acwp + other.Acwp, Plus(Etc, other.Etc));

    private static decimal? Plus(decimal? a, decimal? b) => a is null ? b : b is null ? a : a + b;
}

/// <summary>Waluta wyników CAM-a: waluta programów (gdy wszystkie w jednej), PLN albo USD (kurs bieżący).</summary>
public enum CamCurrency
{
    Program,
    Pln,
    Usd,
}

/// <summary>Wyniki EV CAM-a (tasks/F11.5) – WP z wielu programów w jednej walucie, zakres kosztów, sumy z wskaźnikami z sum.</summary>
public static class CamResults
{
    /// <summary>Progi odchyleń CPI / SPI: poniżej Warning – ostrzeżenie, poniżej Critical – wymagane wyjaśnienie w estymacji.</summary>
    public const decimal Warning = 0.95m, Critical = 0.90m;

    /// <summary>Poziom odchylenia wskaźnika: "ok", "warn", "crit" (null – brak wartości).</summary>
    public static string? Tone(decimal? index) => index is not { } v ? null : v >= Warning ? "ok" : v >= Critical ? "warn" : "crit";

    /// <summary>Przelicznik WP na walutę wyników (Program – 1); null – brak kursu (WP poza sumą).</summary>
    public static decimal? Factor(CamWorkPackage wp, CamCurrency currency) => currency switch
    {
        CamCurrency.Pln => wp.ToPln,
        CamCurrency.Usd => wp.ToUsd,
        _ => 1,
    };

    /// <summary>Wartości WP w zakresie (waluta programu × factor); WP bez materiałów w zakresie Materiały – null.</summary>
    public static CamValues? Of(CamWorkPackage wp, CamScope scope, decimal factor)
    {
        var materialEtc = wp.MaterialEtc ?? 0;
        return scope switch
        {
            CamScope.Labor => new CamValues((wp.Bac - wp.BacMaterial) * factor, null, null, wp.AcwpLabor * factor, wp.EtcCost is { } etc ? (etc - materialEtc) * factor : null),
            CamScope.Material => wp.BacMaterial == 0 && wp.AcwpMaterial == 0 && materialEtc == 0 ? null
                : new CamValues(wp.BacMaterial * factor, null, null, wp.AcwpMaterial * factor, materialEtc * factor),
            _ => new CamValues(wp.Bac * factor, wp.Bcws * factor, wp.Bcwp * factor, wp.Acwp * factor, wp.EtcCost * factor),
        };
    }

    /// <summary>Waluta Program możliwa tylko, gdy wszystkie WP są w jednej walucie programu.</summary>
    public static bool SingleCurrency(IEnumerable<CamWorkPackage> wps) => wps.Select(w => w.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1;

    /// <summary>Suma WP w zakresie i walucie; WP bez kursu albo spoza zakresu – pominięte (Skipped – ich liczba bez kursu).</summary>
    public static (CamValues Total, int Skipped) Total(IEnumerable<CamWorkPackage> wps, CamScope scope, CamCurrency currency)
    {
        var total = CamValues.Zero;
        var skipped = 0;
        foreach (var wp in wps)
        {
            if (Factor(wp, currency) is not { } factor)
            {
                skipped++;
                continue;
            }
            if (Of(wp, scope, factor) is { } values)
                total = total.Add(values);
        }
        return (total, skipped);
    }
}
