using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Modularity;
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
    private readonly INavigator? _navigator;
    private object _page = null!;

    public ProjectsViewModel(ProjectService service, IFileDialogs dialogs, INavigator? navigator = null)
    {
        _service = service;
        _dialogs = dialogs;
        _navigator = navigator;
        ShowList();
    }

    /// <summary>Operacja w tle wspólna dla stron modułu (pasek „Trwa: …”).</summary>
    public BusyState Busy { get; } = new();

    /// <summary>Bieżąca strona; poprzednia jest zwalniana (odpięcie od zdarzeń przebiegów – bez wycieku pamięci).</summary>
    public object Page
    {
        get => _page;
        private set
        {
            var previous = _page;
            if (SetProperty(ref _page, value) && !ReferenceEquals(previous, value))
                (previous as IDisposable)?.Dispose();
        }
    }

    public void ShowList() => Page = new ProjectListViewModel(_service, _dialogs, Busy, ShowWizard, code => _ = ShowProject(code));

    public void ShowWizard() => Page = new WizardViewModel(_service, _dialogs, Busy, ShowList, code => _ = ShowProject(code));

    public async Task ShowProject(string code)
    {
        try
        {
            if (await Busy.Run($"Otwieranie programu {code}…", () => _service.Find(code)) is { } project)
            {
                Page = new ProjectDetailViewModel(_service, _dialogs, Busy, project, ShowList,
                    _navigator is null ? null : link => _navigator.NavigateTo(ModuleKeys.MasterData, link));
                return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Otwarcie programu {Code}", code);
        }
        ShowList();
    }
}
