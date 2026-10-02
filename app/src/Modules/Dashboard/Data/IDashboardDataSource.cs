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

    IReadOnlyList<AttentionItem> Attention();

    IReadOnlyList<EventItem> Events();
}
