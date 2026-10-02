using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>
/// Wiersz mapowania definicji: kolumna pliku → pole parsera (Field = "" – tylko wiersze surowe), czy wartość wymagana,
/// przykład z pliku wczytanego przyciskiem „Kolumny z pliku…”.
/// </summary>
public sealed class MappingRowViewModel(string column, Func<string, string> typeOf, Action changed) : ObservableObject
{
    private string _field = "";
    private bool _required;
    private string _sample = "";

    public string Column { get; } = column;

    public string Sample { get => _sample; set => SetProperty(ref _sample, value); }

    public string Field
    {
        get => _field;
        set
        {
            if (!SetProperty(ref _field, value ?? ""))
                return;
            if (_field.Length == 0)
                Required = false;
            OnPropertyChanged(nameof(IsMapped));
            OnPropertyChanged(nameof(TypeText));
            changed();
        }
    }

    public bool Required
    {
        get => _required;
        set
        {
            if (SetProperty(ref _required, value))
                changed();
        }
    }

    public bool IsMapped => Field.Length > 0;

    public string TypeText => typeOf(Field);

    /// <summary>Typ pola po zmianie listy pól parsera (ten sam Field, inny parser albo nowa wersja).</summary>
    public void RefreshType() => OnPropertyChanged(nameof(TypeText));
}
