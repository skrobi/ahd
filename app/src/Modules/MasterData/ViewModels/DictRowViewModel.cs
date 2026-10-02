using System.ComponentModel;
using PzlEv.Modules.MasterData.Models;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>Wiersz słownika w tabeli: komórki jako tekst do edycji (indeks kolumny), stan nowy / zmieniony.</summary>
public sealed class DictRowViewModel : INotifyPropertyChanged
{
    private readonly string?[] _cells;
    private bool _modified;

    public DictRowViewModel(long? rowId, int? version, string?[] cells)
    {
        RowId = rowId;
        Version = version;
        _cells = cells;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long? RowId { get; }

    public int? Version { get; }

    public bool IsNew => RowId is null;

    public bool IsModified => _modified;

    public string State => IsNew ? "nowy" : _modified ? "zmieniony" : "";

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

    public DictRow ToDictRow(DictionarySpec spec) =>
        new(RowId, Version, spec.Columns.Select((c, i) => (c.Name, Value: _cells[i])).ToDictionary(x => x.Name, x => x.Value));

    public bool Matches(string filter) =>
        _cells.Any(c => c?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
}
