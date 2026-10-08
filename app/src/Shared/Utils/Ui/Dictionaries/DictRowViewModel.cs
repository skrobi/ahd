using System.ComponentModel;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Shared.Utils.Ui.Dictionaries;

/// <summary>
/// Wiersz słownika w tabeli: komórki jako tekst do edycji (indeks kolumny), stan nowy / zmieniony. Wiersz dziedziczony
/// (Cost Category projektu – pozycja słownika globalnego) po zmianie zapisuje się jako nowy wiersz projektu.
/// </summary>
public sealed class DictRowViewModel : INotifyPropertyChanged
{
    private readonly string?[] _cells;
    private bool _modified;

    public DictRowViewModel(long? rowId, int? version, string?[] cells, bool inherited = false)
    {
        RowId = rowId;
        Version = version;
        _cells = cells;
        IsInherited = inherited;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long? RowId { get; }

    public int? Version { get; }

    /// <summary>Pozycja słownika globalnego pokazana w słowniku projektu (nie jest wierszem projektu).</summary>
    public bool IsInherited { get; }

    public bool IsNew => RowId is null;

    public bool IsModified => _modified;

    public string State => IsNew ? "nowy"
        : IsInherited ? _modified ? "zmiana w projekcie" : "globalny"
        : _modified ? "zmieniony" : "";

    public string? this[int index]
    {
        get => _cells[index];
        set
        {
            if (_cells[index] == value)
                return;
            _cells[index] = value;
            _modified = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        }
    }

    /// <summary>Wiersz do zapisu; zmieniony wiersz dziedziczony – nowy wiersz (bez RowId).</summary>
    public DictRow ToDictRow(DictionarySpec spec) =>
        new(IsInherited ? null : RowId, IsInherited ? null : Version,
            spec.Columns.Select((c, i) => (c.Name, Value: _cells[i])).ToDictionary(x => x.Name, x => x.Value));

    public bool Matches(string filter) =>
        _cells.Any(c => c?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
}
