namespace PzlEv.Shared.Utils.Ui.Modularity;

/// <summary>
/// Przejście do ekranu innego modułu po kluczu (ModuleKeys) – bez znajomości jego typów; parameter – co pokazać na ekranie (typ
/// ze Shared, np. DictionaryLink), przekazywany ekranowi, którego DataContext jest INavigationTarget.
/// </summary>
public interface INavigator
{
    void NavigateTo(string moduleKey);

    void NavigateTo(string moduleKey, object parameter);
}

/// <summary>Ekran modułu przyjmujący parametr nawigacji (INavigator.NavigateTo z parametrem); nieznany parametr – bez zmian.</summary>
public interface INavigationTarget
{
    void NavigatedTo(object parameter);
}
