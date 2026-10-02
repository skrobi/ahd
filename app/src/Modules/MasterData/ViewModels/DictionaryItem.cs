using PzlEv.Modules.MasterData.Models;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>Pozycja listy słowników: nazwa i liczba wierszy.</summary>
public sealed class DictionaryItem(DictionarySpec spec, int count) : ObservableObject
{
    private int _count = count;

    public DictionarySpec Spec { get; } = spec;

    public string Name => Spec.Name;

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => _count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
