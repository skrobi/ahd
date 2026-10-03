namespace PzlEv.Shared.Models.PzlProd;

/// <summary>Wynik sprawdzenia PZLPROD (Diagnostyka): liczba wierszy LOG.WBS i WBS_DIC oraz wartości znaczników aktywności.</summary>
public sealed record PzlProdCheck(int Elements, int Groups, IReadOnlyList<PzlProdFlagValue> Flags);

/// <summary>Wartość kolumny Z_ACTIVE albo LOEKZ w LOG.WBS i liczba wierszy z tą wartością.</summary>
public sealed record PzlProdFlagValue(string Column, string Value, int Rows)
{
    public string Display => Value.Length == 0 ? "(puste)" : Value;
}
