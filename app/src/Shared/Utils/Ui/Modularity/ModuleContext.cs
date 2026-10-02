using PzlEv.Shared.Utils.Data;

namespace PzlEv.Shared.Utils.Ui.Modularity;

/// <summary>Co moduł dostaje przy tworzeniu ekranu: nawigację i usługi wspólne.</summary>
public sealed record ModuleContext(INavigator Navigator, AppServices Services);
