using System.Windows.Input;
using PzlEv.Modules.Dashboard.Data;
using PzlEv.Modules.Dashboard.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Dashboard.ViewModels;

/// <summary>Pulpit (F07): fazy globalne, stan przebiegów projektów, „Wymaga uwagi”, ostatnie zdarzenia.</summary>
public sealed class DashboardViewModel
{
    public DashboardViewModel(IDashboardDataSource data, INavigator navigator)
    {
        Eyebrow = data.Eyebrow;
        GlobalCards = data.GlobalCards();
        ZakresCards = data.ZakresCards();
        Attention = data.Attention();
        Events = data.Events();
        OpenImport = new RelayCommand(_ => navigator.NavigateTo(ModuleKeys.Import));
        NewProject = new RelayCommand(_ => navigator.NavigateTo(ModuleKeys.Projects));
    }

    public string Eyebrow { get; }

    public IReadOnlyList<GlobalCard> GlobalCards { get; }

    public IReadOnlyList<ZakresCard> ZakresCards { get; }

    public IReadOnlyList<AttentionItem> Attention { get; }

    public IReadOnlyList<EventItem> Events { get; }

    public int AttentionCount => Attention.Count;

    public ICommand OpenImport { get; }

    public ICommand NewProject { get; }
}
