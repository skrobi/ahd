namespace PzlEv.Shared.Utils.Modularity;

/// <summary>Przejście do ekranu innego modułu po kluczu (ModuleKeys) – bez znajomości jego typów.</summary>
public interface INavigator
{
    void NavigateTo(string moduleKey);
}
