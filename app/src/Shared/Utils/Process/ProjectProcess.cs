namespace PzlEv.Shared.Utils.Process;

/// <summary>Koszt bazowy: element WBS, cost element, rok i okres SAP, kwota (pole kwoty ACTUALS procesu).</summary>
public sealed record CostRow(string WbsElement, string? CostElement, int FiscalYear, int Period, decimal Amount);

/// <summary>Koszty wierszy w walucie projektu (kolejność wierszy); Error – kosztów nie da się policzyć (np. brak kursu).</summary>
public sealed record CostResult(IReadOnlyList<decimal> Costs, string? Error);

/// <summary>
/// Proces projektu (docs/architektura.md, rozdz. 6.1) – ścieżka wspólna wszystkich typów projektu („golden template”):
/// koszt projektu = koszt z ACTUALS z pola kwoty „Parametrów projektu”, bez przekształceń. Typ projektu, który liczy
/// inaczej, ma podklasę nadpisującą wyłącznie punkty zmienności poniżej; import, przypięcie, struktura, BCWS / BCWP,
/// wskaźniki i kontrole przebiegu są wspólne. Typ wewnętrzny (i SAC – do czasu własnych reguł) używa procesu bazowego;
/// wariant wybiera jedna fabryka (ProjectService.Process), poza nią kod nie sprawdza typu projektu.
/// </summary>
public class ProjectProcess
{
    /// <summary>Proces bazowy – bez punktów zmienności.</summary>
    public static ProjectProcess Default { get; } = new();

    /// <summary>Pole kwoty ACTUALS kosztów projektu; configured – z „Parametrów projektu”.</summary>
    public virtual string AmountField(string configured) => configured;

    /// <summary>
    /// Koszt projektu liczony w aplikacji z kosztów bazowych po okresach (Costs); false – koszt = suma ACTUALS po elemencie
    /// WBS liczona w bazie (REP_ProjectCostsByElement).
    /// </summary>
    public virtual bool TransformsCosts => false;

    /// <summary>Waluta kosztów po przekształceniu; null – waluta pola kwoty ACTUALS.</summary>
    public virtual string? Currency => null;

    /// <summary>Koszt wierszy w walucie projektu (last – ostatni okres danych); bazowo – kwota ACTUALS bez zmian.</summary>
    public virtual CostResult Costs(IReadOnlyList<CostRow> rows, (int Year, int Period) last) => new(rows.Select(r => r.Amount).ToList(), null);

    /// <summary>Nazwa kontroli przebiegu z kosztami po przekształceniu (TransformsCosts).</summary>
    public virtual string CostsCheck => "Koszty programu";

    /// <summary>Opis ACWP (kafelek ekranu projektu) w walucie currency; null – opis wspólny (pole kwoty ACTUALS i waluta).</summary>
    public virtual string? AcwpDescription(string? currency) => null;

    /// <summary>Raport kosztów z narzutami w formułach Excela (raport kosztów CES, docs/slowniki.md, rozdz. 6.1).</summary>
    public virtual bool HasBurdenReport => false;
}
