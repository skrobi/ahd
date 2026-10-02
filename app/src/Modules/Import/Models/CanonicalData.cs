using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Modules.Import.Models;

/// <summary>Dane kanoniczne pliku do zapisu w tabeli parsera: zmapowane pola i wiersze (wartości w kolejności Fields).</summary>
public sealed record CanonicalData(ParserRow Parser, IReadOnlyList<ParserField> Fields, IReadOnlyList<CanonicalRow> Rows);
