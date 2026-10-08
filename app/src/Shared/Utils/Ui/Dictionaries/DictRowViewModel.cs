using System.ComponentModel;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Shared.Utils.Ui.Dictionaries;

/// <summary>
/// Wiersz słownika w tabeli: komórki jako tekst do edycji (indeks kolumny), stan nowy / zmieniony. Wiersz dziedziczony
/// (Cost Category projektu – pozycja słownika globalnego) po zmianie zapisuje się jako nowy wiersz projektu.
/// Cells – komórki do wyświetlenia (opis wartości powiązanej, problem komórki).
/// </summary>
public sealed class DictRowViewModel : INotifyPropertyChanged
{
    private readonly string?[] _cells;
    private bool _modified;

    public DictRowViewModel(long? rowId, int? version, string?[] cells, bool inherited = false,
        DictionarySpec? spec = null, Func<DictColumn, IReadOnlyList<LookupOption>?>? options = null)
    {
        RowId = rowId;
        Version = version;
        _cells = cells;
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
