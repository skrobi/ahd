using System.ComponentModel;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Shared.Utils.Ui.Dictionaries;

/// <summary>
/// Wiersz słownika w tabeli: komórki jako tekst do edycji (indeks kolumny), stan nowy / zmieniony. Wiersz dziedziczony
/// (Cost Category projektu – pozycja słownika globalnego) po zmianie zapisuje się jako nowy wiersz projektu.
/// Cells – komórki do wyświetlenia (opis wartości powiązanej, problem komórki). Zmieniony – inny niż przy wczytaniu
/// (powrót do pierwotnej wartości, np. Ctrl+Z, zdejmuje znacznik).
/// </summary>
public sealed class DictRowViewModel : INotifyPropertyChanged
{
    private readonly string?[] _cells;
    private readonly string?[] _original;

    public DictRowViewModel(long? rowId, int? version, string?[] cells, bool inherited = false,
        DictionarySpec? spec = null, Func<DictColumn, IReadOnlyList<LookupOption>?>? options = null)
    {
        RowId = rowId;
        Version = version;
        _cells = cells;
        _original = (string?[])cells.Clone();
        IsInherited = inherited;
        Cells = spec is null ? [] : spec.Columns.Select((c, i) => new DictCellViewModel(this, i, spec, c, options?.Invoke(c))).ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long? RowId { get; }

    public int? Version { get; }

    /// <summary>Pozycja słownika globalnego pokazana w słowniku projektu (nie jest wierszem projektu).</summary>
    public bool IsInherited { get; }

    public IReadOnlyList<DictCellViewModel> Cells { get; }

    public bool IsNew => RowId is null;

    public bool IsModified => _cells.Where((c, i) => (c ?? "") != (_original[i] ?? "")).Any();

    public string State => IsNew ? "nowy"
        : IsInherited ? IsModified ? "zmiana w projekcie" : "globalny"
        : IsModified ? "zmieniony" : "";

    /// <summary>Zmiana komórki (wiersz, kolumna, poprzednia wartość) – historia Ctrl+Z tabeli.</summary>
    internal Action<DictRowViewModel, int, string?>? Edited { get; set; }

    public string? this[int index]
    {
        get => _cells[index];
        set
        {
            var old = _cells[index];
            if (old == value)
                return;
            _cells[index] = value;
            Edited?.Invoke(this, index, old);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            if (index < Cells.Count)
                Cells[index].Changed();
        }
    }

    /// <summary>Wiersz do zapisu; zmieniony wiersz dziedziczony – nowy wiersz (bez RowId).</summary>
    public DictRow ToDictRow(DictionarySpec spec) =>
        new(IsInherited ? null : RowId, IsInherited ? null : Version,
            spec.Columns.Select((c, i) => (c.Name, Value: _cells[i])).ToDictionary(x => x.Name, x => x.Value));

    public bool Matches(string filter) =>
        _cells.Any(c => c?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
        || Cells.Any(c => c.Display.Contains(filter, StringComparison.OrdinalIgnoreCase));
}
