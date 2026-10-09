namespace PzlEv.Shared.Utils.Data;

/// <summary>Cost element z danych ACTUALS: numer, nazwa z danych, liczba wierszy, suma kwoty (informacyjnie).</summary>
public sealed record ActualsCostElement(string CostElement, string? Name, int Rows, decimal Amount);

/// <summary>Cost elementy całego ostatniego importu ACTUALS – „Uzupełnij z ACTUALS” w słowniku Cost Category.</summary>
public interface ICostElementSource
{
    IReadOnlyList<ActualsCostElement> CostElements();
}
