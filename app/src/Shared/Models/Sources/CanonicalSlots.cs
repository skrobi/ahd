namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Sloty stałej tabeli danych kanonicznych CAN_Row (migracja 007): T01–T40 tekst do 400 znaków, L01–L05 tekst do 4000,
/// N01–N20 kwota / liczba, I01–I10 liczba całkowita, D01–D10 data. Pole parsera dostaje slot przy zapisie parsera
/// i zachowuje go we wszystkich wersjach; slot użyty przez jakąkolwiek wersję parsera nie jest przydzielany innemu polu
/// (dane zapisanych plików nie zmieniają znaczenia). Nowe pole i nowy parser nie zmieniają tabel.
/// </summary>
public static class CanonicalSlots
{
    public const int ShortTextLength = 400;

    private static readonly (char Kind, int Count, string Label)[] Kinds =
    [
        ('T', 40, "tekst do 400 znaków"),
        ('L', 5, "tekst do 4000 znaków"),
        ('N', 20, "kwota / liczba"),
        ('I', 10, "liczba całkowita"),
        ('D', 10, "data"),
    ];

    /// <summary>Wszystkie sloty w kolejności kolumn tabeli.</summary>
    public static IEnumerable<string> All => Kinds.SelectMany(k => Enumerable.Range(1, k.Count).Select(n => Name(k.Kind, n)));

    public static char Kind(ParserField field) => field.Type switch
    {
        FieldTypes.Decimal => 'N',
        FieldTypes.Integer => 'I',
        FieldTypes.Date => 'D',
        _ => (field.Length ?? FieldTypes.DefaultTextLength) > ShortTextLength ? 'L' : 'T',
    };

    public static string Name(char kind, int number) => $"{kind}{number:00}";

    public static bool IsSlot(string? slot) => slot is { Length: 3 } && All.Contains(slot);

    public static string Label(char kind) => Kinds.First(k => k.Kind == kind).Label;

    public static int Capacity(char kind) => Kinds.First(k => k.Kind == kind).Count;

    /// <summary>
    /// Sloty pól zapisywanej wersji: pole znane z historii parsera (po nazwie) zachowuje slot – zmiana rodzaju (typ albo
    /// długość tekstu ponad 400 znaków) jest błędem; nowe pole dostaje pierwszy wolny slot swojego rodzaju. Zwraca pola
    /// ze slotami, błędy i opis przydziałów („nowe pole X → T21”).
    /// </summary>
    public static (IReadOnlyList<ParserField> Fields, IReadOnlyList<string> Errors, IReadOnlyList<string> Assigned) Assign(
        IReadOnlyList<ParserField> fields, IEnumerable<ParserField> history)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in history.Where(f => IsSlot(f.Slot)))
        {
            known.TryAdd(field.Field, field.Slot!);
            used.Add(field.Slot!);
        }

        var result = new List<ParserField>(fields.Count);
        var errors = new List<string>();
        var assigned = new List<string>();
        foreach (var field in fields)
        {
            var kind = Kind(field);
            if (known.TryGetValue(field.Field, out var slot))
            {
                if (slot[0] != kind)
                    errors.Add($"Pole {field.Field}: dane zapisane jako {Label(slot[0])} (slot {slot}) – zmiana na {Label(kind)} niemożliwa; dodaj nowe pole");
                result.Add(field with { Slot = slot });
                continue;
            }
            var free = Enumerable.Range(1, Capacity(kind)).Select(n => Name(kind, n)).FirstOrDefault(s => !used.Contains(s));
            if (free is null)
            {
                errors.Add($"Pole {field.Field}: brak wolnego slotu – {Label(kind)}: najwięcej {Capacity(kind)} pól w parserze");
                result.Add(field with { Slot = null });
                continue;
            }
            used.Add(free);
            known[field.Field] = free;
            assigned.Add($"nowe pole {field.Field} → {free}");
            result.Add(field with { Slot = free });
        }
        return (result, errors, assigned);
    }

    /// <summary>Zajęte sloty według rodzaju, np. „T 19/40 · N 4/20 · I 2/10 · D 1/10”.</summary>
    public static string Usage(IEnumerable<ParserField> fields)
    {
        var slots = fields.Where(f => IsSlot(f.Slot)).Select(f => f.Slot!).Distinct().ToList();
        return string.Join(" · ", Kinds.Where(k => slots.Any(s => s[0] == k.Kind)).Select(k => $"{k.Kind} {slots.Count(s => s[0] == k.Kind)}/{k.Count}"));
    }
}
