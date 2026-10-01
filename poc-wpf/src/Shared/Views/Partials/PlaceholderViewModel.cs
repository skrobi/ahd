using System.Windows.Input;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Modularity;
using PzlEv.Shared.Utils.Mvvm;

namespace PzlEv.Shared.Views.Partials;

/// <summary>Ekran zastępczy modułu jeszcze bez implementacji: nazwa, dokumentacja i etapy, które moduł przejmie.</summary>
public sealed class PlaceholderViewModel
{
    public PlaceholderViewModel(IModule module, INavigator navigator)
    {
        Title = module.NavLabel ?? module.Key;
        Doc = module.Doc;
        Stages = module.Stages;
        Back = new RelayCommand(_ => navigator.NavigateTo(ModuleKeys.Dashboard));
    }

    public string Title { get; }

    public string Doc { get; }

    public IReadOnlyList<StageDescriptor> Stages { get; }

    public bool HasStages => Stages.Count > 0;

    public ICommand Back { get; }
}
