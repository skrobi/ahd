using PzlEv.Modules.Dashboard.Models;

namespace PzlEv.Modules.Dashboard.Data;

/// <summary>
/// Źródło danych Pulpitu. W PoC – DashboardSampleData; docelowo implementacja czytająca widoki bazy
/// (Dapper). ViewModel zależy tylko od tego interfejsu, więc zamiana nie dotyka widoku ani ViewModelu.
/// </summary>
public interface IDashboardDataSource
{
    string Eyebrow { get; }

    IReadOnlyList<GlobalCard> GlobalCards();

    IReadOnlyList<ZakresCard> ZakresCards();

    IReadOnlyList<AttentionItem> Attention();

    IReadOnlyList<EventItem> Events();
}
