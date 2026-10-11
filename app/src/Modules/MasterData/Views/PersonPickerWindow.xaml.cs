using System.Windows;
using PzlEv.Modules.MasterData.ViewModels;
using PzlEv.Shared.Models.Hr;

namespace PzlEv.Modules.MasterData.Views;

public partial class PersonPickerWindow : Window
{
    public PersonPickerWindow(PersonPickerViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    /// <summary>Okno wyboru; null – anulowano.</summary>
    public static IReadOnlyList<HrPerson>? Pick(IReadOnlyList<HrPerson> persons, IReadOnlySet<string> existing)
    {
        var model = new PersonPickerViewModel(persons, existing);
        var window = new PersonPickerWindow(model) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true ? model.Queue.ToList() : null;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
