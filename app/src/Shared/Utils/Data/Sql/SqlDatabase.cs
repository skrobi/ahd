using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Shared.Utils.Config;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Baza MS SQL środowiska: połączenie (konto AD użytkownika) i nazwy obiektów według konfiguracji –
/// nazwa logiczna „meta.ImportBatch” (docs/model-danych.md) → [Schema].[PrefixMETA_ImportBatch].
/// </summary>
public sealed class SqlDatabase
{
    public SqlDatabase(SqlSettings settings, string appVersion)
    {
        Settings = settings;
        ConnectionString = settings.ConnectionString is { } explicitConnection
            ? new SqlConnectionStringBuilder(explicitConnection) { ApplicationName = $"PZL-EV {appVersion}" }.ConnectionString
            : new SqlConnectionStringBuilder
            {
                DataSource = settings.Server,
                InitialCatalog = settings.Database,
                IntegratedSecurity = true,
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                TrustServerCertificate = settings.TrustServerCertificate,
                ApplicationName = $"PZL-EV {appVersion}",
            }.ConnectionString;
    }

    public SqlSettings Settings { get; }

    public string ConnectionString { get; }

    public string Describe => Settings.Describe;

    /// <summary>Nazwa obiektu z nazwy logicznej „warstwa.Nazwa”, np. „dict.FxRate” → [FINOP].[PZLEV_DICT_FxRate].</summary>
    public string Table(string logical)
    {
        var dot = logical.IndexOf('.');
        return dot < 0
            ? $"[{Settings.Schema}].[{Settings.TablePrefix}{logical}]"
            : $"[{Settings.Schema}].[{Settings.TablePrefix}{logical[..dot].ToUpperInvariant()}_{logical[(dot + 1)..]}]";
    }

    public SqlConnection Open()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Operacja w jednej transakcji – wszystko albo nic.</summary>
    public T InTransaction<T>(Func<SqlConnection, SqlTransaction, T> work)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var result = work(connection, transaction);
        transaction.Commit();
        return result;
    }

    /// <summary>Nowy identyfikator wiersza logicznego (sekwencja META_LogicalId).</summary>
    public long NextLogicalId(SqlConnection connection, SqlTransaction transaction) =>
        connection.ExecuteScalar<long>($"SELECT NEXT VALUE FOR {Table("meta.LogicalId")}", transaction: transaction);

    /// <summary>
    /// Serwer i baza (Diagnostyka): wersja SQL Server, edycja i poziom zgodności bazy – migracja 007 (indeks kolumnowy,
    /// OPENJSON, COMPRESS) wymaga SQL Server 2016+ i poziomu zgodności co najmniej 130.
    /// </summary>
    public string ServerInfo()
    {
        using var connection = Open();
        var info = connection.QuerySingle<(string Version, string Edition, int Compatibility)>(
            """
            SELECT CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(50)), CAST(SERVERPROPERTY('Edition') AS NVARCHAR(200)),
                   (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME())
            """);
        var major = int.TryParse(info.Version.Split('.')[0], out var m) ? m : 0;
        var name = major switch { 13 => "2016", 14 => "2017", 15 => "2019", 16 => "2022", 17 => "2025", _ => $"(wersja {major})" };
        var warning = info.Compatibility < 130 ? " – za niski dla migracji 007 (wymagane co najmniej 130)" : "";
        return $"SQL Server {name} ({info.Version}) · {info.Edition} · poziom zgodności bazy {info.Compatibility}{warning}";
    }

    /// <summary>Naruszenie unikalności (2601, 2627) – zapis w międzyczasie przez inną osobę.</summary>
    public static bool IsDuplicateKey(SqlException ex) => ex.Number is 2601 or 2627;
}
