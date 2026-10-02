using System.Windows;
using System.Windows.Input;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Modularity;
using PzlEv.Shared.Utils.Mvvm;
using Serilog;

namespace PzlEv.Shell;

/// <summary>
/// Powłoka: menu z listy modułów i nawigacja między ich ekranami. Zna moduły wyłącznie przez IModule;
/// ekran modułu tworzony jest przy pierwszym wejściu i zachowywany (stan ekranu przetrwa przełączanie).
/// </summary>
public sealed class ShellViewModel : ObservableObject, INavigator
{
    private readonly Dictionary<string, IModule> _modules;
    private readonly Dictionary<string, FrameworkElement> _views = new();
    private FrameworkElement? _currentView;

    public ShellViewModel(IReadOnlyList<IModule> modules)
    {
        _modules = modules.ToDictionary(m => m.Key);
        Nav = modules
            .Where(m => m.NavLabel is not null)
            .Select(m => new NavItem(m.Key, m.NavLabel!, m.NavBadge))
            .ToList();
        Navigate = new RelayCommand(p => NavigateTo(p as string ?? ModuleKeys.Dashboard));
        NavigateTo(ModuleKeys.Dashboard);
    }

    public string Environment => ShellSampleData.Environment;
    public string Footer => $"Aplikacja {ShellSampleData.AppVersion} · schemat {ShellSampleData.SchemaVersion}";
    public string WindowsUser => $@"{System.Environment.UserDomainName}\{System.Environment.UserName}";

    public IReadOnlyList<NavItem> Nav { get; }

    public ICommand Navigate { get; }

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
            view = module.CreateView(this);
            _views[moduleKey] = view;
        }

        CurrentView = view;
        Log.ForContext("Module", moduleKey).Information("Nawigacja: {Module}", moduleKey);
    }
}
