using System.Windows;
using System.Windows.Controls;
using PzlEv.Modules.Runs.ViewModels;

namespace PzlEv.Modules.Runs.Views;

public partial class RunPanelView : UserControl
{
    public RunPanelView() => InitializeComponent();

    // Tabela wyniku reguły (kolumny z zapytania): kolumna techniczna wiersza – ukryta, kolumny liczbowe – do prawej.
    private void OnResultColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName == RunPanelViewModel.RowColumn)
            e.Cancel = true;
        else if (DataContext is RunPanelViewModel model && model.NumericColumns.Contains(e.PropertyName) && e.Column is DataGridTextColumn text)
            text.ElementStyle = new Style(typeof(TextBlock), text.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) } };
    }
}
