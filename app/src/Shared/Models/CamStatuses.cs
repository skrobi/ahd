namespace PzlEv.Shared.Models;

/// <summary>Status estymacji CAM WP w cyklu (docs/funkcjonalnosc.md, moduł CAM) – moduł CAM, etap przebiegu „Estymacje CAM”, struktura programu.</summary>
public static class CamStatuses
{
    /// <summary>Brak estymacji w tym cyklu (nie zapisana w bazie).</summary>
    public const string ToDo = "Do uzupełnienia";
    public const string Draft = "Szkic";
    public const string Sent = "Wysłana";
    public const string Approved = "Zatwierdzona";
    public const string Returned = "Zwrócona";

    /// <summary>CAM może zmieniać estymację (nie czeka na analityka i nie jest zatwierdzona).</summary>
    public static bool Editable(string status) => status is ToDo or Draft or Returned;

    /// <summary>Kolejność na liście zadań: najpierw to, co wymaga działania CAM-a.</summary>
    public static int Order(string status) => status switch { Returned => 0, ToDo => 1, Draft => 2, Sent => 3, _ => 4 };
}

/// <summary>
/// Cykl estymacji (Period – kod cyklu): miesięczny – okres zamknięcia RRRR-MM, termin – koniec tygodnia zamykającego okres; tygodniowy –
/// tydzień kalendarza okresów RRRR-Tnn, termin – koniec tygodnia (null – brak w kalendarzu).
/// </summary>
public sealed record CamCycle(string Period, DateOnly? Deadline)
{
    public bool Weekly => CamCycles.IsWeek(Period);
}

/// <summary>Kody cykli estymacji: okres RRRR-MM (miesięczny) albo tydzień RRRR-Tnn (tygodniowy – programy wypełniane co tydzień).</summary>
public static class CamCycles
{
    public static string Week(int year, int week) => $"{year}-T{week:00}";

    public static bool IsWeek(string code) => code.Length > 5 && code[5] == 'T';

    /// <summary>Rok i tydzień kodu tygodnia; null – kod okresu.</summary>
    public static (int Year, int Week)? WeekOf(string code) =>
        IsWeek(code) && int.TryParse(code[..4], out var year) && int.TryParse(code[6..], out var week) ? (year, week) : null;
}
