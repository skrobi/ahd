using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Moduł Projekty – jedna strona naraz: lista, kreator projektu albo ekran projektu.</summary>
public sealed class ProjectsViewModel : ObservableObject
{
    private readonly ProjectService _service;
    private readonly IFileDialogs _dialogs;
    private object _page = null!;

    public ProjectsViewModel(ProjectService service, IFileDialogs dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        ShowList();
    }

    public object Page { get => _page; private set => SetProperty(ref _page, value); }

    public void ShowList() => Page = new ProjectListViewModel(_service, ShowWizard, ShowProject);

    public void ShowWizard() => Page = new WizardViewModel(_service, _dialogs, ShowList, ShowProject);

    public void ShowProject(string code)
    {
        if (_service.Find(code) is { } project)
            Page = new ProjectDetailViewModel(_service, _dialogs, project, ShowList);
        else
            ShowList();
    }
}
