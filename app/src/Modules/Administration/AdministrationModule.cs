using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Administration;

/// <summary>Administracja (F08): definicje źródeł, lokalizacje RABIT, role i grupy AD. W PoC: ekran zastępczy.</summary>
public sealed class AdministrationModule : IModule
{
    public string Key => ModuleKeys.Administration;

    public string? NavLabel => "Administracja";

    public string Doc => "docs/funkcjonalnosc.md (F08), docs/zrodla-danych.md (rozdz. 2–3), docs/uprawnienia.md";

    public FrameworkElement CreateView(ModuleContext context) => PlaceholderView.Create(this, context.Navigator);
}
