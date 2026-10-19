using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>Rodzaj klocka – kolor na palecie i w formule.</summary>
public enum BlockCategory { Cost, Pool, Rate, Operator }

/// <summary>Klocek palety: etykieta, klocek formuły, rodzaj; nieaktywny – klocek narzutu własnej puli.</summary>
public sealed record PaletteBlock(string Label, CasFormula.Token Token, BlockCategory Category, bool IsEnabled = true, string? Tip = null);

/// <summary>
/// Klocek w formule: pozycja, etykieta, rodzaj; CaretBefore – kursor wstawiania przed klockiem. IsEnd – znacznik końca
/// formuły (kursor za ostatnim klockiem, miejsce upuszczenia na końcu).
/// </summary>
public sealed class FormulaBlock(int index, string label, BlockCategory category, bool caretBefore, bool isEnd = false)
{
    public int Index { get; } = index;
    public string Label { get; } = label;
    public BlockCategory Category { get; } = category;
    public bool CaretBefore { get; } = caretBefore;
    public bool IsEnd { get; } = isEnd;
    public bool IsBlock => !IsEnd;
}

/// <summary>
/// Edytor formuły narzutu puli w grupie (okno „Formuła narzutu”): paleta klocków (Base cost elements, narzuty pul,
/// wskaźniki pul, + − × ÷ ( )), formuła z klocków z kursorem wstawiania (kliknięcie, przeciąganie, Backspace / Delete),
/// formuła do czytania i kontrola poprawności na bieżąco. Wynik: klocki (Zatwierdź) albo pula nienaliczana (Usuń narzut).
/// </summary>
public sealed class CasFormulaBuilderViewModel : ObservableObject
{
    private readonly CasFormulaDraft _draft;
    private readonly IReadOnlyDictionary<long, string> _names;
    private readonly IReadOnlySet<long> _known;

    public CasFormulaBuilderViewModel(string group, CasBurden.Pool pool, IReadOnlyList<CasBurden.Pool> pools, IReadOnlyList<CasFormula.Token>? tokens)
    {
        Group = group;
        PoolName = pool.Name;
        IsNew = tokens is null;
        _names = pools.ToDictionary(p => p.Id, p => p.Name);
        _known = pools.Select(p => p.Id).ToHashSet();
        _draft = new CasFormulaDraft(pool.Id, tokens ?? []);   // pula nienaliczana – pusta formuła
        CostBlocks = [new PaletteBlock(CasBurden.Base, CasFormula.Token.BaseCost, BlockCategory.Cost, Tip: "Koszt bazowy kolumny raportu (okres albo rok)")];
        PoolBlocks = pools.Select(p => new PaletteBlock(p.Name, CasFormula.Token.Pool(p.Id), BlockCategory.Pool, p.Id != pool.Id,
            p.Id == pool.Id ? "Narzut tej kategorii – nie może wejść do własnej formuły" : $"Narzut kategorii {p.Name} w tym samym wierszu")).ToList();
        RateBlocks = pools.Select(p => new PaletteBlock($"wsk. {p.Name}", CasFormula.Token.Rate(p.Id), BlockCategory.Rate,
            Tip: $"Wskaźnik kategorii {p.Name} z wiersza wskaźników (słownik „Wskaźniki narzutu CAS”)")).OrderBy(b => b.Token.Id == pool.Id ? 0 : 1).ToList();
        OperatorBlocks =
        [
            new("+", CasFormula.Token.Op('+'), BlockCategory.Operator), new("−", CasFormula.Token.Op('-'), BlockCategory.Operator),
            new("×", CasFormula.Token.Op('*'), BlockCategory.Operator), new("÷", CasFormula.Token.Op('/'), BlockCategory.Operator),
            new("(", CasFormula.Token.Open, BlockCategory.Operator), new(")", CasFormula.Token.Close, BlockCategory.Operator),
        ];
        Add = new RelayCommand(p => { if (p is PaletteBlock { IsEnabled: true } block) Insert(block, null); });
        RemoveBlock = new RelayCommand(p => { if (p is FormulaBlock block) Change(() => _draft.RemoveAt(block.Index)); });
        Clear = new RelayCommand(_ => Change(() => _draft.Replace([])));
        UseDefault = new RelayCommand(_ => Change(() => _draft.Replace(CasFormula.Default(pool.Id))));
        Refresh();
    }

    public string Group { get; }
    public string PoolName { get; }

    /// <summary>Pula nie miała formuły w grupie (puste pole macierzy) – edytor zaczyna od pustej formuły.</summary>
    public bool IsNew { get; }

    public string Title => IsNew ? $"Narzut {PoolName} w grupie {Group} – kategoria nienaliczana, nowa formuła" : $"Narzut {PoolName} w grupie {Group}";

    public IReadOnlyList<PaletteBlock> CostBlocks { get; }
    public IReadOnlyList<PaletteBlock> PoolBlocks { get; }
    public IReadOnlyList<PaletteBlock> RateBlocks { get; }
    public IReadOnlyList<PaletteBlock> OperatorBlocks { get; }

    public ObservableCollection<FormulaBlock> Blocks { get; } = [];

    /// <summary>Kursor za ostatnim klockiem (znacznik na końcu formuły).</summary>
    public bool CaretAtEnd => _draft.Cursor == _draft.Tokens.Count;

    public string Readable => _draft.Tokens.Count == 0 ? "" : $"= {CasFormula.Display(_draft.Tokens, Name)}";

    public string? Error => _draft.Error(_known);

    public bool IsValid => Error is null;

    public string Check => Error ?? "Formuła poprawna.";

    public IReadOnlyList<CasFormula.Token> Tokens => _draft.Tokens.ToList();

    public ICommand Add { get; }
    public ICommand RemoveBlock { get; }
    public ICommand Clear { get; }
    public ICommand UseDefault { get; }

    /// <summary>Wstawienie klocka palety: at – pozycja upuszczenia (null – kursor).</summary>
    public void Insert(PaletteBlock block, int? at)
    {
        if (block.IsEnabled)
            Change(() => _draft.Insert(block.Token, at));
    }

    public void Move(int from, int to) => Change(() => _draft.Move(from, to));

    public void SetCursor(int position) => Change(() => _draft.MoveCursor(position));

    public void Backspace() => Change(_draft.Backspace);

    public void Delete() => Change(_draft.Delete);

    public void Left() => SetCursor(_draft.Cursor - 1);

    public void Right() => SetCursor(_draft.Cursor + 1);

    public void Home() => SetCursor(0);

    public void End() => SetCursor(_draft.Tokens.Count);

    private string Name(long id) => _names.TryGetValue(id, out var name) ? name : "(kategoria usunięta)";

    private void Change(Action action)
    {
        action();
        Refresh();
    }

    private void Refresh()
    {
        Blocks.Clear();
        for (var i = 0; i < _draft.Tokens.Count; i++)
        {
            var token = _draft.Tokens[i];
            var (label, category) = token.Kind switch
            {
                CasFormula.Kind.Base => (CasBurden.Base, BlockCategory.Cost),
                CasFormula.Kind.Pool => (Name(token.Id), BlockCategory.Pool),
                CasFormula.Kind.Rate => ($"wsk. {Name(token.Id)}", BlockCategory.Rate),
                CasFormula.Kind.Operator => (token.Symbol switch { '*' => "×", '/' => "÷", '-' => "−", _ => "+" }, BlockCategory.Operator),
                CasFormula.Kind.Open => ("(", BlockCategory.Operator),
                _ => (")", BlockCategory.Operator),
            };
            Blocks.Add(new FormulaBlock(i, label, category, i == _draft.Cursor));
        }
        Blocks.Add(new FormulaBlock(_draft.Tokens.Count, "", BlockCategory.Operator, CaretAtEnd, isEnd: true));
        OnPropertyChanged(nameof(CaretAtEnd));
        OnPropertyChanged(nameof(Readable));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Check));
    }
}
