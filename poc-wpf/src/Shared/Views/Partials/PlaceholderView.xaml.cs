using System.Windows;
using System.Windows.Controls;
using PzlEv.Shared.Utils.Modularity;

namespace PzlEv.Shared.Views.Partials;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView()
    {
        InitializeComponent();
    }

    /// <summary>Ekran zastępczy dla modułu bez implementacji (IModule.CreateView).</summary>
    public static FrameworkElement Create(IModule module, INavigator navigator)
        => new PlaceholderView { DataContext = new PlaceholderViewModel(module, navigator) };
}
