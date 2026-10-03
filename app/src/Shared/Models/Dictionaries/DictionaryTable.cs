namespace PzlEv.Shared.Models.Dictionaries;

/// <summary>
/// Tabela słownika w bazie: opis słownika (kolumny, klucz), tabela (np. dict.Calendar) i mapowanie kolumn
/// opisu na kolumny tabeli. Moduł przekazuje magazynowi tabele słowników, z którymi pracuje.
/// </summary>
public sealed record DictionaryTable(DictionarySpec Spec, string Table, IReadOnlyList<(string Spec, string Column)> Columns);
