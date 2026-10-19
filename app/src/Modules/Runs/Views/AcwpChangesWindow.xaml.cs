using System.Windows;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Modules.Runs.ViewModels;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Dialogs;

namespace PzlEv.Modules.Runs.Views;

public partial class AcwpChangesWindow : Window
{
    private readonly AcwpChangesViewModel _model;

    public AcwpChangesWindow(AcwpChangesViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        Loaded += async (_, _) => await model.Load();
    }

    /// <summary>Okno niemodalne – można je zostawić obok ekranu programu.</summary>
    public static void Open(IRunStore store, Func<IProjectProcessSource?> processes, AcwpChangesRequest request) =>
        new AcwpChangesWindow(new AcwpChangesViewModel(store, processes, request, new FileDialogs())) { Owner = Application.Current?.MainWindow }.Show();

    // Wybrana pozycja drzewa (TreeView.SelectedItem tylko do odczytu) – linie źródłowe liścia.
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => _model.Selected = e.NewValue as AcwpChangeNode;
}

/// <summary>Kontrakt IAcwpChanges – okno „Zmiany ACWP” dla ekranu programu (struktura).</summary>
public sealed class AcwpChangesLauncher(IRunStore store, Func<IProjectProcessSource?> processes) : IAcwpChanges
{
    public void Show(AcwpChangesRequest request) => AcwpChangesWindow.Open(store, processes, request);
}
