using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Import.ViewModels;

/// <summary>
/// Plik na liście ekranu Import: po sprawdzeniu źródeł – jak zostanie potraktowany („zostanie zaimportowany”),
/// w trakcie importu – etap (np. „3/4 odczyt i zapis wierszy do bazy – 350 000 wierszy · 12 s”), potem decyzja z opisem.
/// </summary>
public sealed class ImportFileRow(string location, string fileName, long? size, DateTime? modifiedLocal, string recognition, bool matches, string status)
    : ObservableObject
{
    private string _status = status;

    public ImportFileRow(FileCheck check)
        : this(check.Location, check.FileName, check.Size, check.ModifiedLocal, check.Recognition, check.Matches, check.Note)
    {
    }

    public string Location { get; } = location;

    public string FileName { get; } = fileName;

    public long? Size { get; } = size;

    public DateTime? ModifiedLocal { get; } = modifiedLocal;

    public string Recognition { get; } = recognition;

    /// <summary>Nazwa pasuje do aktywnej definicji źródła.</summary>
    public bool Matches { get; } = matches;

    public string Status { get => _status; set => SetProperty(ref _status, value); }
}
