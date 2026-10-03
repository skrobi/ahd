using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Moduł Projekty – jedna strona naraz: lista, kreator projektu albo ekran projektu. Operacje w tle – Busy.</summary>
public sealed class ProjectsViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private readonly IFileDialogs _dialogs;
    private object _page = null!;

    public ProjectsViewModel(ProjectService service, IFileDialogs dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        ShowList();
    }

    /// <summary>Operacja w tle wspólna dla stron modułu (pasek „Trwa: …”).</summary>
    public BusyState Busy { get; } = new();

    public object Page { get => _page; private set => SetProperty(ref _page, value); }

    public void ShowList() => Page = new ProjectListViewModel(_service, Busy, ShowWizard, code => _ = ShowProject(code));

    public void ShowWizard() => Page = new WizardViewModel(_service, _dialogs, Busy, ShowList, code => _ = ShowProject(code));

    public async Task ShowProject(string code)
    {
        try
        {
            if (await Busy.Run($"Otwieranie projektu {code}…", () => _service.Find(code)) is { } project)
            {
                Page = new ProjectDetailViewModel(_service, _dialogs, Busy, project, ShowList);
                return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Otwarcie projektu {Code}", code);
        }
        ShowList();
    }
}
