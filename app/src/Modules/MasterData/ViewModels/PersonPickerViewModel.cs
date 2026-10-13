using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Shared.Models.Hr;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>
/// Okno „Dodaj z HR” (słownik Osoby): szukanie pracowników PZLHRPROD (USRID, PERNR, imię i nazwisko, e-mail, dział),
/// „Dodaj” przy wyniku odkłada osobę na listę do dodania; „Zatwierdź” zwraca listę – trafia do podglądu słownika.
/// </summary>
public sealed class PersonPickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<HrPerson> _persons;
    private readonly IReadOnlySet<string> _existing;
    private string _search = "";
    private IReadOnlyList<PersonResult> _results = [];

    public PersonPickerViewModel(IReadOnlyList<HrPerson> persons, IReadOnlySet<string> existing)
    {
        _persons = persons;
        _existing = existing;
        Add = new RelayCommand(p =>
        {
            if (p is PersonResult { CanAdd: true } result)
            {
                Queue.Add(result.Person);
                Refresh();
            }
        }, p => p is PersonResult { CanAdd: true });
        Remove = new RelayCommand(p =>
        {
            if (p is HrPerson person && Queue.Remove(person))
                Refresh();
        });
        Queue.CollectionChanged += (_, _) => OnPropertyChanged(nameof(QueueText));
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
                Refresh();
        }
    }

    public IReadOnlyList<PersonResult> Results { get => _results; private set => SetProperty(ref _results, value); }

    /// <summary>Osoby wybrane do dodania.</summary>
    public ObservableCollection<HrPerson> Queue { get; } = [];

    public string QueueText => $"Do dodania: {Queue.Count}";

    public ICommand Add { get; }

    public ICommand Remove { get; }

    private void Refresh() =>
        Results = GlobalDictionaries.SearchPersons(_persons, Search)
            .Select(p => new PersonResult(p,
                _existing.Contains(p.Usrid) ? "już w słowniku" : Queue.Any(q => string.Equals(q.Usrid, p.Usrid, StringComparison.OrdinalIgnoreCase)) ? "na liście" : null))
            .ToList();
}

/// <summary>Wynik szukania: osoba i powód, dla którego nie można jej dodać (null – można).</summary>
public sealed record PersonResult(HrPerson Person, string? Blocked)
{
    public bool CanAdd => Blocked is null;

    public string Details => string.Join(" · ", new[] { Person.Pernr, Person.Usrid, Person.Email, Person.DepartmentShort }.Where(v => v.Length > 0));
}
