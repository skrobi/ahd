using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Views;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Kontrakt IRunPanels: panel przebiegu programu (RunPanelView z RunPanelViewModel) dla ekranu programu.</summary>
public sealed class RunPanels(IRunStore store, RunLauncher launcher, Func<IAcwpChanges?> acwpChanges, Func<ICamReview?>? cams = null) : IRunPanels
{
    public IRunPanel Create(string project) =>
        new Panel(new RunPanelViewModel(store, launcher, acwpChanges(), project, new PzlEv.Shared.Utils.Ui.Dialogs.FileDialogs(), cams));

    private sealed class Panel(RunPanelViewModel model) : IRunPanel
    {
        public object View { get; } = new RunPanelView { DataContext = model };

        public event Action<DictionaryLink>? FixRequested
        {
            add => model.FixRequested += value;
            remove => model.FixRequested -= value;
        }

        public void Dispose() => model.Dispose();
    }
}
