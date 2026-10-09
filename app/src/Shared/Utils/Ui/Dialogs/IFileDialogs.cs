namespace PzlEv.Shared.Utils.Ui.Dialogs;

/// <summary>Wybór pliku i potwierdzenie operacji – ViewModel nie otwiera okien sam.</summary>
public interface IFileDialogs
{
    /// <summary>Pytanie Tak / Nie przed operacją, której skutków nie widać od razu (np. usunięcie budżetu WP).</summary>
    bool Confirm(string title, string message);

    string? OpenExcel(string title);

    string? SaveExcel(string title, string fileName);
}
