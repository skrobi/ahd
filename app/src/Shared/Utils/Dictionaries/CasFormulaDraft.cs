namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Formuła w budowie w edytorze z klocków (okno „Formuła narzutu”): klocki i kursor wstawiania (pozycja 0..Count).
/// Wstawienie klocka (kliknięcie albo upuszczenie z palety) – w miejscu kursora; przesunięcie klocka przeciągnięciem;
/// Backspace / Delete – usuwa klocek przed / za kursorem. Poprawność – CasFormula.Error i zakaz klocka narzutu własnej puli.
/// </summary>
public sealed class CasFormulaDraft(long pool, IEnumerable<CasFormula.Token> tokens)
{
    private readonly List<CasFormula.Token> _tokens = tokens.ToList();

    /// <summary>Pula, której narzut liczy formuła (klocek jej narzutu – niedozwolony).</summary>
    public long Pool { get; } = pool;

    public IReadOnlyList<CasFormula.Token> Tokens => _tokens;

    public int Cursor { get; private set; } = tokens.Count();

    public void MoveCursor(int position) => Cursor = Math.Clamp(position, 0, _tokens.Count);

    /// <summary>Wstawia klocek w pozycji (domyślnie – kursor); kursor za wstawionym klockiem.</summary>
    public void Insert(CasFormula.Token token, int? at = null)
    {
        var position = Math.Clamp(at ?? Cursor, 0, _tokens.Count);
        _tokens.Insert(position, token);
        Cursor = position + 1;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _tokens.Count)
            return;
        _tokens.RemoveAt(index);
        if (Cursor > index)
            Cursor--;
    }

    public void Backspace() => RemoveAt(Cursor - 1);

    public void Delete() => RemoveAt(Cursor);

    /// <summary>Przeniesienie klocka (przeciągnięcie): to – pozycja wstawienia liczona przed usunięciem (0..Count).</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || from >= _tokens.Count)
            return;
        to = Math.Clamp(to, 0, _tokens.Count);
        var token = _tokens[from];
        _tokens.RemoveAt(from);
        if (to > from)
            to--;
        _tokens.Insert(to, token);
        Cursor = to + 1;
    }

    public void Replace(IEnumerable<CasFormula.Token> tokens)
    {
        _tokens.Clear();
        _tokens.AddRange(tokens);
        Cursor = _tokens.Count;
    }

    /// <summary>Błąd formuły albo null (known – identyfikatory pul ze słownika).</summary>
    public string? Error(IReadOnlySet<long>? known = null) =>
        _tokens.Any(t => t.Kind == CasFormula.Kind.Pool && t.Id == Pool)
            ? "Narzut kategorii nie może zależeć od niego samego – usuń klocek narzutu tej kategorii (wskaźnik kategorii jest dozwolony)."
            : CasFormula.Error(_tokens, known);
}
