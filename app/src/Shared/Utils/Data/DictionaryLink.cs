namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Przejście do słownika z filtrem tabeli – „Popraw w aplikacji” przy wyniku kontroli przebiegu (docs/pipeline-fazy.md, rozdz. 4.1):
/// słownik (kod albo nazwa – słownik programu albo globalny), wartość filtra (null – bez filtra).
/// </summary>
public sealed record DictionaryLink(string Dictionary, string? Filter);
