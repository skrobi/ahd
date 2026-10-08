using System.Windows.Controls;
using System.Windows.Data;
using PzlEv.Shared.Utils.Ui.Dictionaries;

namespace PzlEv.Shared.Views.Partials;

public partial class DictionaryGrid : UserControl
{
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

    /// <summary>Kolumny tabeli z opisu słownika: komórka [i] wiersza, klucz oznaczony „*”, stan wiersza na końcu.</summary>
    private void RebuildColumns()
    {
        RowsGrid.Columns.Clear();
        if (DataContext is not DictionaryTableViewModel { Spec: { } spec })
            return;
        for (var i = 0; i < spec.Columns.Count; i++)
        {
            var column = spec.Columns[i];
            RowsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Key ? $"{column.Name} *" : column.Name,
                Binding = new Binding($"[{i}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                MinWidth = 80,
            });
        }
        RowsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Stan",
            Binding = new Binding(nameof(DictRowViewModel.State)),
            IsReadOnly = true,
        });
    }
}
