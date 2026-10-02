namespace PzlEv.Shared.Utils.Ui.Dialogs;

/// <summary>Wybór pliku – ViewModel nie otwiera okien sam.</summary>
public interface IFileDialogs
{
    string? OpenExcel(string title);

    string? SaveExcel(string title, string fileName);
}
