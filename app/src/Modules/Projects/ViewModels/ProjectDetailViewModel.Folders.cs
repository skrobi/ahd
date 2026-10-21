using System.Windows.Input;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Ekran programu – zakładka Foldery: podgląd, zgłoszenie do IT, tworzenie folderów.</summary>
public sealed partial class ProjectDetailViewModel
{
    public string FolderPreview => _service.Folders.Preview(Code);

    public string ItRequest => _service.Folders.ItRequest(Code);

    public ICommand CreateFolders { get; }

    private async Task DoCreateFolders()
    {
        await Try(async () =>
        {
            var created = await Busy.Run("Tworzenie folderów programu…", () => _service.Folders.Create(Code));
            await Reload(refreshMapping: false);
            Status = created.Count == 0 ? "Wszystkie foldery programu istnieją." : $"Utworzono foldery: {string.Join(", ", created)}.";
        });
    }
}
