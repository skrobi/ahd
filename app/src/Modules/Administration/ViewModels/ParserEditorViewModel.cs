using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>
/// Administracja → Parsery: parser = tabela danych kanonicznych i jej pola (nazwa w bazie, nazwa kolumny w pliku jako
/// podpowiedź mapowania, typ, długość, dopełnianie zerami). Zapis tworzy nową wersję i zakłada / rozszerza tabelę.
/// </summary>
public sealed class ParserEditorViewModel : ObservableObject
{
    private readonly SourceConfigService _service;
    private readonly Action _saved;
    private ParserRow? _selected;
    private long? _parserId;
    private int? _version;
    private string _code = "", _name = "", _table = "", _message = "";
    private bool _active = true;

    public ParserEditorViewModel(SourceConfigService service, Action saved)
    {
        _service = service;
        _saved = saved;
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

    public string TableText => $"Tabela danych kanonicznych: CAN_{(IsNew ? _code.Trim().ToUpperInvariant() : _table)}";

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
        Message = "Nowy parser: kod (np. FORECAST), nazwa i pola – każde pole to kolumna tabeli CAN_<kod>.";
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

    private void Changed()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(TableText));
    }
}
