namespace PzlEv.Modules.Cam.Models;

/// <summary>Rodzaj wiersza lat estymacji: godziny albo materiały (waluta estymacji).</summary>
public static class CamLineKinds
{
    public const char Hours = 'H';
    public const char Material = 'M';
}

/// <summary>Wiersz lat estymacji: rodzaj (CamLineKinds), rok, propozycja systemu (null – bez propozycji), wartość CAM.</summary>
public sealed record CamEstimateLine(char Kind, int Year, decimal? Proposal, decimal Value);

/// <summary>Zdarzenie w historii estymacji: kto, kiedy, status po zdarzeniu, opis zmian, komentarz (uzasadnienie albo uwaga analityka).</summary>
public sealed record CamEstimateEvent(DateTimeOffset At, string By, string Status, string Summary, string? Comment);

/// <summary>
/// Estymacja WP programu w cyklu (okres zamknięcia RRRR-MM): CAM, status, prognozowany start i koniec, waluta kwot materiałów
/// (waluta programu), uzasadnienie CAM, uwaga analityka, wiersze lat; EstimateId null – jeszcze nie zapisana; UpdatedAt –
/// znacznik wersji do kontroli równoczesnej zmiany.
/// </summary>
public sealed record CamEstimate(
    long? EstimateId,
    string Project,
    string Wp,
    string Cycle,
    string Cam,
    string Status,
    DateOnly? ForecastStart,
    DateOnly? ForecastFinish,
    string? Currency,
    string? Justification,
    string? ReviewNote,
    IReadOnlyList<CamEstimateLine> Lines,
    DateTimeOffset? UpdatedAt = null,
    string? UpdatedBy = null,
    DateTimeOffset? SubmittedAt = null,
    string? SubmittedBy = null,
    DateTimeOffset? ReviewedAt = null,
    string? ReviewedBy = null,
    DateOnly? ActualStart = null,
    DateOnly? ActualFinish = null)
{
    public IEnumerable<CamEstimateLine> Of(char kind) => Lines.Where(l => l.Kind == kind).OrderBy(l => l.Year);

    public decimal Total(char kind) => Of(kind).Sum(l => l.Value);
}
