using PzlEv.Modules.Dashboard.Models;

namespace PzlEv.Modules.Dashboard.Data;

/// <summary>
/// Źródło danych Pulpitu – SqlDashboardData (baza środowiska). ViewModel zależy tylko od tego interfejsu.
/// </summary>
public interface IDashboardDataSource
{
    string Eyebrow { get; }

    IReadOnlyList<GlobalCard> GlobalCards();

    IReadOnlyList<ZakresCard> ZakresCards();

    /// <summary>Otwarte problemy (ERROR i WARNING) ze wszystkich obszarów – błędy najpierw, potem najnowsze.</summary>
    IReadOnlyList<AttentionItem> Attention();

    /// <summary>Ręczne oznaczenie problemu jako rozwiązanego (z wpisem w dzienniku).</summary>
    void Resolve(long problemId);

    IReadOnlyList<EventItem> Events();
}
