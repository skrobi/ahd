using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Modules.Projects.ViewModels;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Modules.Projects.Views;

/// <summary>
/// Kolumny tabeli struktury (stała szerokość – przy wielu kolumnach pojawia się suwak poziomy; pierwsza kolumna
/// zamrożona) i edycja w komórkach: komórki spoza StructureEdits.CanEdit są zablokowane, zatwierdzenie wiersza
/// (Enter, przejście do innego wiersza) zapisuje zmiany.
/// </summary>
public partial class StructureView : UserControl
{
    /// <summary>Kolumny tekstowe: klucz (StructureEdits), nagłówek, szerokość, czcionka stała, wyrównanie do prawej.</summary>
    private static readonly (string Key, string Header, double Width, bool Mono, bool Right)[] Codes =
    [
        ("WbsElement", "Element CES", 130, true, false),
        (StructureEdits.P1s, "P1S", 160, true, false),
    ];

    private static readonly (string Key, string Header, double Width, bool Mono, bool Right)[] Budget =
    [
        (StructureEdits.BacHours, "BAC HOURS", 100, false, true),
        (StructureEdits.BacMaterial, "BAC MATERIAL", 110, false, true),
        (StructureEdits.Start, "Baseline Start", 110, true, false),
        (StructureEdits.Finish, "Baseline Koniec", 110, true, false),
    ];

    private readonly List<DataGridColumn> _codeColumns = [];
    private readonly DataGridComboBoxColumn _cam;
    private bool _codesVisible;

    public StructureView()
    {
        InitializeComponent();
        _cam = new DataGridComboBoxColumn
        {
            Header = "CAM",
            Width = 180,
            SortMemberPath = StructureEdits.Cam,
            SelectedValuePath = nameof(LookupOption.Value),
            DisplayMemberPath = nameof(LookupOption.Label),
            SelectedValueBinding = new Binding($"[{StructureEdits.Cam}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        };
        BuildColumns();
        DataContextChanged += (_, _) => _cam.ItemsSource = Model?.Persons;
    }

    private StructureViewModel? Model => DataContext as StructureViewModel;

    private void BuildColumns()
    {
        // Nazwa (drzewo, zamrożona) – w nagłówku „+” / „−” pokazuje albo chowa kolumny Element CES i P1S.
        var toggle = new Button { Content = "+", Width = 20, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Pokaż / ukryj kolumny Element CES i P1S" };
        toggle.Click += (_, _) => ShowCodes(!_codesVisible, toggle);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(toggle);
        header.Children.Add(new TextBlock { Text = "Nazwa", VerticalAlignment = VerticalAlignment.Center });
        RowsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = header,
            Width = 360,
            SortMemberPath = StructureEdits.Name,
            CellTemplate = (DataTemplate)Resources["NameCell"],
            CellEditingTemplate = (DataTemplate)Resources["NameEdit"],
        });
        foreach (var column in Codes)
        {
            var added = TextColumn(column);
            added.Visibility = Visibility.Collapsed;
            _codeColumns.Add(added);
        }
        RowsGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "WP",
            Width = 50,
            SortMemberPath = StructureEdits.Wp,
            Binding = new Binding(nameof(StructureRowViewModel.IsWp)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        });
        RowsGrid.Columns.Add(_cam);
        foreach (var column in Budget)
            TextColumn(column);
        RowsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Braki",
            Width = 60,
            IsReadOnly = true,
            CellTemplate = (DataTemplate)Resources["GapCell"],
        });
    }

    private DataGridTextColumn TextColumn((string Key, string Header, double Width, bool Mono, bool Right) definition)
    {
        var (key, header, width, mono, right) = definition;
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
        return column;
    }

    private void ShowCodes(bool visible, Button toggle)
    {
        _codesVisible = visible;
        foreach (var column in _codeColumns)
            column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        toggle.Content = visible ? "−" : "+";
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
