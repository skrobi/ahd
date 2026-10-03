using Dapper;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Import.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Migracje schematu (sql/mssql) i nazwy obiektów z konfiguracji.</summary>
public sealed class SqlMigrationsTests
{
    private static readonly SqlDatabase Prod = new(new SqlSettings("pzltestdb.intl.lmco.com", "PZLTEST", "FINOP", "PZLEV_"), "0.8.0");

    [Fact]
    public void Logical_names_map_to_schema_and_signature()
    {
        Assert.Equal("[FINOP].[PZLEV_META_ImportBatch]", Prod.Table("meta.ImportBatch"));
        Assert.Equal("[FINOP].[PZLEV_DICT_FxRate]", Prod.Table("dict.FxRate"));
        Assert.Contains("Integrated Security=True", Prod.ConnectionString);
        Assert.Contains("Application Name=\"PZL-EV 0.8.0\"", Prod.ConnectionString);
    }

    [Fact]
    public void Scripts_are_embedded_numbered_and_parameterised()
    {
        var scripts = SqlMigrations.All();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], scripts.Select(s => s.Number));
        Assert.Equal([false, true, false, false, false, false, false], scripts.Select(s => s.IsPresets));   // 002_dane_startowe – dane startowe
        Assert.Equal(7, SqlMigrations.Required);

        var batches = SqlMigrations.Batches(scripts[0].Text, "FINOP", "PZLEV_").ToList();
        Assert.True(batches.Count > 5);
        Assert.Contains(batches, b => b.Contains("CREATE TABLE [FINOP].[PZLEV_META_Project]"));
        Assert.All(scripts.SelectMany(s => SqlMigrations.Batches(s.Text, "FINOP", "PZLEV_")),
            b => Assert.False(b.Contains("$(Schema)") || b.Contains("$(Prefix)")));
    }

    [SqlFact]
    public void Migrations_create_stage_1_tables_and_skip_executed_scripts()
    {
        using var database = new TestDatabase();   // bez danych startowych

        Assert.Equal(["002_dane_startowe.sql"], SqlMigrations.Pending(database.Sql).Select(s => s.Name));
        Assert.Empty(SqlMigrations.Apply(database.Sql, presets: false));
        Assert.Equal(["002_dane_startowe.sql"], SqlMigrations.Apply(database.Sql));   // 001 wykonana – pominięta
        Assert.Empty(SqlMigrations.Apply(database.Sql));                              // wszystko wykonane
        Assert.Empty(SqlMigrations.Pending(database.Sql));
        var status = SqlMigrations.Status(database.Sql);
        Assert.All(status, s => Assert.NotNull(s.AppliedAt));
        Assert.Equal(7, SqlMigrations.CurrentVersion(database.Sql));
        using var connection = database.Sql.Open();
        var tables = connection.Query<string>(
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema AND LEFT(t.name, LEN(@prefix)) = @prefix",
            new { schema = TestDatabase.Schema, prefix = database.Sql.Settings.TablePrefix }).ToList();
        // 20 z migracji 001 + META_Parser (004) + CAN_MappingReport, DICT_MappingCorrection (005)
        // + CAN_Row, META_SourceFileContent – CAN_Actuals, CAN_MappingReport, STG_RawRow (007)
        Assert.Equal(22, tables.Count);
        Assert.DoesNotContain(tables, t => t.EndsWith("CAN_Actuals") || t.EndsWith("CAN_MappingReport") || t.EndsWith("STG_RawRow"));
        Assert.Contains(database.Sql.Settings.TablePrefix + "META_PerformanceObjective", tables);
        Assert.Contains(database.Sql.Settings.TablePrefix + "DICT_ScheduleBudget", tables);

        // 004: parser ACTUALS w bazie (układ pliku z 2026-10); 007: pola mają sloty w CAN_Row
        var parsers = new SqlSourceConfigStore(database.Sql, new TestServices().Clock, new TestUser()).Parsers();
        var actuals = parsers.Single(p => p.Code == "ACTUALS");
        Assert.Equal(("ACTUALS", "Actuals", 26), (actuals.Code, actuals.Table, actuals.Fields.Count));
        Assert.Equal(ActualsLayout.Columns, actuals.Fields.Where(f => f.Column.Length > 0).Select(f => f.Column));
        Assert.Equal(["CostElementDescr", "PartnerCctr", "SourceObjectName"], actuals.Fields.Where(f => f.Column.Length == 0).Select(f => f.Field));
        Assert.Equal(["WbsElement", "FiscalYear", "Period"], actuals.Fields.Where(f => f.Required).Select(f => f.Field));
        Assert.Equal(new ParserField("CostElement", "Cost Element", FieldTypes.Text, 10, 10, Slot: "T03"), actuals.Fields.Single(f => f.Field == "CostElement"));
        Assert.Equal(["T01", "T02", "T03", "T04", "T05", "T06", "N01", "T07", "N02", "T08", "N03", "N04", "T09", "T10", "T11", "T12", "T13", "T14", "T15",
            "I01", "D01", "I02", "T16", "T17", "T18", "T19"], actuals.Fields.Select(f => f.Slot));   // kolejność pól, osobno dla każdego typu
        Assert.Equal("T 19/40 · N 4/20 · I 2/10 · D 1/10", actuals.SlotUsage);
        var definitionColumns = connection.Query<(string Name, bool Nullable)>(   // kolumny, sygnatura i wersja parsera definicji nieużywane
            "SELECT name, is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(@table)", new { table = database.Sql.Table("meta.SourceDefinition") }).ToList();
        Assert.All(["Columns", "Signature", "ParserVersion"], n => Assert.Contains(definitionColumns, c => c.Name == n && c.Nullable));

        // 005: parser MAPOWANIA (raport mapowań z Excela); 007: sloty T01–T11
        var mapping = parsers.Single(p => p.Code == "MAPOWANIA");
        Assert.Equal(("MappingReport", 11), (mapping.Table, mapping.Fields.Count));
        Assert.Equal(["src", "pspnr", "pspnr_sap", "pspnr_ces", "pspnr_parent", "project", "project_sap", "project_ces", "wbs", "wbs_sap", "wbs_ces"],
            mapping.Fields.Select(f => f.Column));
        Assert.Equal(["Src"], mapping.Fields.Where(f => f.Required).Select(f => f.Field));
        Assert.Equal(Enumerable.Range(1, 11).Select(i => $"T{i:00}"), mapping.Fields.Select(f => f.Slot));

        // 007: CAN_Row – stała tabela ze slotami i indeksem kolumnowym
        var canonical = database.Sql.Table("can.Row");
        var slots = connection.Query<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@canonical) ORDER BY column_id", new { canonical }).ToList();
        Assert.Equal(["FileId", "RowNumber", "ParserId", "ParserVersion", .. CanonicalSlots.All], slots);
        Assert.Equal("CLUSTERED COLUMNSTORE", connection.ExecuteScalar<string>("SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID(@canonical) AND index_id = 1", new { canonical }));
    }

    [SqlFact]
    public void Server_info_shows_sql_server_version_and_compatibility_level()
    {
        using var database = new TestDatabase();
        Assert.Matches(@"^SQL Server 20\d\d \(1\d\.[\d.]+\) · .+ · poziom zgodności bazy 1\d0$", database.Sql.ServerInfo());   // Diagnostyka
    }

    [SqlFact]
    public void Migration_started_by_two_people_at_once_runs_the_script_once()
    {
        using var database = new TestDatabase();   // bez danych startowych – 002 do wykonania
        var results = new IReadOnlyList<string>[2];

        Parallel.For(0, 2, i => results[i] = SqlMigrations.Apply(database.Sql));   // blokada sp_getapplock

        Assert.Equal(["002_dane_startowe.sql"], results.SelectMany(r => r));
        using var connection = database.Sql.Open();
        Assert.Equal(2, connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {database.Sql.Table("meta.SourceDefinition")}"));
    }

    [SqlFact]
    public void Migration_003_removes_files_stored_despite_failed_validation()
    {
        using var database = new TestDatabase();
        var clock = new TestServices().Clock;
        var store = new SqlImportStore(database.Sql);
        var batch = store.BeginBatch(clock.Now, @"PZL\test", "PC-1", "0.11.0");
        var content = Path.Combine(Path.GetTempPath(), $"pzl-ev-{Guid.NewGuid():N}.csv");
        File.WriteAllText(content, "A\n1\n");
        long Register(char hash, string name, string canonicalStatus)
        {
            var sha = new string(hash, 64);
            var id = store.StoreFile(new SourceFileRow(0, sha, "RABIT", name, "ACTUALS_PAF", 10, clock.Now, "csv", null, "utf-8", ";",
                ["A"], "sygnatura", 1, batch, clock.Now, @"PZL\test", canonicalStatus, 0, null), content, null)!.FileId;
            store.RecordSeen(new SourceFileSeenRow(0, batch, "RABIT", name, 10, clock.Now, sha, FileDecisions.Imported, "ACTUALS_PAF", 1, "zapis wersji 0.11"));
            return id;
        }
        var layout = Register('a', "ACTUALS_PAF2_B6_AC1.csv", "brak – sygnatura kolumn niezgodna z definicją");
        var values = Register('b', "ACTUALS_PAF2_B6_AC2.csv", "brak – 3 błędów wartości");
        var rawOnly = Register('c', "FORECAST_PAF.csv", "brak – źródło bez parsera (tylko wiersze surowe)");
        File.Delete(content);
        Assert.NotNull(store.LastSettled("RABIT", "ACTUALS_PAF2_B6_AC1.csv"));
        using (var connection = database.Sql.Open())   // baza sprzed migracji 003
            connection.Execute($"DELETE FROM {database.Sql.Table("meta.SchemaVersion")} WHERE Version = 3");

        Assert.Equal(["003_usuniecie_plikow_niezgodnych.sql"], SqlMigrations.Apply(database.Sql, presets: false));

        Assert.Null(store.File(layout));
        Assert.Null(store.File(values));
        Assert.Empty(store.RawRows(layout));
        Assert.NotNull(store.File(rawOnly));
        Assert.Single(store.RawRows(rawOnly));
        Assert.Null(store.LastSettled("RABIT", "ACTUALS_PAF2_B6_AC1.csv"));    // kolejny import pobierze plik ponownie
        Assert.NotNull(store.LastSettled("RABIT", "FORECAST_PAF.csv"));
        Assert.Equal(3, store.Seen(batch).Count);                              // historia decyzji zostaje
        Assert.Contains(new SqlJournal(database.Sql, clock, new TestUser()).Recent(3), e => e.Message ==
            "Migracja 003 – usunięte pliki niezgodne z definicją źródła: 2 (wiersze surowe: 2); kolejny import pobierze je ponownie");
    }
}
