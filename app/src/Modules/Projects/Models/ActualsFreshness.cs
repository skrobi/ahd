using System.Globalization;

namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Plik ACTUALS, z którego są dane kosztów (najnowsza zaimportowana wersja – CAN_LatestFiles): ReportAt – data raportu
/// w RABIT (modyfikacja pliku) danych w bazie, ImportedAt – kiedy zaimportowano; SeenReportAt / CheckedAt – ostatnie
/// sprawdzenie pliku w RABIT (import „pominięty” albo „duplikat”, gdy treść się nie zmieniła – dane są aktualne na tę datę).
/// </summary>
public sealed record ActualsFile(string FileName, DateTimeOffset ReportAt, DateTimeOffset ImportedAt, DateTimeOffset? SeenReportAt, DateTimeOffset? CheckedAt)
{
    /// <summary>Stan danych w RABIT: ostatni raport, na który dane w bazie są aktualne (także bez zmian treści).</summary>
    public DateTimeOffset DataAt => SeenReportAt is { } seen && seen > ReportAt ? seen : ReportAt;

    /// <summary>Ostatnie pobranie z RABIT (import albo sprawdzenie bez zmian).</summary>
    public DateTimeOffset LastCheck => CheckedAt is { } checkedAt && checkedAt > ImportedAt ? checkedAt : ImportedAt;
}

/// <summary>
/// Skąd i z kiedy są dane ACTUALS projektu (status w zakładce Wskaźniki): pliki najnowszego importu – data raportu w RABIT,
/// import i ostatnie sprawdzenie. Error – nie udało się odczytać.
/// </summary>
public sealed record ActualsFreshness(IReadOnlyList<ActualsFile> Files, string? Error = null)
{
    private static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Jedna linia: stan danych RABIT, import, ostatnie sprawdzenie.</summary>
    public string Summary
    {
        get
        {
            if (Error is not null)
                return "Dane RABIT: nie udało się odczytać";
            if (Files.Count == 0)
                return "Dane RABIT: brak zaimportowanych plików ACTUALS";
            var (oldest, newest) = (Files.Min(f => f.DataAt), Files.Max(f => f.DataAt));
            var data = Time(oldest) == Time(newest) ? Time(newest) : $"{Time(oldest)} – {Time(newest)}";
            return $"Dane RABIT z {data} · import {Time(Files.Max(f => f.ImportedAt))} · sprawdzone {Time(Files.Max(f => f.LastCheck))}";
        }
    }

    /// <summary>Szczegóły na plik (podpowiedź).</summary>
    public string Details => Error is not null
        ? $"Odczyt dat danych ACTUALS nieudany: {Error}"
        : Files.Count == 0
            ? "Brak zaimportowanych plików ACTUALS – koszty (ACWP) nie są dostępne. Import RABIT – ekran Import."
            : "Koszty (ACWP) – najnowsza zaimportowana wersja każdego pliku ACTUALS (zrzut RABIT, wszystkie programy):\n" + string.Join("\n",
                Files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).Select(f =>
                    $"• {f.FileName}: raport RABIT {Time(f.ReportAt)}, import {Time(f.ImportedAt)}"
                    + (f.CheckedAt is { } c && c > f.ImportedAt ? $"; sprawdzony {Time(c)} – bez zmian (raport RABIT {Time(f.DataAt)})" : "")))
              + "\nData danych to data raportu w RABIT; ponowne pobranie tej samej treści (pominięty / duplikat) potwierdza, że dane są aktualne na nowszą datę.";
}

/// <summary>
/// Przebieg z zatwierdzonymi danymi ACTUALS (etap „Zatwierdzenie ACTUALS” zakończony): struktura programu pokazuje koszty w stanie
/// danych z chwili przypięcia (AsOf) – nowszy import RABIT ani niezatwierdzony przebieg ich nie zmieniają (docs/pipeline-fazy.md, rozdz. 4.1).
/// </summary>
public sealed record ApprovedActuals(string RunCode, int PinNo, DateTimeOffset AsOf, string? ApprovedBy, string? Comment)
{
    private static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Status (zakładka Wskaźniki): źródło danych struktury; null – brak zatwierdzonego przebiegu (ostatni import).</summary>
    public static string Summary(ApprovedActuals? approved) => approved is null
        ? "ACTUALS struktury: ostatni import – niezatwierdzone"
        : $"ACTUALS struktury: przebieg {approved.RunCode} #{approved.PinNo} · stan {Time(approved.AsOf)}";

    public static string Details(ApprovedActuals? approved) => approved is null
        ? "Program nie ma przebiegu z zatwierdzonymi danymi ACTUALS – struktura pokazuje ostatni import RABIT. Zatwierdzenie: klocek "
          + "„Zatwierdzenie ACTUALS” w szablonie przebiegu (ekran Przebiegi) i „Zatwierdź dane ACTUALS i dalej” w zakładce Przebieg."
        : $"Koszty struktury (ACWP, Actual DLH) – dane ACTUALS w stanie z przypięcia przebiegu {approved.RunCode} #{approved.PinNo} ({Time(approved.AsOf)}), "
          + $"zatwierdzone{(approved.Comment is { Length: > 0 } c ? $": {c}" : approved.ApprovedBy is { } by ? $" przez {by}" : "")}. "
          + "Nowszy import RABIT trafi do struktury po zatwierdzeniu danych w kolejnym przebiegu.";
}
