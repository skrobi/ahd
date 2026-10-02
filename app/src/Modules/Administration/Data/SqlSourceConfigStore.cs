using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Administration.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Modules.Administration.Data;

/// <summary>
/// Konfiguracja importu w bazie (META_SourceDefinition, META_Parser, META_SourceLocation): nowa wersja zamyka
/// poprzednią; zapis zmienionej w międzyczasie wersji = konflikt. Zapis parsera zmienia też jego tabelę (SqlCanonical).
/// </summary>
public sealed class SqlSourceConfigStore(SqlDatabase db, IClock clock, ICurrentUser user) : ISourceConfigStore
{
    private readonly string _definitions = db.Table(DbTables.SourceDefinition);
    private readonly string _locations = db.Table(DbTables.SourceLocation);
    private readonly string _parsers = db.Table(DbTables.Parser);

    public IReadOnlyList<SourceDefinitionRow> Definitions() =>
        QueryDefinitions($"SELECT {SqlDefinitionRow.Columns} FROM {_definitions} WHERE SupersededAt IS NULL ORDER BY Code", null);

    public IReadOnlyList<SourceDefinitionRow> DefinitionHistory(long definitionId) =>
        QueryDefinitions($"SELECT {SqlDefinitionRow.Columns} FROM {_definitions} WHERE DefinitionId = @definitionId ORDER BY Version", new { definitionId });

    public string? SaveDefinition(DefinitionInput input, string signature, int parserVersion)
    {
        try
        {
            return db.InTransaction((connection, transaction) =>
            {
                var now = clock.Now;
                var definitionId = input.DefinitionId;
                var version = 1;
                if (definitionId is { } id)
                {
                    var current = connection.QuerySingleOrDefault<int?>(
                        $"SELECT Version FROM {_definitions} WITH (UPDLOCK, HOLDLOCK) WHERE DefinitionId = @id AND SupersededAt IS NULL",
                        new { id }, transaction);
                    if (current is null || current != input.Version)
                        return $"Definicję {input.Code} zmieniono w międzyczasie – odśwież dane.";
                    connection.Execute($"UPDATE {_definitions} SET SupersededAt = @now, SupersededBy = @user WHERE DefinitionId = @id AND SupersededAt IS NULL",
                        new { now, user = user.Account, id }, transaction);
                    version = current.Value + 1;
                }
                connection.Execute(
                    $"""
                    INSERT INTO {_definitions} (DefinitionId, Version, Code, Prefix, ReportType, Columns, Signature, Parser, ParserVersion, Mapping, Active, RecordedAt, RecordedBy)
                    VALUES (@DefinitionId, @Version, @Code, @Prefix, @ReportType, @Columns, @Signature, @Parser, @ParserVersion, @Mapping, @Active, @At, @User)
                    """,
                    new
                    {
                        DefinitionId = definitionId ?? db.NextLogicalId(connection, transaction), Version = version, input.Code, input.Prefix,
                        input.ReportType, Columns = SqlJson.Write(input.Columns), Signature = signature, input.Parser, ParserVersion = parserVersion,
                        Mapping = input.Mapping is { Count: > 0 } mapping ? SqlJson.Write(mapping) : null,
                        input.Active, At = now, User = user.Account,
                    },
                    transaction);
                return (string?)null;
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return $"Kod {input.Code} albo prefiks {input.Prefix} jest już używany – odśwież dane.";
        }
    }

    public string? DeleteDefinition(long definitionId, int version) =>
        db.InTransaction((connection, transaction) =>
        {
            var closed = connection.Execute(
                $"UPDATE {_definitions} SET SupersededAt = @now, SupersededBy = @user WHERE DefinitionId = @definitionId AND Version = @version AND SupersededAt IS NULL",
                new { now = clock.Now, user = user.Account, definitionId, version }, transaction);
            return closed == 1 ? null : "Definicję zmieniono albo usunięto w międzyczasie – odśwież dane.";
        });

    public IReadOnlyList<ParserRow> Parsers() =>
        QueryParsers($"SELECT {SqlParserRow.Columns} FROM {_parsers} WHERE SupersededAt IS NULL ORDER BY Code", null);

    public IReadOnlyList<ParserRow> ParserHistory(long parserId) =>
        QueryParsers($"SELECT {SqlParserRow.Columns} FROM {_parsers} WHERE ParserId = @parserId ORDER BY Version", new { parserId });

    public ParserSaveOutcome SaveParser(ParserInput input, string table)
    {
        try
        {
            return db.InTransaction((connection, transaction) =>
            {
                var now = clock.Now;
                var version = 1;
                if (input.ParserId is { } id)
                {
                    var current = connection.QuerySingleOrDefault<int?>(
                        $"SELECT Version FROM {_parsers} WITH (UPDLOCK, HOLDLOCK) WHERE ParserId = @id AND SupersededAt IS NULL",
                        new { id }, transaction);
                    if (current is null || current != input.Version)
                        return new ParserSaveOutcome($"Parser {input.Code} zmieniono w międzyczasie – odśwież dane.", []);
                    connection.Execute($"UPDATE {_parsers} SET SupersededAt = @now, SupersededBy = @user WHERE ParserId = @id AND SupersededAt IS NULL",
                        new { now, user = user.Account, id }, transaction);
                    version = current.Value + 1;
                }
                var parserId = input.ParserId ?? db.NextLogicalId(connection, transaction);
                connection.Execute(
                    $"""
                    INSERT INTO {_parsers} (ParserId, Version, Code, Name, TableName, Fields, Active, RecordedAt, RecordedBy)
                    VALUES (@ParserId, @Version, @Code, @Name, @Table, @Fields, @Active, @At, @User)
                    """,
                    new { ParserId = parserId, Version = version, input.Code, input.Name, Table = table, Fields = SqlJson.Write(input.Fields), input.Active, At = now, User = user.Account },
                    transaction);
                var parser = new ParserRow(0, parserId, version, input.Code, input.Name, table, input.Fields, input.Active, now, user.Account, null, null);
                var changes = SqlCanonical.EnsureTable(db, connection, transaction, parser);
                return new ParserSaveOutcome(null, changes);
            });
        }
        catch (InvalidOperationException ex)
        {
            return new ParserSaveOutcome(ex.Message, []);   // niezgodny typ kolumny, niedozwolona nazwa – transakcja wycofana
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return new ParserSaveOutcome($"Kod {input.Code} albo tabela CAN_{table} jest już używana – odśwież dane.", []);
        }
        catch (SqlException ex) when (ex.Number is 229 or 262 or 1088 or 4902)
        {
            return new ParserSaveOutcome($"Brak uprawnień do zmiany tabel w bazie ({ex.Message}) – zmianę parsera wykona osoba z prawem tworzenia i zmiany tabel.", []);
        }
    }

    public IReadOnlyList<SourceLocationRow> Locations()
    {
        using var connection = db.Open();
        return connection.Query<SqlLocationRow>($"SELECT {SqlLocationRow.Columns} FROM {_locations} WHERE SupersededAt IS NULL ORDER BY Name")
            .Select(r => r.ToRow())
            .ToList();
    }

    public string? SaveLocation(LocationInput input)
    {
        try
        {
            return db.InTransaction((connection, transaction) =>
            {
                var now = clock.Now;
                var version = 1;
                if (input.LocationId is { } id)
                {
                    var current = connection.QuerySingleOrDefault<int?>(
                        $"SELECT Version FROM {_locations} WITH (UPDLOCK, HOLDLOCK) WHERE LocationId = @id AND SupersededAt IS NULL",
                        new { id }, transaction);
                    if (current is null || current != input.Version)
                        return $"Lokalizację {input.Name} zmieniono w międzyczasie – odśwież dane.";
                    connection.Execute($"UPDATE {_locations} SET SupersededAt = @now, SupersededBy = @user WHERE LocationId = @id AND SupersededAt IS NULL",
                        new { now, user = user.Account, id }, transaction);
                    version = current.Value + 1;
                }
                connection.Execute(
                    $"INSERT INTO {_locations} (LocationId, Version, Name, Path, Active, RecordedAt, RecordedBy) VALUES (@LocationId, @Version, @Name, @Path, @Active, @At, @User)",
                    new
                    {
                        LocationId = input.LocationId ?? db.NextLogicalId(connection, transaction), Version = version, input.Name, input.Path, input.Active,
                        At = now, User = user.Account,
                    },
                    transaction);
                return (string?)null;
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return $"Lokalizacja o nazwie {input.Name} już istnieje – odśwież dane.";
        }
    }

    private List<ParserRow> QueryParsers(string sql, object? parameters)
    {
        using var connection = db.Open();
        return connection.Query<SqlParserRow>(sql, parameters).Select(r => r.ToRow()).ToList();
    }

    private List<SourceDefinitionRow> QueryDefinitions(string sql, object? parameters)
    {
        using var connection = db.Open();
        return connection.Query<SqlDefinitionRow>(sql, parameters).Select(r => r.ToRow()).ToList();
    }
}
