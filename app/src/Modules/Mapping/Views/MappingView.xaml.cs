using System.Windows;
using System.Windows.Controls;
using PzlEv.Modules.Mapping.ViewModels;

namespace PzlEv.Modules.Mapping.Views;

public partial class MappingView : UserControl
{
    public MappingView()
    {
        InitializeComponent();
    }

    /// <summary>TreeView nie wiąże SelectedItem – wybór przekazywany do ViewModelu.</summary>
    private void OnTreeSelection(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        (DataContext as MappingViewModel)?.SelectTreeItem(e.NewValue);
}
