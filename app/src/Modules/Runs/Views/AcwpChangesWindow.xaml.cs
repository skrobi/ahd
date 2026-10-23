using System.Globalization;
using System.Windows;
using System.Windows.Data;
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

    // Wykres przyrostów bieżącego porównania w osobnym oknie (stan z chwili otwarcia).
    private void OnChart(object sender, RoutedEventArgs e) =>
        new AcwpChartWindow(_model.Title.Replace("Zmiany ACWP", "Przyrosty ACWP"), $"{_model.ChartSource}. {_model.ChartNote}", _model.Chart)
            { Owner = this }.Show();
}

/// <summary>
/// Szerokość etykiety wiersza drzewa zmian: szerokość drzewa bez kolumn kwot, pasków i wcięcia poziomu (TreeViewItem – 19 px na poziom),
/// więc kolumny kwot wszystkich poziomów kończą się w jednej linii (nagłówek kolumn – Margin 22,0,20,0).
/// </summary>
public sealed class AcwpTreeLabelWidth : IMultiValueConverter
{
    private const double Indent = 19;
    private const double Numbers = 3 * 150;
    private const double Chrome = 22 + 20;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var width = values.ElementAtOrDefault(0) is double w ? w : 0;
        var depth = (values.ElementAtOrDefault(1) as string) switch
        {
            AcwpChanges.CostElementLevel => 1,
            AcwpChanges.WbsLevel => 2,
            AcwpChanges.PeriodLevel => 3,
            _ => 0,
        };
        return Math.Max(160, width - Numbers - Chrome - Indent * depth);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Kontrakt IAcwpChanges – okno „Zmiany ACWP” dla ekranu programu (struktura).</summary>
public sealed class AcwpChangesLauncher(IRunStore store, Func<IProjectProcessSource?> processes) : IAcwpChanges
{
    public void Show(AcwpChangesRequest request) => AcwpChangesWindow.Open(store, processes, request);
}
