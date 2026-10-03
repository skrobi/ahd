using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PzlEv.Modules.Projects.ViewModels;

namespace PzlEv.Modules.Projects.Views;

/// <summary>Przeciąganie wierszy nakładki: uchwyt ⋮⋮ → wiersz (staje się jego dzieckiem) albo pasek korzenia.</summary>
public partial class PoEditorView : UserControl
{
    private const string Format = "pzl-ev/po-node";
    private Point _start;
    private PoRowViewModel? _dragged;

    public PoEditorView() => InitializeComponent();

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _dragged = (sender as FrameworkElement)?.DataContext as PoRowViewModel;
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

    private static DataGridRow? FindRow(DependencyObject? element)
    {
        while (element is not null and not DataGridRow)
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        return element as DataGridRow;
    }
}
