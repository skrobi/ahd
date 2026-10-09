using PzlEv.Shared.Models;

namespace PzlEv.Shell;

/// <summary>Pozycja menu – tworzona z IModule (Key, NavLabel, NavBadge); Icon – znak ikony (FIcon) dla zwiniętego menu.</summary>
public sealed record NavItem(string Key, string Label, string? Badge)
{
    public string Icon => Key switch
    {
        ModuleKeys.Dashboard => "",
        ModuleKeys.Import => "",
        ModuleKeys.Mapping => "",
        ModuleKeys.Projects => "",
        ModuleKeys.Runs => "",
        ModuleKeys.MasterData => "",
        ModuleKeys.Administration => "",
        ModuleKeys.Diagnostics => "",
        _ => "",
    };
}
