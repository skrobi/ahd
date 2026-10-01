using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Test.Models;
using PzlEv.Test.Mvvm;
using Serilog;

namespace PzlEv.Test.ViewModels;

public sealed record NavItem(string Key, string Label, string? Badge);

public sealed class MainViewModel : ObservableObject
{
    private string _currentView = "pulpit";

    public MainViewModel()
    {
        Navigate = new RelayCommand(p =>
        {
            CurrentView = p as string ?? "pulpit";
            Log.Information("Nawigacja: {View}", CurrentView);
        });

        try
        {
            Packages = new ObservableCollection<PackageInfo>(PackageDiagnostics.Collect());
            foreach (var p in Packages)
                Log.Information("Pakiet {Name} {Version} ({Location})", p.Name, p.Version, p.Location);
        }
        catch (Exception ex)
        {
            // Brak któregoś pakietu w bundlu = wynik testu negatywny – pokazujemy, nie wywracamy aplikacji.
            Log.Error(ex, "Nie udało się wczytać pakietów");
            Packages = [new PackageInfo("BŁĄD", ex.GetType().Name, ex.Message)];
        }
        Log.Information("Runtime: {Runtime}; OS: {Os}; Exe: {Exe}", Runtime, Os, ExePath);
    }

    public string Environment => SampleData.Environment;
    public string User => SampleData.User;
    public string Eyebrow => SampleData.Eyebrow;
    public string Footer => $"Aplikacja {SampleData.AppVersion} · schemat {SampleData.SchemaVersion}";

    public IReadOnlyList<NavItem> Nav { get; } =
    [
        new("pulpit", "Pulpit", null),
        new("import", "Import RABIT", null),
        new("mapa", "Przypisania", "6"),
        new("projekty", "Projekty", "3"),
        new("przebiegi", "Przebiegi", "2 aktywne"),
        new("slowniki", "Słowniki", null),
    ];

    public IReadOnlyList<GlobalCard> GlobalCards { get; } = SampleData.GlobalCards();
    public IReadOnlyList<ZakresCard> ZakresCards { get; } = SampleData.ZakresCards();
    public IReadOnlyList<AttentionItem> Attention { get; } = SampleData.Attention();
    public IReadOnlyList<EventItem> Events { get; } = SampleData.Events();
    public int AttentionCount => Attention.Count;

    public ObservableCollection<PackageInfo> Packages { get; }
    public string Runtime => PackageDiagnostics.Runtime;
    public string Os => PackageDiagnostics.Os;
    public string ExePath => PackageDiagnostics.ExePath;
    public string WindowsUser => $@"{System.Environment.UserDomainName}\{System.Environment.UserName}";

    public ICommand Navigate { get; }

    public string CurrentView
    {
        get => _currentView;
        set
        {
            if (SetProperty(ref _currentView, value))
            {
                OnPropertyChanged(nameof(IsPulpit));
                OnPropertyChanged(nameof(PlaceholderTitle));
            }
        }
    }

    public bool IsPulpit => CurrentView == "pulpit";

    public string PlaceholderTitle =>
        Nav.FirstOrDefault(n => n.Key == CurrentView)?.Label ?? CurrentView;
}
