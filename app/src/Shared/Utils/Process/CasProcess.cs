using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Shared.Utils.Process;

/// <summary>
/// Proces projektu CAS (docs/slowniki.md, rozdz. 6.1): koszt bazowy zawsze z PLN (Value in Obj. Crcy), waluta projektu
/// inna niż PLN – kursem CAS okresu kosztu, narzuty CAS; koszt projektu = TOTAL COST (CasCosts). Kalkulator tworzony przy
/// pierwszym użyciu (słowniki narzutów, kursy, waluta projektu).
/// </summary>
public sealed class CasProcess(Func<CasCosts> calculator) : ProjectProcess
{
    private readonly Lazy<CasCosts> _costs = new(calculator);

    public override string AmountField(string configured) => ProjectSettings.DefaultActualsAmount;

    public override bool TransformsCosts => true;

    public override string? Currency => _costs.Value.Currency;

    public override CostResult Costs(IReadOnlyList<CostRow> rows, (int Year, int Period) last)
    {
        var result = _costs.Value.Calculate(rows, last);
        return new(result.Costs, result.Error);
    }

    public override string CostsCheck => "Koszty po narzutach CAS";

    public override string? AcwpDescription(string? currency) =>
        "koszt rzeczywisty narastająco po narzutach CAS (TOTAL COST) – koszt PLN z ACTUALS (Value in Obj. Crcy)"
        + (currency is { } c && c != "PLN" ? $" w {c} kursem CAS okresu kosztu" : "") + "; stan danych – tag „ACTUALS struktury” w nagłówku programu, bez wykluczeń";

    public override bool HasBurdenReport => true;
}
