using System.ComponentModel;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Shared.Utils.Ui.Dictionaries;

/// <summary>
/// Komórka tabeli słownika: wartość (zapisywana w wierszu), tekst do wyświetlenia (w kolumnie powiązanej – opis,
/// np. imię i nazwisko), problem sprawdzony od razu po wpisaniu (DictionaryCells) – podświetlenie i podpowiedź.
/// </summary>
public sealed class DictCellViewModel(DictRowViewModel row, int index, DictionarySpec spec, DictColumn column, IReadOnlyList<LookupOption>? options)
    : INotifyPropertyChanged
{
    private Issue? _issue = DictionaryCells.Check(spec, column, row[index], options);

    public event PropertyChangedEventHandler? PropertyChanged;

    public DictColumn Column => column;

    /// <summary>Wartości słownika powiązanego (lista wyboru); null – kolumna bez powiązania.</summary>
    public IReadOnlyList<LookupOption>? Options => options;

    public string? Value
    {
        get => row[index];
        set => row[index] = ValueFormat.Clean(value);
    }

    public string Display => DictionaryCells.Display(row[index], options);

    /// <summary>Kolumna tak / nie jako pole wyboru: zaznaczone – „tak”, odznaczone – puste (nie).</summary>
    public bool IsChecked
    {
        get => ValueFormat.TryNormalize(column, row[index], out var canonical, out _) && canonical == "tak";
        set => Value = value ? "tak" : null;
    }

    /// <summary>Klucz sortowania: liczba w kolumnach liczbowych (sortowanie jak w Excelu, nie jak tekst), inaczej tekst.</summary>
    public object? SortKey => column.Type is ColumnType.Decimal or ColumnType.Integer
        ? PzlEv.Shared.Utils.Files.PolishNumber.TryParse(row[index], out var number) ? number : null
        : Display;

    /// <summary>Poziom problemu jako kolor palety: crit, warn albo pusty.</summary>
    public string Level => _issue is null ? "" : _issue.Level == CheckLevel.Error ? "crit" : "warn";

    public string? Problem => _issue is null ? null : $"{_issue.LevelText}: {_issue.Message}";

    internal void Changed()
    {
        _issue = DictionaryCells.Check(spec, column, row[index], options);
        foreach (var name in new[] { nameof(Value), nameof(Display), nameof(IsChecked), nameof(SortKey), nameof(Level), nameof(Problem) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
