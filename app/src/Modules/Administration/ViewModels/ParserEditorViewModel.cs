using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>
/// Administracja → Parsery: parser = pola danych kanonicznych – pole w bazie, kolumna w pliku, typ, długość,
/// dopełnianie zerami, wymagane. Parser pilnuje układu pliku. „Kolumny z pliku…” porównuje parser z wierszem nagłówków
/// pliku (nowe kolumny → propozycje pól, przykłady wartości). Zapis tworzy nową wersję; nowe pola dostają sloty w CAN_Row (bez zmian tabel).
/// </summary>
public sealed class ParserEditorViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", ModuleKeys.Administration);

    private readonly SourceConfigService _service;
    private readonly IFileDialogs _dialogs;
    private readonly Action _saved;
    private ParserRow? _selected;
    private long? _parserId;
    private int? _version;
    private string _code = "", _name = "", _table = "", _message = "";
    private bool _active = true;

    public ParserEditorViewModel(SourceConfigService service, IFileDialogs dialogs, Action saved)
    {
        _service = service;
        _dialogs = dialogs;
        _saved = saved;
        ColumnsFromFile = new RelayCommand(_ => LoadColumnsFromFile());
        NewParser = new RelayCommand(_ => Clear());
        AddField = new RelayCommand(_ => Fields.Add(new ParserFieldRowViewModel { Length = FieldTypes.DefaultTextLength.ToString() }));
        RemoveField = new RelayCommand(p =>
        {
            if (p is ParserFieldRowViewModel row)
                Fields.Remove(row);
        });
        Save = new RelayCommand(_ => DoSave());
        Reload();
        Clear();
    }

    public ObservableCollection<ParserRow> Parsers { get; } = [];

    public ObservableCollection<ParserFieldRowViewModel> Fields { get; } = [];

    public ObservableCollection<ParserRow> History { get; } = [];

    public IReadOnlyList<ParserOption> TypeOptions { get; } = FieldTypes.All.Select(t => new ParserOption(t, FieldTypes.Label(t))).ToList();

    public ICommand NewParser { get; }
    public ICommand ColumnsFromFile { get; }
    public ICommand AddField { get; }
    public ICommand RemoveField { get; }
    public ICommand Save { get; }

    public ParserRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value) || value is null)
                return;
            _parserId = value.ParserId;
            _version = value.Version;
            Code = value.Code;
            Name = value.Name;
            Active = value.Active;
            _table = value.Table;
            Fields.Clear();
            foreach (var parserField in value.Fields)
                Fields.Add(ParserFieldRowViewModel.From(parserField));
            History.Clear();
            foreach (var version in _service.ParserHistory(value.ParserId).Reverse())
                History.Add(version);
            Message = "";
            Changed();
        }
    }

    public string Code
    {
        get => _code;
        set
        {
            if (SetProperty(ref _code, value ?? ""))
                OnPropertyChanged(nameof(TableText));
        }
    }

    public string Name { get => _name; set => SetProperty(ref _name, value ?? ""); }
    public bool Active { get => _active; set => SetProperty(ref _active, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public bool IsNew => _parserId is null;

    public string FormTitle => IsNew ? "Nowy parser" : $"Zmiana parsera {_code} (wersja {_version})";

    public string TableText => IsNew
        ? "Dane w stałej tabeli CAN_Row – pola dostaną sloty przy zapisie"
        : $"Dane w stałej tabeli CAN_Row – zajęte sloty: {CanonicalSlots.Usage(Fields.Select(f => f.ToField()))}";

    /// <summary>Lista parserów od nowa (po zapisie albo zmianie z innego ekranu).</summary>
    public void Reload()
    {
        Parsers.Clear();
        foreach (var parser in _service.Parsers())
            Parsers.Add(parser);
    }

    private void Clear()
    {
        Selected = null;
        _parserId = null;
        _version = null;
        _table = "";
        Code = Name = "";
        Active = true;
        Fields.Clear();
        History.Clear();
        Message = "Nowy parser: kod (np. FORECAST), nazwa i pola – najprościej „Kolumny z pliku…” z przykładowym plikiem źródła.";
        Changed();
    }

    private void DoSave()
    {
        var input = new ParserInput(_parserId, _version, Code, Name, Fields.Select(f => f.ToField()).ToList(), Active);
        var result = _service.SaveParser(input);
        Message = string.Join(Environment.NewLine, new[] { result.Message }.Concat(result.Issues.Select(i => "• " + i.Message)));
        if (!result.Success)
            return;
        var code = _parserId is null ? Code.Trim().ToUpperInvariant() : _code;
        Reload();
        Selected = Parsers.FirstOrDefault(p => p.Code == code);
        Message = result.Message;
        _saved();
    }

    /// <summary>
    /// Wiersz nagłówków przykładowego pliku (ten sam odczyt co import): kolumny bez pola dochodzą jako nowe pola (tekst –
    /// sprawdź typ), pola z kolumną spoza pliku są oznaczone; przykłady wartości przy polach.
    /// </summary>
    private void LoadColumnsFromFile()
    {
        var path = _dialogs.OpenExcel("Przykładowy plik źródła – kolumny z wiersza nagłówków");
        if (path is null)
            return;
        try
        {
            var source = TabularFileReader.Open(File.ReadAllBytes(path), Path.GetFileName(path));
            var data = new TabularData(source.Headers, source.Rows().Take(200).ToList(), source.FileType, source.Sheet, source.Encoding, source.Delimiter);   // przykłady z początku pliku
            var (added, missing) = SourceConfigService.CompareWithFile(Fields.Select(f => f.ToField()).ToList(), data.Headers);
            foreach (var field in added)
                Fields.Add(ParserFieldRowViewModel.From(field));
            foreach (var row in Fields)
            {
                var index = data.Headers.ToList().FindIndex(h => string.Equals(h.Trim(), row.Column.Trim(), StringComparison.OrdinalIgnoreCase));
                row.Sample = row.Column.Trim().Length == 0 ? ""
                    : index < 0 ? "— brak kolumny w pliku —"
                    : data.Rows.Select(r => index < r.Length ? r[index] : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
            }
            Message = $"Plik {Path.GetFileName(path)}{(data.Sheet is null ? "" : $" (arkusz {data.Sheet})")}: {data.Headers.Count} kolumn. " +
                      (added.Count == 0 ? "Nowych kolumn brak. " : $"Nowe pola ({added.Count}): {string.Join(", ", added.Select(f => f.Column))} – sprawdź typy. ") +
                      (missing.Count == 0 ? "" : $"Kolumn parsera nie ma w pliku: {string.Join(", ", missing.Select(f => f.Column))} – wyczyść „Kolumna w pliku” albo usuń pole. ") +
                      "Zapisz parser.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Kolumny z pliku {Path}: błąd odczytu", path);
            Message = $"Nie udało się odczytać pliku {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(TableText));
    }
}
