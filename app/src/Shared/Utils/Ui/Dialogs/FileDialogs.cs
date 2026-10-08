using Microsoft.Win32;

namespace PzlEv.Shared.Utils.Ui.Dialogs;

public sealed class FileDialogs : IFileDialogs
{
    private const string ExcelFilter = "Excel (*.xlsx)|*.xlsx|Wszystkie pliki (*.*)|*.*";

    private const string OpenFilter = "Excel i CSV (*.xlsx;*.xlsm;*.csv;*.txt)|*.xlsx;*.xlsm;*.csv;*.txt|Wszystkie pliki (*.*)|*.*";

    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;

    public string? OpenExcel(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = OpenFilter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveExcel(string title, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = ExcelFilter, FileName = fileName, DefaultExt = ".xlsx" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
