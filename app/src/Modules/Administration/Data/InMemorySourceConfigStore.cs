using PzlEv.Modules.Administration.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Administration.Data;

/// <summary>Konfiguracja importu w pamięci (tabele meta.SourceDefinition, meta.SourceLocation).</summary>
public sealed class InMemorySourceConfigStore(InMemoryDatabase db, IClock clock, ICurrentUser user) : ISourceConfigStore
{
    public IReadOnlyList<SourceDefinitionRow> Definitions() =>
        db.Read(() => db.Table<SourceDefinitionRow>(DbTables.SourceDefinition).Where(d => d.IsCurrent).OrderBy(d => d.Code).ToList());

    public IReadOnlyList<SourceDefinitionRow> DefinitionHistory(long definitionId) =>
        db.Read(() => db.Table<SourceDefinitionRow>(DbTables.SourceDefinition).Where(d => d.DefinitionId == definitionId).OrderBy(d => d.Version).ToList());

    public string? SaveDefinition(DefinitionInput input, string signature, int parserVersion)
    {
        var conflict = db.Write(() =>
        {
            var rows = db.Table<SourceDefinitionRow>(DbTables.SourceDefinition);
            var now = clock.Now;
            SourceDefinitionRow? existing = null;
            if (input.DefinitionId is { } id)
            {
                existing = rows.FirstOrDefault(d => d.DefinitionId == id && d.IsCurrent);
                if (existing is null || existing.Version != input.Version)
                    return $"Definicję {input.Code} zmieniono w międzyczasie – odśwież dane.";
            }
            if (rows.Any(d => d.IsCurrent && d.DefinitionId != input.DefinitionId &&
                              (string.Equals(d.Prefix, input.Prefix, StringComparison.OrdinalIgnoreCase) || d.Code == input.Code)))
                return $"Kod {input.Code} albo prefiks {input.Prefix} jest już używany – odśwież dane.";

            var row = new SourceDefinitionRow(
                db.NextId(DbTables.SourceDefinition),
                existing?.DefinitionId ?? db.NextId(DbTables.SourceDefinition + ".DefinitionId"),
                (existing?.Version ?? 0) + 1,
                input.Code, input.Prefix, input.ReportType, input.Columns.ToList(), signature, input.Parser, parserVersion, input.Active,
                now, user.Account, null, null);
            if (existing is not null)
                rows[rows.IndexOf(existing)] = existing with { SupersededAt = now, SupersededBy = user.Account };
            rows.Add(row);
            return null;
        });
        if (conflict is null)
            db.Commit();
        return conflict;
    }

    public string? DeleteDefinition(long definitionId, int version)
    {
        var conflict = db.Write(() =>
        {
            var rows = db.Table<SourceDefinitionRow>(DbTables.SourceDefinition);
            var existing = rows.FirstOrDefault(d => d.DefinitionId == definitionId && d.IsCurrent);
            if (existing is null || existing.Version != version)
                return "Definicję zmieniono albo usunięto w międzyczasie – odśwież dane.";
            rows[rows.IndexOf(existing)] = existing with { SupersededAt = clock.Now, SupersededBy = user.Account };
            return null;
        });
        if (conflict is null)
            db.Commit();
        return conflict;
    }

    public IReadOnlyList<SourceLocationRow> Locations() =>
        db.Read(() => db.Table<SourceLocationRow>(DbTables.SourceLocation).Where(l => l.IsCurrent).OrderBy(l => l.Name).ToList());

    public string? SaveLocation(LocationInput input)
    {
        var conflict = db.Write(() =>
        {
            var rows = db.Table<SourceLocationRow>(DbTables.SourceLocation);
            var now = clock.Now;
            SourceLocationRow? existing = null;
            if (input.LocationId is { } id)
            {
                existing = rows.FirstOrDefault(l => l.LocationId == id && l.IsCurrent);
                if (existing is null || existing.Version != input.Version)
                    return $"Lokalizację {input.Name} zmieniono w międzyczasie – odśwież dane.";
            }
            if (rows.Any(l => l.IsCurrent && l.LocationId != input.LocationId && string.Equals(l.Name, input.Name, StringComparison.OrdinalIgnoreCase)))
                return $"Lokalizacja o nazwie {input.Name} już istnieje – odśwież dane.";

            var row = new SourceLocationRow(
                db.NextId(DbTables.SourceLocation),
                existing?.LocationId ?? db.NextId(DbTables.SourceLocation + ".LocationId"),
                (existing?.Version ?? 0) + 1,
                input.Name, input.Path, input.Active, now, user.Account, null, null);
            if (existing is not null)
                rows[rows.IndexOf(existing)] = existing with { SupersededAt = now, SupersededBy = user.Account };
            rows.Add(row);
            return null;
        });
        if (conflict is null)
            db.Commit();
        return conflict;
    }
}
