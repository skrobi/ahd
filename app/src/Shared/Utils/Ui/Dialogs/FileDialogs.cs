using Microsoft.Win32;

namespace PzlEv.Shared.Utils.Ui.Dialogs;

public sealed class FileDialogs : IFileDialogs
{
    private const string ExcelFilter = "Excel (*.xlsx)|*.xlsx|Wszystkie pliki (*.*)|*.*";

    public string? OpenExcel(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ExcelFilter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveExcel(string title, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = ExcelFilter, FileName = fileName, DefaultExt = ".xlsx" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
