using System.Windows.Input;
using PzlEv.Modules.Dashboard.Data;
using PzlEv.Modules.Dashboard.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Dashboard.ViewModels;

/// <summary>Pulpit (F07): fazy globalne, projekty, „Wymaga uwagi”, ostatnie zdarzenia – z bazy; „Odśwież” czyta ponownie.</summary>
public sealed class DashboardViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "dashboard");

    private readonly IDashboardDataSource _data;
    private string _eyebrow = "";
    private string? _error;
    private IReadOnlyList<GlobalCard> _globalCards = [];
    private IReadOnlyList<ZakresCard> _zakresCards = [];
    private IReadOnlyList<AttentionItem> _attention = [];
    private IReadOnlyList<EventItem> _events = [];

    public DashboardViewModel(IDashboardDataSource data, INavigator navigator)
    {
        _data = data;
        OpenImport = new RelayCommand(_ => navigator.NavigateTo(ModuleKeys.Import));
        NewProject = new RelayCommand(_ => navigator.NavigateTo(ModuleKeys.Projects));
        Refresh = new RelayCommand(_ => Load());
        Load();
    }

    public string Eyebrow { get => _eyebrow; private set => SetProperty(ref _eyebrow, value); }

    /// <summary>Błąd odczytu z bazy (null = brak).</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

    public IReadOnlyList<GlobalCard> GlobalCards { get => _globalCards; private set => SetProperty(ref _globalCards, value); }

    public IReadOnlyList<ZakresCard> ZakresCards
    {
        get => _zakresCards;
        private set
        {
            if (SetProperty(ref _zakresCards, value))
                OnPropertyChanged(nameof(NoProjects));
        }
    }

    public IReadOnlyList<AttentionItem> Attention
    {
        get => _attention;
        private set
        {
            if (SetProperty(ref _attention, value))
            {
                OnPropertyChanged(nameof(AttentionCount));
                OnPropertyChanged(nameof(NoAttention));
            }
        }
    }

    public IReadOnlyList<EventItem> Events
    {
        get => _events;
        private set
        {
            if (SetProperty(ref _events, value))
                OnPropertyChanged(nameof(NoEvents));
        }
    }

    public int AttentionCount => Attention.Count;

    public bool NoProjects => ZakresCards.Count == 0;

    public bool NoAttention => Attention.Count == 0;

    public bool NoEvents => Events.Count == 0;

    public ICommand OpenImport { get; }

    public ICommand NewProject { get; }

    public ICommand Refresh { get; }

    private void Load()
    {
        try
        {
            Eyebrow = _data.Eyebrow;
            GlobalCards = _data.GlobalCards();
            ZakresCards = _data.ZakresCards();
            Attention = _data.Attention();
            Events = _data.Events();
            Error = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Pulpit – odczyt z bazy nieudany");
            Error = $"Nie udało się odczytać danych z bazy: {ex.Message}";
        }
    }
}
