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
/// Delete, Ctrl+D, Ctrl+Z (cofnij wklejenie), Alt+→ / Alt+← (rozwiń / zwiń). Kolumna klucza komórki – SortMemberPath (klucz StructureEdits).
/// </summary>
public partial class StructureView : UserControl
{
    private static readonly IValueConverter CellBackground = new LevelBrushConverter();

    private readonly List<DataGridColumn> _codeColumns = [];
    // Grupy kolumn jak grupowanie w Excelu (StructureColumns.Groups): kolumny grupy, kolumna zastępcza zwiniętej grupy
    // („+ Schedule”), przycisk grupy w nagłówku pierwszej kolumny, stan zwinięcia; tekst nagłówka kolumny (wklejanie).
    private readonly Dictionary<string, List<DataGridColumn>> _groupColumns = [];
    private readonly Dictionary<string, DataGridColumn> _groupStubs = [];
    private readonly Dictionary<string, Button> _groupButtons = [];
    private readonly Dictionary<string, bool> _collapsed = StructureColumns.Groups.ToDictionary(g => g.Name, g => g.Collapsed);
    private readonly Dictionary<DataGridColumn, string> _headers = [];
    private DataGridColumn? _nameColumn;
    private Button? _codesToggle;
    private readonly LookupColumn _cam = new(StructureEdits.Cam, "CAM", 180);
    private readonly LookupColumn _category = new(StructureEdits.CostCategory, "Cost Category", 150);
    private bool _codesVisible;
    private bool _editing;
    private (string Id, int Column, bool Focused)? _current;

    public StructureView()
    {
        InitializeComponent();
        BuildColumns();
        BuildColumnHelp();
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
                _cam.Options = model.Persons;
                _category.Options = model.Categories;
            }
        };
    }

    private StructureViewModel? Model => DataContext as StructureViewModel;

    /// <summary>
    /// Kolumny w grupach (StructureColumns): nagłówek – nazwa grupy z przyciskiem „−” / „+” nad pierwszą kolumną grupy
    /// i nazwa kolumny, opis w podpowiedzi; zwinięta grupa – jedna wąska kolumna „+ Grupa”. Nazwa (drzewo, zamrożona) jest
    /// zawsze widoczna – w jej nagłówku przycisk grupy WBS Attributes i „+” / „−” dla kolumn CES Element i P1S. Kolumny
    /// z danych (FromView) – tylko do odczytu.
    /// </summary>
    private void BuildColumns()
    {
        foreach (var group in StructureColumns.Groups)
        {
            var columns = StructureColumns.All.Where(c => c.Group == group.Name).ToList();
            _groupColumns[group.Name] = [];
            if (columns[0].Key != StructureEdits.Name)
            {
                var stub = new DataGridTemplateColumn
                {
                    Header = GroupButton(group, collapsedLabel: true),
                    IsReadOnly = true,
                    Width = DataGridLength.Auto,
                    MinWidth = 40,
                    CellTemplate = new DataTemplate(),
                };
                _groupStubs[group.Name] = stub;
                RowsGrid.Columns.Add(stub);
            }
            for (var i = 0; i < columns.Count; i++)
            {
                var column = Create(columns[i]);
                column.Header = Header(columns[i], group, first: i == 0);
                _headers[column] = columns[i].Header;
                _groupColumns[group.Name].Add(column);
                if (!RowsGrid.Columns.Contains(column))
                    RowsGrid.Columns.Add(column);
            }
        }
        RowsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = Header(new StructureColumns.Column("Gaps", "Braki", "", "Braki do uzupełnienia: element bez WP, WP bez budżetu, WP bez CAM, koszt bez WP – szczegóły w podpowiedzi ⚠.", Right: false), null, first: false),
            Width = 60,
            IsReadOnly = true,
            CellTemplate = (DataTemplate)Resources["GapCell"],
        });
        ApplyGroups();
    }

    /// <summary>„Opis kolumn” pod tabelą: grupy z opisem i kolumny z wyjaśnieniem (kolumny z danych – tylko do odczytu).</summary>
    private void BuildColumnHelp()
    {
        foreach (var group in StructureColumns.Groups)
        {
            ColumnHelp.Children.Add(new TextBlock
            {
                Text = $"{group.Name} – {group.Description}{(group.Collapsed ? " (domyślnie zwinięta)" : "")}",
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2), TextWrapping = TextWrapping.Wrap,
            });
            foreach (var column in StructureColumns.All.Where(c => c.Group == group.Name))
                ColumnHelp.Children.Add(new TextBlock
                {
                    Text = $"• {column.Header}: {column.Description}{(column.FromView ? " [tylko do odczytu]" : "")}",
                    Margin = new Thickness(12, 0, 0, 1), TextWrapping = TextWrapping.Wrap, FontSize = 12,
                });
        }
    }

    private DataGridColumn Create(StructureColumns.Column definition)
    {
        switch (definition.Key)
        {
            case StructureEdits.Name:
                _nameColumn = new DataGridTemplateColumn
                {
                    Width = definition.Width,
                    SortMemberPath = StructureEdits.Name,
                    ClipboardContentBinding = new Binding($"[{StructureEdits.Name}]"),
                    CellTemplate = (DataTemplate)Resources["NameCell"],
                    CellEditingTemplate = (DataTemplate)Resources["NameEdit"],
                };
                return _nameColumn;
            case StructureEdits.Wp:
                return new DataGridTemplateColumn
                {
                    Width = definition.Width,
                    IsReadOnly = true,   // znacznik zmienia kliknięcie pola (OnWpClick) albo wklejenie
                    SortMemberPath = StructureEdits.Wp,
                    ClipboardContentBinding = new Binding(nameof(StructureRowViewModel.IsWp)),
                    CellTemplate = (DataTemplate)Resources["WpCell"],
                };
            case StructureEdits.Cam:
                _cam.Width = definition.Width;
                return _cam;
            case StructureEdits.CostCategory:
                _category.Width = definition.Width;
                return _category;
            default:
                var column = TextColumn(definition);
                if (definition.Key is StructureColumns.WbsElement or StructureEdits.P1s)
                    _codeColumns.Add(column);
                return column;
        }
    }

    private DataGridTextColumn TextColumn(StructureColumns.Column definition)
    {
        var key = definition.Key;
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0)));
        style.Setters.Add(new Setter(TextBlock.BackgroundProperty, new Binding($"Problems[{key}]") { Converter = CellBackground, ConverterParameter = "cell" }));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding($"ProblemText[{key}]")));
        if (definition.Right)
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        if (definition.FromView)
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("Muted")));
        var column = new DataGridTextColumn
        {
            Width = definition.Width,
            SortMemberPath = key,
            Binding = new Binding($"[{key}]") { Mode = definition.FromView ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            IsReadOnly = definition.FromView,
            ElementStyle = style,
        };
        if (definition.Mono)
            column.FontFamily = (FontFamily)FindResource("FMono");
        return column;
    }

    /// <summary>Nagłówek kolumny: linia grupy (przycisk i nazwa grupy nad pierwszą kolumną grupy) i nazwa kolumny; opis w podpowiedzi.</summary>
    private FrameworkElement Header(StructureColumns.Column definition, StructureColumns.Group? group, bool first)
    {
        var panel = new StackPanel
        {
            ToolTip = $"{definition.Header}\n{definition.Description}" + (definition.FromView ? "\nTylko do odczytu – wartość z danych (nie edytuje się w tabeli)." : ""),
        };
        var top = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = 20 };
        if (first && group is not null)
        {
            top.Children.Add(GroupButton(group, collapsedLabel: false));
            top.Children.Add(new TextBlock { Text = group.Name, FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        }
        panel.Children.Add(top);
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        if (definition.Key == StructureEdits.Name)
        {
            _codesToggle = new Button { Content = "+", Width = 20, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Pokaż / ukryj kolumny CES Element i Legacy Element (P1S)" };
            _codesToggle.Click += (_, _) => { _codesVisible = !_codesVisible; ApplyGroups(); };
            name.Children.Add(_codesToggle);
        }
        name.Children.Add(new TextBlock
        {
            Text = definition.Header,
            VerticalAlignment = VerticalAlignment.Center,
            FontStyle = definition.FromView ? FontStyles.Italic : FontStyles.Normal,
        });
        panel.Children.Add(name);
        return panel;
    }

    /// <summary>Przycisk grupy: „−” nad pierwszą kolumną rozwiniętej grupy, „+ Grupa” w kolumnie zastępczej zwiniętej grupy.</summary>
    private Button GroupButton(StructureColumns.Group group, bool collapsedLabel)
    {
        var columns = string.Join(", ", StructureColumns.All.Where(c => c.Group == group.Name && c.Key != StructureEdits.Name).Select(c => c.Header));
        var button = new Button
        {
            Content = collapsedLabel ? $"+ {group.Name}" : "−",
            MinWidth = 20,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = $"{group.Name}: {group.Description}\nKolumny: {columns}\n{(collapsedLabel ? "Rozwiń grupę" : "Zwiń grupę")}",
        };
        button.Click += (_, _) =>
        {
            _collapsed[group.Name] = !collapsedLabel;
            ApplyGroups();
        };
        if (!collapsedLabel)
            _groupButtons[group.Name] = button;
        return button;
    }

    /// <summary>Widoczność kolumn według zwinięcia grup i przełącznika CES Element / P1S.</summary>
    private void ApplyGroups()
    {
        CommitEdit();
        foreach (var (group, columns) in _groupColumns)
        {
            var collapsed = _collapsed[group];
            foreach (var column in columns)
            {
                var visible = column == _nameColumn || (!collapsed && (!_codeColumns.Contains(column) || _codesVisible));
                column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_groupStubs.TryGetValue(group, out var stub))
                stub.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            if (_groupButtons.TryGetValue(group, out var button))
                button.Content = collapsed ? "+" : "−";
        }
        if (_codesToggle is not null)
            _codesToggle.Content = _codesVisible ? "−" : "+";
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
        else if (ctrl && e.Key == Key.Z)
        {
            model.Undo.Execute(null);
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

    private string HeaderText(DataGridColumn column) =>
        _headers.TryGetValue(column, out var text) ? text : column.Header as string ?? "";

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
