using System.Windows.Controls;
using System.Windows.Input;
using PzlEv.Modules.Projects.ViewModels;

namespace PzlEv.Modules.Projects.Views;

public partial class ProjectListView : UserControl
{
    public ProjectListView() => InitializeComponent();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProjectListViewModel { Selected: { } item } vm && vm.Open.CanExecute(item))
            vm.Open.Execute(item);
    }
}
