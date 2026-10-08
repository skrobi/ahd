using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PzlEv.Modules.Projects.Services;
using PzlEv.Modules.Projects.ViewModels;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Converters;

namespace PzlEv.Modules.Projects.Views;

/// <summary>
/// Kolumny tabeli struktury (stała szerokość – przy wielu kolumnach pojawia się suwak poziomy; pierwsza kolumna
/// zamrożona) i edycja jak w Excelu: komórki spoza StructureEdits.CanEdit są zablokowane, zatwierdzenie wiersza
/// (Enter, przejście do innego wiersza, wyjście z tabeli) zapisuje zmiany; zaznaczanie komórek, Ctrl+C / Ctrl+V,
/// Delete, Ctrl+D, Alt+→ / Alt+← (rozwiń / zwiń). Kolumna klucza komórki – SortMemberPath (klucz StructureEdits).
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

    private static readonly IValueConverter CellBackground = new LevelBrushConverter();

    private readonly List<DataGridColumn> _codeColumns = [];
    private readonly CamColumn _cam = new();
    private bool _codesVisible;
    private bool _editing;
    private (string Id, int Column, bool Focused)? _current;

    public StructureView()
    {
        InitializeComponent();
        BuildColumns();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is StructureViewModel old)
            {
                old.EndEditRequested -= CommitEdit;
                old.RowsReplacing -= RememberCurrent;
                old.RowsReplaced -= RestoreCurrent;
            }
            if (e.NewValue is StructureViewModel model)
            {
                model.EndEditRequested += CommitEdit;
                model.RowsReplacing += RememberCurrent;
                model.RowsReplaced += RestoreCurrent;
                _cam.Persons = model.Persons;
            }
        };
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
            ClipboardContentBinding = new Binding($"[{StructureEdits.Name}]"),
            CellTemplate = (DataTemplate)Resources["NameCell"],
            CellEditingTemplate = (DataTemplate)Resources["NameEdit"],
        });
        foreach (var column in Codes)
        {
            var added = TextColumn(column);
            added.Visibility = Visibility.Collapsed;
            _codeColumns.Add(added);
        }
        RowsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "WP",
            Width = 50,
            IsReadOnly = true,   // znacznik zmienia kliknięcie pola (OnWpClick) albo wklejenie
            SortMemberPath = StructureEdits.Wp,
            ClipboardContentBinding = new Binding(nameof(StructureRowViewModel.IsWp)),
            CellTemplate = (DataTemplate)Resources["WpCell"],
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
        style.Setters.Add(new Setter(TextBlock.BackgroundProperty, new Binding($"Problems[{key}]") { Converter = CellBackground, ConverterParameter = "cell" }));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding($"ProblemText[{key}]")));
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
            column.FontFamily = (FontFamily)FindResource("FMono");
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

    /// <summary>Kliknięcie WP: zmiana znacznika (wiązanie) i od razu zapis wiersza.</summary>
    private void OnWpClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StructureRowViewModel row } && Model is { } model)
            _ = model.CommitRow(row);
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not StructureRowViewModel row || Model is not { } model || !model.CanEditNow() || !row.CanEdit(e.Column.SortMemberPath))
            e.Cancel = true;
        else
            _editing = true;
    }

    /// <summary>Jak w Excelu: pisanie w zaznaczonej komórce zastępuje jej zawartość (kolumna Nazwa – szablon edycji).</summary>
    private void OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column is not DataGridTemplateColumn || e.EditingEventArgs is not TextCompositionEventArgs typed)
            return;
        if (FindChild<TextBox>(e.EditingElement) is { } box)
        {
            box.Text = typed.Text;
            box.CaretIndex = box.Text.Length;
        }
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) => _editing = false;

    private void OnRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        _editing = false;
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not StructureRowViewModel row || Model is not { } model)
            return;
        // Zapis po zakończeniu zatwierdzenia wiersza (wartości komórek są już w wierszu).
        _ = Dispatcher.InvokeAsync(() => model.CommitRow(row), DispatcherPriority.Background);
    }

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (Model is { } model && RowsGrid.CurrentItem is StructureRowViewModel row)
            model.Selected = row;
    }

    /// <summary>Zatwierdzenie edycji w toku (odświeżenie, zwinięcie, przycisk poza tabelą) – wartość nie przepada.</summary>
    private void CommitEdit()
    {
        if (!_editing)
            return;
        RowsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        RowsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        _editing = false;
    }

    /// <summary>Fokus poza tabelą (przycisk, inna zakładka) – edycja w toku jest zatwierdzana; lista CAM (okno listy) – nie.</summary>
    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_editing || e.NewFocus is DependencyObject target && (RowsGrid.IsAncestorOf(target) || InPopup(target)))
            return;
        Dispatcher.BeginInvoke(CommitEdit, DispatcherPriority.Input);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
            CommitEdit();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing || Model is not { } model)
            return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.V)
        {
            Paste(model, ClipboardTable.Parse(ClipboardText()));
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            // WP nie jest czyszczony klawiszem Delete – odznaczenie usuwa przypisanie i harmonogram.
            model.SetCells(SelectedCells().Where(c => c.Column != StructureEdits.Wp).Select(c => (c.Row, c.Column, (string?)null)));
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D)
        {
            var order = model.Rows.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => x.i);
            model.SetCells(SelectedCells()
                .GroupBy(c => c.Column)
                .SelectMany(g =>
                {
                    var cells = g.OrderBy(c => order.GetValueOrDefault(c.Row)).ToList();
                    var value = cells[0].Row[g.Key];
                    return cells.Skip(1).Select(c => (c.Row, c.Column, value));
                })
                .ToList());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey is Key.Right or Key.Left && RowsGrid.CurrentItem is StructureRowViewModel row)
        {
            if (row.HasChildren && row.IsExpanded == (e.SystemKey == Key.Left))
                model.Toggle(row);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Wklejenie bloku od bieżącej komórki w kolejności widocznych wierszy i kolumn; jedna komórka – do całego
    /// zaznaczenia; pierwszy wiersz równy nagłówkom kolumn – pominięty; wiersze poza tabelą – pominięte.
    /// </summary>
    private void Paste(StructureViewModel model, IReadOnlyList<string[]> block)
    {
        if (block.Count == 0)
            return;
        var selection = SelectedCells();
        if (block is [[var single]] && selection.Count > 1)
        {
            model.SetCells(selection.Select(c => (c.Row, c.Column, (string?)single)));
            return;
        }
        var columns = RowsGrid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var startColumn = Math.Max(0, columns.IndexOf(RowsGrid.CurrentCell.Column));
        var startRow = RowsGrid.CurrentCell.Item is StructureRowViewModel current ? Math.Max(0, model.Rows.IndexOf(current)) : 0;
        if (block[0].Select((text, i) => (text, i)).Where(x => x.text.Trim().Length > 0)
            .All(x => startColumn + x.i < columns.Count && HeaderText(columns[startColumn + x.i]).Equals(x.text.Trim(), StringComparison.OrdinalIgnoreCase)))
            block = block.Skip(1).ToList();
        var cells = new List<(StructureRowViewModel, string, string?)>();
        for (var r = 0; r < block.Count && startRow + r < model.Rows.Count; r++)
        {
            for (var c = 0; c < block[r].Length && startColumn + c < columns.Count; c++)
            {
                if (columns[startColumn + c].SortMemberPath is { Length: > 0 } key)
                    cells.Add((model.Rows[startRow + r], key, block[r][c]));
            }
        }
        model.SetCells(cells);
    }

    private static string HeaderText(DataGridColumn column) =>
        column.Header as string ?? (column.SortMemberPath == StructureEdits.Name ? "Nazwa" : "");

    private List<(StructureRowViewModel Row, string Column)> SelectedCells() =>
        RowsGrid.SelectedCells
            .Where(c => c.Item is StructureRowViewModel && c.Column?.SortMemberPath is { Length: > 0 })
            .Select(c => ((StructureRowViewModel)c.Item, c.Column.SortMemberPath))
            .ToList();

    /// <summary>Bieżąca komórka przed wymianą wierszy (odświeżenie po zapisie) – po wymianie wraca na to samo miejsce.</summary>
    private void RememberCurrent() =>
        _current = RowsGrid.CurrentCell.Item is StructureRowViewModel row && RowsGrid.CurrentCell.Column is { } column
            ? (row.Id, column.DisplayIndex, RowsGrid.IsKeyboardFocusWithin)
            : null;

    private void RestoreCurrent()
    {
        if (_current is not { } current || Model is not { } model)
            return;
        Dispatcher.BeginInvoke(() =>
        {
            var row = model.Rows.FirstOrDefault(r => r.Id == current.Id);
            var column = RowsGrid.Columns.FirstOrDefault(c => c.DisplayIndex == current.Column);
            if (row is null || column is null)
                return;
            RowsGrid.CurrentCell = new DataGridCellInfo(row, column);
            RowsGrid.SelectedCells.Clear();
            RowsGrid.SelectedCells.Add(RowsGrid.CurrentCell);
            if (!current.Focused)
                return;
            RowsGrid.ScrollIntoView(row, column);
            RowsGrid.UpdateLayout();
            if (column.GetCellContent(row)?.Parent is DataGridCell cell)
                cell.Focus();
        }, DispatcherPriority.Loaded);
    }

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

    /// <summary>Element w oknie listy (np. lista CAM) – poza drzewem wizualnym tabeli.</summary>
    private static bool InPopup(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
        {
            if (current is Popup || current.GetType().Name == "PopupRoot")
                return true;
        }
        return false;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
                return found;
            if (FindChild<T>(child) is { } nested)
                return nested;
        }
        return null;
    }
}
