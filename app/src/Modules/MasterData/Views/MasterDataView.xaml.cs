using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PzlEv.Modules.MasterData.ViewModels;

namespace PzlEv.Modules.MasterData.Views;

public partial class MasterDataView : UserControl
{
    public MasterDataView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MasterDataViewModel old)
                old.ColumnsChanged -= RebuildColumns;
            if (e.NewValue is MasterDataViewModel vm)
                vm.ColumnsChanged += RebuildColumns;
            RebuildColumns();
        };
    }

    /// <summary>Kolumny tabeli z opisu słownika: komórka [i] wiersza, klucz oznaczony „*”, stan wiersza na końcu.</summary>
    private void RebuildColumns()
    {
        RowsGrid.Columns.Clear();
        if (DataContext is not MasterDataViewModel { Spec: { } spec })
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
