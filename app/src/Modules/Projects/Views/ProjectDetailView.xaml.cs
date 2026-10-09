using System.Windows;
using System.Windows.Controls;

namespace PzlEv.Modules.Projects.Views;

public partial class ProjectDetailView : UserControl
{
    public ProjectDetailView() => InitializeComponent();

    /// <summary>„?” nad tabelą struktury – skróty i opis kolumn w osobnym oknie (pod tabelą nic – tabela do dołu okna).</summary>
    private void OnStructureHelp(object sender, RoutedEventArgs e) =>
        new StructureHelpWindow { Owner = Window.GetWindow(this) }.ShowDialog();
}
