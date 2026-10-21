using System.Windows.Input;
using PzlEv.Modules.Dashboard.Data;
using PzlEv.Modules.Dashboard.Models;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Dashboard.ViewModels;

/// <summary>Pulpit (F07): klikalne karty faz globalnych, „Wymaga uwagi”, ostatnie zdarzenia (stronicowane) – z bazy; „Odśwież” czyta ponownie.</summary>
public sealed class DashboardViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "dashboard");

    private readonly IDashboardDataSource _data;
    private string _eyebrow = "";
    private string? _error;
    private IReadOnlyList<GlobalCard> _globalCards = [];
    private IReadOnlyList<AttentionItem> _attention = [];
    private IReadOnlyList<EventItem> _events = [];
    private int _eventPage;

    /// <summary>Liczba zdarzeń na stronę listy „Ostatnie zdarzenia”.</summary>
    public const int EventsPerPage = 6;

    public DashboardViewModel(IDashboardDataSource data, INavigator navigator)
    {
        _data = data;
        OpenCard = new RelayCommand(p =>
        {
            if (p is GlobalCard { ModuleKey: { } key })
                navigator.NavigateTo(key);
        });
        PrevEvents = new RelayCommand(_ => EventPage--, _ => EventPage > 0);
        NextEvents = new RelayCommand(_ => EventPage++, _ => EventPage < EventPageCount - 1);
        Refresh = new AsyncRelayCommand(Load, () => !Busy.IsBusy);
        ResolveProblem = new RelayCommand(p => _ = Resolve(p as AttentionItem), _ => !Busy.IsBusy);
        _ = Load();
    }

    /// <summary>Odczyt z bazy w tle (pasek „Trwa: …”) – okno nie zamarza przy starcie i odświeżaniu.</summary>
    public BusyState Busy { get; } = new();

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
            {
                OnPropertyChanged(nameof(NoEvents));
                _eventPage = -1;
                EventPage = 0;
            }
        }
    }

    /// <summary>Bieżąca strona zdarzeń (od 0).</summary>
    public int EventPage
    {
        get => _eventPage;
        private set
        {
            var page = Math.Clamp(value, 0, EventPageCount - 1);
            if (SetProperty(ref _eventPage, page))
            {
                OnPropertyChanged(nameof(PagedEvents));
                OnPropertyChanged(nameof(EventPageLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public int EventPageCount => Math.Max(1, (Events.Count + EventsPerPage - 1) / EventsPerPage);

    public IReadOnlyList<EventItem> PagedEvents => Events.Skip(EventPage * EventsPerPage).Take(EventsPerPage).ToList();

    public string EventPageLabel => $"{EventPage + 1} / {EventPageCount}";

    public int AttentionCount => Attention.Count;

    public bool NoAttention => Attention.Count == 0;

    public bool NoEvents => Events.Count == 0;

    /// <summary>Klik w kartę fazy globalnej – przejście do modułu karty.</summary>
    public ICommand OpenCard { get; }

    public ICommand PrevEvents { get; }

    public ICommand NextEvents { get; }

    public ICommand Refresh { get; }

    /// <summary>„Rozwiązane” przy problemie z listy „Wymaga uwagi”.</summary>
    public ICommand ResolveProblem { get; }

    private async Task Resolve(AttentionItem? item)
    {
        if (item is null)
            return;
        try
        {
            await Busy.Run("Oznaczanie problemu jako rozwiązanego…", () => _data.Resolve(item.ProblemId));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Pulpit – oznaczenie problemu nieudane");
            Error = $"Nie udało się oznaczyć problemu jako rozwiązanego: {ex.Message}";
            return;
        }
        await Load();
    }

    private async Task Load()
    {
        try
        {
            var (eyebrow, global, attention, events) = await Busy.Run("Wczytywanie pulpitu…",
                () => (_data.Eyebrow, _data.GlobalCards(), _data.Attention(), _data.Events()));
            Eyebrow = eyebrow;
            GlobalCards = global;
            Attention = attention;
            Events = events;
            Error = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Pulpit – odczyt z bazy nieudany");
            Error = $"Nie udało się odczytać danych z bazy: {ex.Message}";
        }
    }
}
