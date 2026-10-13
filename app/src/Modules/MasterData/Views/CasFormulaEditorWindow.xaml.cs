using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using PzlEv.Modules.MasterData.ViewModels;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.MasterData.Views;

public partial class CasFormulaEditorWindow : Window
{
    private readonly CasFormulaEditorViewModel _model;

    public CasFormulaEditorWindow(CasFormulaEditorViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.Loaded += BuildColumns;
        Loaded += async (_, _) => await model.Load();
        Closing += (_, e) =>
        {
            if (model.Save.CanExecute(null)
                && MessageBox.Show(this, "Formuły grupy mają niezapisane zmiany. Zamknąć bez zapisu?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }

    /// <summary>Okno edytora; true – zapisano formuły (ekran Słowniki odświeża tabelę).</summary>
    public static bool Edit(DictionaryService service, string? group)
    {
        var model = new CasFormulaEditorViewModel(service, group);
        new CasFormulaEditorWindow(model) { Owner = Application.Current?.MainWindow }.ShowDialog();
        return model.Saved;
    }

    /// <summary>Kolumny macierzy: Pula, Naliczana, składnik na kolumnę (nagłówek pionowo), formuła, uwagi.</summary>
    private void BuildColumns()
    {
        Matrix.Columns.Clear();
        Matrix.Columns.Add(new DataGridTextColumn { Header = "Pula", Binding = new Binding(nameof(CasFormulaRowViewModel.Pool)), IsReadOnly = true, Width = 210 });
        Matrix.Columns.Add(Check("Naliczana", nameof(CasFormulaRowViewModel.Applied), null));
        for (var i = 0; i < _model.Components.Count; i++)
            Matrix.Columns.Add(Check(_model.Components[i], $"Cells[{i}].IsOn", $"Cells[{i}].IsEnabled"));
        Matrix.Columns.Add(new DataGridTextColumn { Header = "Formuła", Binding = new Binding(nameof(CasFormulaRowViewModel.Preview)), IsReadOnly = true, Width = 360 });
        Matrix.Columns.Add(new DataGridTextColumn
        {
            Header = "Uwagi", Binding = new Binding(nameof(CasFormulaRowViewModel.Problem)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.ForegroundProperty, FindResource("Crit")) } },
        });
    }

    /// <summary>Kolumna pola wyboru zmieniająca wartość jednym kliknięciem; nagłówek obrócony (wąska macierz).</summary>
    private static DataGridTemplateColumn Check(string header, string isOn, string? isEnabled)
    {
        var enabled = isEnabled is null ? "" : $" IsEnabled=\"{{Binding {isEnabled}}}\"";
        var template = (DataTemplate)XamlReader.Parse(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            $"<CheckBox HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" IsChecked=\"{{Binding {isOn}, UpdateSourceTrigger=PropertyChanged}}\"{enabled} />" +
            "</DataTemplate>");
        return new DataGridTemplateColumn
        {
            Header = new TextBlock { Text = header, LayoutTransform = new RotateTransform(-90), ToolTip = header },
            CellTemplate = template,
            Width = 30,
        };
    }
}
