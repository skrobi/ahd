using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Dictionaries;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Ekran programu – zakładki Słowniki programu i Konfiguracja: wybór słownika, tabela, Excel, import źródeł programu.</summary>
public sealed partial class ProjectDetailViewModel
{
    public ObservableCollection<DictionaryPanelViewModel> Dictionaries { get; } = [];

    /// <summary>Słownik projektu pokazany w tabeli (zakładka „Słowniki projektu”).</summary>
    public DictionaryPanelViewModel? SelectedDictionary
    {
        get => _selectedDictionary;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedDictionary))
                return;
            Table.CommitEdits();
            // Trwający odczyt nie blokuje zmiany słownika (nowszy Reload zastępuje starszy); zapis i wczytywanie pliku – tak.
            if (Table.HasPendingChanges || Busy.IsBusy && _loading == 0)
            {
                Status = Busy.IsBusy ? "Poczekaj na zakończenie bieżącej operacji." : "Masz niezapisane zmiany słownika – zapisz albo odrzuć je przed zmianą słownika.";
                // Przywrócenie zaznaczenia po zakończeniu kliknięcia – bez tego lista zostaje na klikniętym słowniku, a tabela pokazuje poprzedni.
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(OnSelectionChanged);
                return;
            }
            _selectedDictionary = value;
            OnSelectionChanged();
            _ = Reload(refreshMapping: false);
        }
    }

    /// <summary>Definicje źródeł (Administracja) ze słownika „Źródła danych” projektu – zakładka „Konfiguracja”, tylko odczyt.</summary>
    public IReadOnlyList<SourceDefinitionRow> SourceDefinitions { get => _sourceDefinitions; private set => SetProperty(ref _sourceDefinitions, value); }

    /// <summary>Słowniki zakładki „Słowniki projektu” (bez słowników konfiguracji).</summary>
    public IReadOnlyList<DictionaryPanelViewModel> ProjectDictionaryList { get; }

    /// <summary>Słowniki zakładki „Konfiguracja”: Źródła danych, Parametry projektu.</summary>
    public IReadOnlyList<DictionaryPanelViewModel> ConfigDictionaryList { get; }

    /// <summary>Wybrany słownik, gdy należy do listy zakładki „Słowniki projektu” (inaczej brak zaznaczenia).</summary>
    public DictionaryPanelViewModel? SelectedProjectDictionary
    {
        get => _selectedDictionary is { } d && ProjectDictionaryList.Contains(d) ? d : null;
        set => SelectedDictionary = value;
    }

    /// <summary>Wybrany słownik, gdy należy do listy zakładki „Konfiguracja”.</summary>
    public DictionaryPanelViewModel? SelectedConfigDictionary
    {
        get => _selectedDictionary is { } d && ConfigDictionaryList.Contains(d) ? d : null;
        set => SelectedDictionary = value;
    }

    /// <summary>Otwarcie zakładki „Słowniki projektu” – tabela pokazuje słownik z jej listy.</summary>
    public bool DictionaryTabOpen
    {
        get => SelectedProjectDictionary is not null;
        set { if (value) SelectFrom(ProjectDictionaryList); }
    }

    /// <summary>Otwarcie zakładki „Konfiguracja” – tabela pokazuje słownik konfiguracji.</summary>
    public bool ConfigTabOpen
    {
        get => SelectedConfigDictionary is not null;
        set { if (value) SelectFrom(ConfigDictionaryList); }
    }

    /// <summary>Indeks zakładki „Słowniki programu” (kolejność zakładek w ProjectDetailView.xaml).</summary>
    private const int DictionaryTab = 3;

    /// <summary>
    /// „Popraw w aplikacji” przy wyniku kontroli przebiegu: słownik programu – zakładka „Słowniki programu” z tym słownikiem i filtrem tabeli;
    /// słownik globalny – ekran Słowniki (nawigacja z parametrem); nieznany – komunikat.
    /// </summary>
    private void OnFixRequested(DictionaryLink link)
    {
        var filter = link.Filter ?? "";
        if (ProjectDictionaries.Find(link.Dictionary) is not { } item || Dictionaries.FirstOrDefault(p => p.Item.Code == item.Code) is not { } panel)
        {
            if (PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.Find(link.Dictionary) is not null && _openGlobalDictionary is not null)
                _openGlobalDictionary(link);
            else
                Status = $"Nie ma słownika „{link.Dictionary}” (kolumna Slownik reguły) – popraw nazwę w regule albo otwórz słownik ręcznie.";
            return;
        }
        SelectedDictionary = panel;
        if (!ReferenceEquals(_selectedDictionary, panel))
            return;   // niezapisane zmiany innego słownika – komunikat ustawia SelectedDictionary
        Table.Filter = filter;   // filtr zostaje po wczytaniu wierszy (Reload)
        SelectedTab = item.Code is ProjectDictionaries.Sources or ProjectDictionaries.Settings ? DictionaryTab + 1 : DictionaryTab;
        Status = $"„{panel.Name}”{(filter.Length > 0 ? $" – filtr: {filter}" : "")}. Popraw słownik, zapisz i wykonaj ponownie etap na zakładce Przebieg "
                 + "(brak wiersza – „Dodaj wiersz”).";
    }

    /// <summary>Słownik z listy zakładki: bieżący, jeśli z niej pochodzi, inaczej pierwszy (niezapisane zmiany – bez zmiany).</summary>
    private void SelectFrom(IReadOnlyList<DictionaryPanelViewModel> list)
    {
        if (_selectedDictionary is null || !list.Contains(_selectedDictionary))
            SelectedDictionary = list.FirstOrDefault();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedDictionary));
        OnPropertyChanged(nameof(SelectedProjectDictionary));
        OnPropertyChanged(nameof(SelectedConfigDictionary));
    }

    /// <summary>Wiersze wybranego słownika projektu – edycja w komórkach (wspólna tabela słownika).</summary>
    public DictionaryTableViewModel Table { get; } = new();

    public ICommand ExportDictionaries { get; }
    public ICommand AddDictionaryRow { get; }
    public ICommand RemoveDictionaryRow { get; }
    public ICommand SaveDictionary { get; }
    public ICommand DiscardDictionary { get; }

    /// <summary>„Importuj źródła projektu” – pliki definicji ze słownika „Źródła danych”, potem odświeżenie kosztów.</summary>
    public ICommand ImportSources { get; }

    /// <summary>Kontekst walidacji (w tle): harmonogram względem WP z wczytanego „WP i CAM”, jeśli jest podgląd – inaczej z bazy.</summary>
    private Func<ProjectDictionaryContext> Context(string dictionary)
    {
        var wpPreview = Dictionaries.FirstOrDefault(p => p.Item.Code == ProjectDictionaries.WpCam)?.Preview;
        var fromPreview = dictionary != ProjectDictionaries.WpCam && wpPreview is { HasErrors: false } ? wpPreview.Working : null;
        var (tree, mapping) = (_saved, _mapping);
        return () => _service.Context(Code, tree, fromPreview ?? _service.Rows(ProjectDictionaries.WpCam, Code), mapping);
    }

    private async Task LoadDictionary(DictionaryPanelViewModel panel)
    {
        Table.CommitEdits();
        if (Table.HasPendingChanges)
        {
            Status = "Masz niezapisane zmiany słownika – zapisz albo odrzuć je przed wczytaniem z Excela.";
            return;
        }
        var path = _dialogs.OpenExcel($"Wczytaj słownik „{panel.Name}” programu {Code}");
        if (path is null)
            return;
        var context = Context(panel.Item.Code);
        await Try(async () =>
        {
            var (sheet, preview) = await Busy.Run($"Wczytywanie i sprawdzanie słownika „{panel.Name}”…", () =>
            {
                var found = ProjectService.FindSheet(path, panel.Item);
                return (found, _service.PreviewDictionary(panel.Item.Code, context(), path, Code, found));
            });
            panel.SetPreview(preview, $"{Path.GetFileName(path)}{(sheet is null ? "" : $" · arkusz „{sheet}”")}");
            Status = preview.HasErrors
                ? $"{panel.Name}: plik ma błędy (ERROR) – nie można go wczytać."
                : preview.HasChanges ? $"{panel.Name}: sprawdź różnice i zatwierdź." : $"{panel.Name}: plik nie zawiera zmian.";
        });
    }

    private async Task ApplyDictionary(DictionaryPanelViewModel panel)
    {
        var (context, preview) = (Context(panel.Item.Code), panel.Preview!);
        await Try(async () =>
        {
            var outcome = await Busy.Run($"Zapisywanie słownika „{panel.Name}”…", () => _service.ApplyDictionary(panel.Item.Code, context(), preview, Code));
            if (outcome.Status == SaveStatus.Saved)
                panel.SetPreview(null, "");
            await Reload(refreshMapping: false);
            Status = $"{panel.Name}: {outcome.Message}";
        });
    }

    private void DoRemoveDictionaryRow()
    {
        var (removed, _) = Table.RemoveSelected();
        Status = removed == 0 ? ""
            : _selectedDictionary?.Item.Code == PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.CostCategory
                ? "Zmiany programu usunięte z tabeli – po zapisie obowiązują pozycje ze słownika globalnego."
                : "Wiersze usunięte z tabeli – zapisz, aby zamknąć ich obowiązywanie (historia zostaje).";
    }

    /// <summary>Zapis tabeli wybranego słownika z regułami projektu; po zapisie – odświeżenie struktury i liczników.</summary>
    private async Task DoSaveDictionary(bool confirmWarnings)
    {
        var panel = _selectedDictionary!;
        var context = Context(panel.Item.Code);
        Table.CommitEdits();
        var (working, removed) = Table.State();
        await Try(async () =>
        {
            var outcome = await Busy.Run($"Zapisywanie słownika „{panel.Name}”…",
                () => _service.SaveDictionary(panel.Item.Code, context(), working, removed, Code, confirmWarnings));
            switch (outcome.Status)
            {
                case SaveStatus.Saved:
                case SaveStatus.NoChanges:
                    await Reload(refreshMapping: false, discard: true);
                    Table.SetIssues(outcome.Issues);
                    break;
                case SaveStatus.NeedsConfirmation:
                    Table.SetIssues(outcome.Issues, needsConfirmation: true);
                    break;
                default:
                    Table.SetIssues(outcome.Issues);
                    break;
            }
            Status = $"{panel.Name}: {outcome.Message}";
        });
    }

    private async Task DoExportDictionaries()
    {
        var path = _dialogs.SaveExcel("Pobierz słowniki programu", $"{Code}_slowniki_programu.xlsx");
        if (path is null)
            return;
        var (tree, mapping) = (_saved, _mapping);
        await Try(async () =>
        {
            await Busy.Run("Zapisywanie słowników programu do Excela…", () => _service.ExportDictionaries(path, Code, tree, mapping));
            Status = $"Zapisano {path} – popraw w Excelu i wczytaj ponownie (przed zapisem zobaczysz różnice).";
        });
    }

    /// <summary>
    /// Import plików definicji źródeł projektu (słownik „Źródła danych”): logowanie do SharePoint, import wspólny
    /// z innymi projektami (plik zapisuje się raz), potem ponowny odczyt struktury P1S i kosztów.
    /// </summary>
    private async Task DoImportSources()
    {
        Structure.CommitEdits();
        var import = _service.Imports!;
        IReadOnlySet<string> sources = new HashSet<string>();
        await Try(async () => sources = await Busy.Run("Odczyt źródeł programu…", () => _service.ProjectSources(Code)));
        if (sources.Count == 0)
        {
            Status = "Program nie ma źródeł danych – dodaj definicje źródeł w słowniku programu „Źródła danych”.";
            return;
        }
        if (!await import.SignIn())
        {
            Status = "Import nie rozpoczęty – logowanie do SharePoint anulowane.";
            return;
        }
        SourceImportResult? result = null;
        await Try(async () => result = await Busy.Run($"Import źródeł programu ({string.Join(", ", sources.Order())})…",
            () => import.Import(sources, null, CancellationToken.None)));
        if (result is null)
            return;
        await Reload(refreshMapping: true);
        var errors = result.Issues.Where(i => i.Level == CheckLevel.Error).Select(i => i.Message).Take(3).ToList();
        Status = $"Import źródeł programu #{result.BatchId}: {result.Summary}" + (errors.Count > 0 ? $" – {string.Join("; ", errors)}" : "");
    }
}
