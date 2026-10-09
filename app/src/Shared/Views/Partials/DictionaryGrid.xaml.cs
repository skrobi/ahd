using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Dictionaries;

namespace PzlEv.Shared.Views.Partials;

/// <summary>
/// Tabela słownika jak arkusz Excela: kolumny z opisu słownika (DictionaryCellColumn), zaznaczanie komórek,
/// Ctrl+V – wklejenie bloku od bieżącej komórki (jedna komórka – do całego zaznaczenia), Delete – wyczyszczenie,
/// Ctrl+D – wypełnienie w dół wartością z pierwszego zaznaczonego wiersza, Ctrl+Z – cofnięcie, Ctrl+minus – usunięcie
/// wierszy zaznaczenia (Ctrl+C – kopiowanie DataGrid). Bieżący wiersz – DictionaryTableViewModel.SelectedRow.
/// </summary>
public partial class DictionaryGrid : UserControl
{
    private bool _editing;

    public DictionaryGrid()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DictionaryTableViewModel old)
            {
                old.ColumnsChanged -= RebuildColumns;
                old.EndEditRequested -= EndEdit;
            }
            if (e.NewValue is DictionaryTableViewModel table)
            {
                table.ColumnsChanged += RebuildColumns;
                table.EndEditRequested += EndEdit;
            }
            RebuildColumns();
        };
    }

    private DictionaryTableViewModel? Table => DataContext as DictionaryTableViewModel;

    /// <summary>Kolumny tabeli z opisu słownika (typ, lista wyboru, słownik powiązany), stan wiersza na końcu.</summary>
    private void RebuildColumns()
    {
        RowsGrid.Columns.Clear();
        if (Table is not { Spec: { } spec } table)
            return;
        for (var i = 0; i < spec.Columns.Count; i++)
            RowsGrid.Columns.Add(new DictionaryCellColumn(i, spec.Columns[i], table.Options(spec.Columns[i])));
        RowsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Stan",
            Binding = new Binding(nameof(DictRowViewModel.State)),
            IsReadOnly = true,
        });
    }

    /// <summary>Zatwierdzenie (Zapisz, zmiana słownika, filtr) albo anulowanie (Odrzuć zmiany) komórki w trakcie edycji.</summary>
    private void EndEdit(bool commit)
    {
        if (!_editing)
            return;
        if (commit)
            RowsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        else
            RowsGrid.CancelEdit(DataGridEditingUnit.Cell);
        _editing = false;
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e) => _editing = !e.Cancel;

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) => _editing = false;

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (Table is { } table && RowsGrid.CurrentItem is DictRowViewModel row)
            table.SelectedRow = row;
    }

    private void OnSelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (Table is { } table)
            table.SelectedRows = RowsGrid.SelectedCells.Select(c => c.Item).OfType<DictRowViewModel>().Distinct().ToList();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing || Table is not { Spec: not null } table)
            return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.V)
        {
            var column = (RowsGrid.CurrentCell.Column as DictionaryCellColumn)?.Index ?? 0;
            table.Paste(RowsGrid.CurrentCell.Item as DictRowViewModel, column, ClipboardTable.Parse(ClipboardText()), SelectedCells());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            table.SetCells(SelectedCells().Select(c => (c.Row, c.Column, (string?)null)));
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D)
        {
            var visible = table.RowsView.Cast<DictRowViewModel>().ToList();
            var fill = SelectedCells()
                .GroupBy(c => c.Column)
                .SelectMany(g =>
                {
                    var cells = g.OrderBy(c => visible.IndexOf(c.Row)).ToList();
                    var value = cells[0].Row[g.Key];
                    return cells.Skip(1).Select(c => (c.Row, c.Column, value));
                })
                .ToList();
            table.SetCells(fill);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Z)
        {
            table.Undo.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key is Key.OemMinus or Key.Subtract)
        {
            table.RemoveSelected();
            e.Handled = true;
        }
    }

    /// <summary>Tekst schowka; schowek zajęty przez inny program – brak wklejenia zamiast błędu.</summary>
    private static string? ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private IReadOnlyList<(DictRowViewModel Row, int Column)> SelectedCells() =>
        RowsGrid.SelectedCells
            .Where(c => c.Item is DictRowViewModel && c.Column is DictionaryCellColumn)
            .Select(c => ((DictRowViewModel)c.Item, ((DictionaryCellColumn)c.Column).Index))
            .ToList();
}
