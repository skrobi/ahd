using System.ComponentModel;
using System.Windows;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Wiersz tabeli struktury projektu: komórki po kluczu kolumny (StructureEdits), wcięcie i rozwinięcie drzewa,
/// zmiany wpisane w wierszu do chwili zapisu. IEditableObject – Esc w tabeli cofa zmiany wiersza.
/// </summary>
public sealed class StructureRowViewModel(StructureRow row) : INotifyPropertyChanged, IEditableObject
{
    private readonly Dictionary<string, string?> _changes = [];
    private Dictionary<string, string?>? _beforeEdit;
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public StructureRow Row => row;

    public string Id => row.Id;

    public int Depth => row.Depth;

    public bool IsObjective => row.Kind == GridRowKind.Objective;

    public bool IsVirtual => row.IsVirtual;

    public bool IsGreyed => row.IsGreyed;

    public bool HasChildren => row.HasChildren;

    public Thickness Indent => new(row.Depth * 16, 0, 0, 0);

    public string Glyph => !row.HasChildren ? "" : _isExpanded ? "▾" : "▸";

    public string? Note => row.Note;

    public string? Gap => row.Gap;

    public bool HasGap => row.Gap is not null;

    /// <summary>Zmiany wpisane w wierszu, jeszcze niezapisane (klucz kolumny → wartość).</summary>
    public IReadOnlyDictionary<string, string?> Changes => _changes;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            Notify(nameof(IsExpanded));
            Notify(nameof(Glyph));
        }
    }

    /// <summary>Komórka: wpisana zmiana albo wartość z bazy (liczby w formacie polskim, daty RRRR-MM-DD).</summary>
    public string? this[string column]
    {
        get => _changes.TryGetValue(column, out var changed) ? changed : Value(column);
        set
        {
            if (!StructureEdits.CanEdit(row, column))
                return;
            if ((value ?? "") == (Value(column) ?? ""))
                _changes.Remove(column);
            else
                _changes[column] = value;
            Notify("Item[]");
            Notify(nameof(IsWp));
        }
    }

    /// <summary>Znacznik WP (checkbox): element P1S jest pakietem pracy z kosztami i budżetem.</summary>
    public bool IsWp
    {
        get => StructureEdits.IsChecked(this[StructureEdits.Wp]);
        set => this[StructureEdits.Wp] = value ? "true" : "false";
    }

    public bool CanEdit(string column) => StructureEdits.CanEdit(row, column);

    /// <summary>Odrzuca niezapisane zmiany wiersza.</summary>
    public void Revert()
    {
        _changes.Clear();
        Notify("Item[]");
        Notify(nameof(IsWp));
    }

    public void BeginEdit() => _beforeEdit ??= new Dictionary<string, string?>(_changes);

    public void CancelEdit()
    {
        if (_beforeEdit is null)
            return;
        _changes.Clear();
        foreach (var (key, value) in _beforeEdit)
            _changes[key] = value;
        _beforeEdit = null;
        Notify("Item[]");
        Notify(nameof(IsWp));
    }

    public void EndEdit() => _beforeEdit = null;

    private string? Value(string column) => column switch
    {
        StructureEdits.Name => row.Name,
        "WbsElement" => row.WbsElement,
        StructureEdits.P1s => row.P1s,
        StructureEdits.Wp => row.Wp is null ? "false" : "true",
        StructureEdits.Cam => row.Cam,
        StructureEdits.CostCategory => row.CostCategory,
        StructureEdits.BacHours => Amount(row.BacHours),
        StructureEdits.BacMaterial => Amount(row.BacMaterial),
        StructureEdits.Start => row.Start,
        StructureEdits.Finish => row.Finish,
        "WpCount" => row.Wps.Count == 0 ? null : row.Wps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string? Amount(decimal value) => value == 0 ? null : PolishNumber.ToDisplay(value);

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
