using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PzlEv.Modules.Projects.Services;
using PzlEv.Modules.Projects.ViewModels;

namespace PzlEv.Modules.Projects.Views;

/// <summary>
/// Kolumny tabeli struktury (stała szerokość – przy wielu kolumnach pojawia się suwak poziomy; pierwsza kolumna
/// zamrożona) i edycja w komórkach: komórki spoza StructureEdits.CanEdit są zablokowane, zatwierdzenie wiersza
/// (Enter, przejście do innego wiersza) zapisuje zmiany.
/// </summary>
public partial class StructureView : UserControl
{
    /// <summary>Kolumny danych: klucz (StructureEdits), nagłówek, szerokość, czcionka stała, wyrównanie do prawej.</summary>
    private static readonly (string Key, string Header, double Width, bool Mono, bool Right)[] Columns =
    [
        ("WbsElement", "Element CES", 130, true, false),
        (StructureEdits.P1s, "P1S", 160, true, false),
        (StructureEdits.Wp, "WP", 110, true, false),
        (StructureEdits.Cam, "CAM", 150, false, false),
        (StructureEdits.CostCategory, "Cost Category", 110, false, false),
        (StructureEdits.BacHours, "BAC HOURS", 100, false, true),
        (StructureEdits.BacMaterial, "BAC MATERIAL", 110, false, true),
        (StructureEdits.Start, "Start", 100, true, false),
        (StructureEdits.Finish, "Koniec", 100, true, false),
    ];

    public StructureView()
    {
        InitializeComponent();
        BuildColumns();
    }

    private StructureViewModel? Model => DataContext as StructureViewModel;

    private void BuildColumns()
    {
        RowsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Nazwa",
            Width = 360,
            SortMemberPath = StructureEdits.Name,
            CellTemplate = (DataTemplate)Resources["NameCell"],
            CellEditingTemplate = (DataTemplate)Resources["NameEdit"],
        });
        foreach (var (key, header, width, mono, right) in Columns)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0)));
            if (right)
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            var column = new DataGridTextColumn
            {
                Header = header,
                Width = width,
                SortMemberPath = key,
                Binding = new Binding($"[{key}]") { Mode = key == "WbsElement" ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                IsReadOnly = key == "WbsElement",
                ElementStyle = style,
            };
            if (mono)
                column.FontFamily = (System.Windows.Media.FontFamily)FindResource("FMono");
            RowsGrid.Columns.Add(column);
        }
        RowsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Braki",
            Width = 60,
            IsReadOnly = true,
            CellTemplate = (DataTemplate)Resources["GapCell"],
        });
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StructureRowViewModel row })
            Model?.Toggle(row);
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not StructureRowViewModel row || Model is not { } model || !model.CanEditNow() || !row.CanEdit(e.Column.SortMemberPath))
            e.Cancel = true;
    }

    private void OnRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not StructureRowViewModel row || Model is not { } model)
            return;
        // Zapis po zakończeniu zatwierdzenia wiersza (wartości komórek są już w wierszu).
        _ = Dispatcher.InvokeAsync(() => model.CommitRow(row), DispatcherPriority.Background);
    }
}
