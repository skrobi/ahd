using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PzlEv.Modules.Import.Models;

namespace PzlEv.Modules.Import.Views;

public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
    }

    /// <summary>Klik w ścieżkę źródła – otwiera adres SharePoint w przeglądarce albo folder w Eksploratorze.</summary>
    private void OpenPath(object sender, RoutedEventArgs e)
    {
        if ((sender as Hyperlink)?.DataContext is not ImportLocation { Path: { Length: > 0 } path })
            return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageBox.Show($"Nie udało się otworzyć: {path}\n{ex.Message}", "Źródła plików", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
