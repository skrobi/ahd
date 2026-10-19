using System.Windows;
using PzlEv.Modules.Runs.ViewModels;

namespace PzlEv.Modules.Runs.Views;

public partial class RunChecksWindow : Window
{
    public RunChecksWindow(RunChecksViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    /// <summary>Okno modalne z ekranu Przebiegi; saved – po zapisie ustawień albo słownika (odświeżenie szablonów).</summary>
    public static void Open(RunChecksViewModel model, Action saved)
    {
        model.Saved += saved;
        new RunChecksWindow(model) { Owner = Application.Current?.MainWindow }.ShowDialog();
    }
}
