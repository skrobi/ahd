namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Dane testu dostępu do folderu RABIT na SharePoint: link do folderu (z przeglądarki albo ścieżka WebDAV),
/// opcjonalny link do jednego pliku oraz identyfikatory listy i widoku biblioteki (dla metod A, B, G).
/// </summary>
public sealed record AccessTestInput(string FolderLink, string FileLink, string ListId, string ViewId)
{
    /// <summary>Biblioteka Shared Documents witryny RabbitReporting – z połączenia Excela (narzędzie w Pythonie).</summary>
    public const string RabitListId = "{6BBBA130-435C-4893-93A3-12FDD034BDC2}";

    public const string RabitViewId = "{7228D59B-433E-47AE-821D-306CA7C1058D}";
}
