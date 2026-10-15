using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PzlEv.Modules.Projects.ViewModels;

namespace PzlEv.Modules.Projects.Views;

/// <summary>
/// Przeciąganie wierszy nakładki: uchwyt ⋮⋮ → wiersz (staje się jego dzieckiem) albo pasek korzenia. Zwijanie dzieci (▸ / ▾),
/// zaznaczenie kilku wierszy (Ctrl / Shift) i menu prawego przycisku: „Przenieś do” (najwyższy poziom, węzły wirtualne),
/// „Grupuj w nowy węzeł”, kolejność, zwijanie. Edycja w komórkach: Nazwa (pole z wcięciem, zatwierdzane przy Enter
/// / wyjściu z komórki), Element CES (bez węzłów wirtualnych), P1S; nowy węzeł – od razu edycja nazwy.
/// </summary>
public partial class PoEditorView : UserControl
{
    private const string Format = "pzl-ev/po-node";
    private Point _start;
    private PoRowViewModel? _dragged;

    /// <summary>
    /// Tabela do dołu okna – ekran projektu: bez limitu MaxHeight, wysokość z układu (wiersz * edytora w wierszu * ekranu);
    /// w kreatorze (przyciski kroków pod edytorem, strona przewijana) – najwyżej MaxHeight z XAML.
    /// </summary>
    public bool FillPage { get; set; }

    public PoEditorView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (FillPage)
                NodesGrid.MaxHeight = double.PositiveInfinity;
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PoEditorViewModel old)
            {
                old.EditRequested -= BeginNameEdit;
                old.CommitRequested -= CommitEdits;
            }
            if (e.NewValue is PoEditorViewModel vm)
            {
                vm.EditRequested += BeginNameEdit;
                vm.CommitRequested += CommitEdits;
            }
        };
    }

    /// <summary>Edycja nazwy wiersza (nowy węzeł) – po wygenerowaniu wierszy tabeli.</summary>
    private void BeginNameEdit(PoRowViewModel row) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!NodesGrid.Items.Contains(row))
                return;
            NodesGrid.ScrollIntoView(row, NameColumn);
            NodesGrid.CurrentCell = new DataGridCellInfo(row, NameColumn);
            NodesGrid.BeginEdit();
        }, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>Przed przebudową wierszy (przycisk, menu, przeciągnięcie) i odczytem drzewa – edycja komórki zatwierdzona.</summary>
    private void CommitEdits()
    {
        NodesGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        NodesGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
    }

    /// <summary>Element CES węzła wirtualnego nie istnieje – bez edycji.</summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Column == WbsColumn && e.Row.Item is PoRowViewModel { IsVirtual: true })
            e.Cancel = true;
    }

    /// <summary>Pole nazwy: kursor w polu, tekst zaznaczony (pisanie zastępuje nazwę).</summary>
    private void OnNameBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    /// <summary>Nazwa zapisuje się przy zatwierdzeniu komórki (Enter, wyjście z komórki); Esc – bez zmiany.</summary>
    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Column == NameColumn && Find<TextBox>(e.EditingElement) is { } box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private static T? Find<T>(DependencyObject? element) where T : DependencyObject
    {
        if (element is T found)
            return found;
        for (var i = 0; element is not null && i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (Find<T>(VisualTreeHelper.GetChild(element, i)) is { } child)
                return child;
        return null;
    }

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _dragged = (sender as FrameworkElement)?.DataContext as PoRowViewModel;
        e.Handled = true;   // uchwyt nie zaczyna edycji komórki nazwy
    }

    private void OnGripMove(object sender, MouseEventArgs e)
    {
        if (_dragged is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var delta = e.GetPosition(this) - _start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        var row = _dragged;
        _dragged = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(Format, row.Key), DragDropEffects.Move);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(Format) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropRow(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(Format) || DataContext is not PoEditorViewModel vm)
            return;
        var target = FindRow(e.OriginalSource as DependencyObject)?.Item as PoRowViewModel;
        var key = (long)e.Data.GetData(Format)!;
        if (target is not null && target.Key != key)
            vm.Move(key, target.Key);
        e.Handled = true;
    }

    private void OnDropRoot(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(Format) && DataContext is PoEditorViewModel vm)
            vm.Move((long)e.Data.GetData(Format)!, null);
        e.Handled = true;
    }

    private void OnToggle(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PoRowViewModel { HasChildren: true } row && DataContext is PoEditorViewModel vm)
        {
            vm.Toggle(row);
            e.Handled = true;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is PoEditorViewModel vm)
            vm.SelectedRows = NodesGrid.SelectedItems.OfType<PoRowViewModel>().ToList();
    }

    /// <summary>Prawy przycisk na niezaznaczonym wierszu – zaznacza tylko ten wiersz (na zaznaczonym – zaznaczenie zostaje).</summary>
    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindRow(e.OriginalSource as DependencyObject) is { IsSelected: false } row)
        {
            NodesGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not PoEditorViewModel vm || NodesGrid.ContextMenu is not { } menu || vm.SelectedKeys.Count == 0)
        {
            e.Handled = true;
            return;
        }
        menu.Items.Clear();
        var count = vm.SelectedKeys.Count;
        var moveTo = new MenuItem { Header = count > 1 ? $"Przenieś do ({count})" : "Przenieś do" };
        foreach (var (key, label) in vm.MoveTargets())
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => vm.MoveSelectedTo(key);
            moveTo.Items.Add(item);
        }
        menu.Items.Add(moveTo);
        menu.Items.Add(Command(count > 1 ? $"Grupuj w nowy węzeł ({count})" : "Grupuj w nowy węzeł", vm.GroupSelected));
        menu.Items.Add(new Separator());
        menu.Items.Add(Command("Wyżej", vm.Up));
        menu.Items.Add(Command("Niżej", vm.Down));
        menu.Items.Add(Command("Na początek", vm.Top));
        menu.Items.Add(Command("Na koniec", vm.Bottom));
        menu.Items.Add(Command("Poziom wyżej", vm.Outdent));
        menu.Items.Add(new Separator());
        menu.Items.Add(Command("Zwiń wszystko", vm.CollapseAll));
        menu.Items.Add(Command("Rozwiń wszystko", vm.ExpandAll));
    }

    private static MenuItem Command(string header, ICommand command) => new() { Header = header, Command = command };

    private static DataGridRow? FindRow(DependencyObject? element)
    {
        while (element is not null and not DataGridRow)
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        return element as DataGridRow;
    }
}
