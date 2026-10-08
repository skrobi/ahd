using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Dictionaries;

namespace PzlEv.Shared.Views.Partials;

/// <summary>
/// Tabela słownika jak arkusz Excela: kolumny z opisu słownika (DictionaryCellColumn), zaznaczanie komórek,
/// Ctrl+V – wklejenie bloku od bieżącej komórki, Delete – wyczyszczenie, Ctrl+D – wypełnienie w dół wartością
/// z pierwszego zaznaczonego wiersza (Ctrl+C – kopiowanie DataGrid). Bieżący wiersz – DictionaryTableViewModel.SelectedRow.
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
                old.ColumnsChanged -= RebuildColumns;
            if (e.NewValue is DictionaryTableViewModel table)
                table.ColumnsChanged += RebuildColumns;
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

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e) => _editing = !e.Cancel;

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) => _editing = false;

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (Table is { } table && RowsGrid.CurrentItem is DictRowViewModel row)
            table.SelectedRow = row;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing || Table is not { Spec: not null } table)
            return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.V)
        {
            var block = ClipboardTable.Parse(Clipboard.ContainsText() ? Clipboard.GetText() : null);
            var column = (RowsGrid.CurrentCell.Column as DictionaryCellColumn)?.Index ?? 0;
            table.Paste(RowsGrid.CurrentCell.Item as DictRowViewModel, column, block);
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
    }

    private IEnumerable<(DictRowViewModel Row, int Column)> SelectedCells() =>
        RowsGrid.SelectedCells
            .Where(c => c.Item is DictRowViewModel && c.Column is DictionaryCellColumn)
            .Select(c => ((DictRowViewModel)c.Item, ((DictionaryCellColumn)c.Column).Index))
            .ToList();
}
