using System.Windows;
using PzlEv.Modules.Cam.Data;
using PzlEv.Modules.Cam.Services;
using PzlEv.Modules.Cam.ViewModels;
using PzlEv.Modules.Cam.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.Cam;

/// <summary>CAM (F11): estymacje WP dla Finansów, zatwierdzanie przez analityka, wyniki EV WP CAM-a. Test – bez uprawnień (wybór CAM-a z listy).</summary>
public sealed class CamModule : IModule
{
    private CamService? _service;

    public string Key => ModuleKeys.Cam;

    public string? NavLabel => "CAM";

    public string Doc => "docs/funkcjonalnosc.md (moduł CAM), tasks/F11.*";

    // Magazyn: baza MS SQL środowiska – CAM_Estimate*, migracja 056; WP i zapis zatwierdzonej estymacji – kontrakt ICamWorkPackages (Projekty).
    public void Initialize(AppServices services) =>
        _service = new CamService(new SqlCamStore(services.Sql), () => services.Contracts.Get<ICamWorkPackages>(), services);

    public FrameworkElement CreateView(ModuleContext context) => new CamView { DataContext = new CamViewModel(_service!) };
}
