using System.Windows;
using System.Windows.Input;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Shell;

/// <summary>
/// Powłoka: menu z listy modułów i nawigacja między ich ekranami. Zna moduły wyłącznie przez IModule;
/// ekran modułu tworzony jest przy pierwszym wejściu i zachowywany (stan ekranu przetrwa przełączanie).
/// </summary>
public sealed class ShellViewModel : ObservableObject, INavigator
{
    private readonly Dictionary<string, IModule> _modules;
    private readonly AppServices _services;
    private readonly Dictionary<string, FrameworkElement> _views = new();
    private FrameworkElement? _currentView;
    private bool _navCollapsed;

    public ShellViewModel(IReadOnlyList<IModule> modules, AppServices services)
    {
        _services = services;
        _modules = modules.ToDictionary(m => m.Key);
        Nav = modules
            .Where(m => m.NavLabel is not null)
            .Select(m => new NavItem(m.Key, m.NavLabel!, m.NavBadge))
            .ToList();
        Navigate = new RelayCommand(p => NavigateTo(p as string ?? ModuleKeys.Dashboard));
        ToggleNav = new RelayCommand(_ => NavCollapsed = !NavCollapsed);
        NavigateTo(ModuleKeys.Dashboard);
    }

    public string Environment => _services.Config.Environment;
    public string Footer => $"Aplikacja {_services.AppVersion}";
    public string DatabaseText => _services.DataDescription;
    public string WindowsUser => _services.User.Account;

    public IReadOnlyList<NavItem> Nav { get; }

    public ICommand Navigate { get; }

    /// <summary>Menu zwinięte do ikon (więcej miejsca na ekran modułu, np. tabelę struktury); nazwa modułu w podpowiedzi.</summary>
    public bool NavCollapsed
    {
        get => _navCollapsed;
        set => SetProperty(ref _navCollapsed, value);
    }

    public ICommand ToggleNav { get; }

    public FrameworkElement? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public void NavigateTo(string moduleKey)
    {
        if (!_modules.TryGetValue(moduleKey, out var module))
        {
            Log.Warning("Nawigacja do nieznanego modułu {Module}", moduleKey);
            return;
        }

        if (!_views.TryGetValue(moduleKey, out var view))
        {
            view = module.CreateView(new ModuleContext(this, _services));
            _views[moduleKey] = view;
        }

        CurrentView = view;
        Log.ForContext("Module", moduleKey).Information("Nawigacja: {Module}", moduleKey);
    }
}
