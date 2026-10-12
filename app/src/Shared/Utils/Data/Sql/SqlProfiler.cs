using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Profilowanie zapytań SQL do logu (wszystkie bazy: środowiska, PZLPROD, PZLHRPROD): zdarzenia diagnostyczne
/// Microsoft.Data.SqlClient (WriteCommandBefore / After / Error) – czas wykonania polecenia od wysłania do odpowiedzi
/// (bez czytania wierszy wyniku), baza, tekst polecenia (skrócony, bez wartości parametrów). Do logu trafiają polecenia
/// dłuższe niż próg; błędy – zawsze. Włączane raz przy starcie aplikacji.
/// </summary>
/// <summary>Wykonane polecenie SQL: czas, baza, tekst (skrócony), błąd (null – bez błędu).</summary>
public sealed record SqlProfile(TimeSpan Elapsed, string? Database, string Sql, string? Error);

public sealed partial class SqlProfiler : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private const string ListenerName = "SqlClientDiagnosticListener";
    private const int MaxText = 600;

    private static readonly ILogger Logger = Log.ForContext("Module", "sql");
    private static SqlProfiler? _instance;

    private readonly ConcurrentDictionary<Guid, long> _started = new();
    private readonly TimeSpan _threshold;
    private readonly Action<SqlProfile> _report;
    private readonly List<IDisposable> _subscriptions = [];

    private SqlProfiler(TimeSpan threshold, Action<SqlProfile> report)
    {
        _threshold = threshold;
        _report = report;
    }

    /// <summary>Włącza profilowanie do logu (wywołanie ponowne – bez zmian).</summary>
    public static void Start(TimeSpan threshold)
    {
        if (_instance is not null)
            return;
        _instance = Watch(threshold, Write);
        Logger.Information("Profilowanie SQL włączone: polecenia dłuższe niż {Threshold} ms", (int)threshold.TotalMilliseconds);
    }

    /// <summary>Obserwacja poleceń SQL: report dostaje polecenia dłuższe niż próg i błędy; Dispose – koniec obserwacji.</summary>
    public static SqlProfiler Watch(TimeSpan threshold, Action<SqlProfile> report)
    {
        var profiler = new SqlProfiler(threshold, report);
        profiler._subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(profiler));
        return profiler;
    }

    public void Dispose()
    {
        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
        }
    }

    private static void Write(SqlProfile profile)
    {
        if (profile.Error is not null)
            Logger.Warning("SQL błąd po {Elapsed} ms [{Database}]: {Sql} – {Error}", (long)profile.Elapsed.TotalMilliseconds, profile.Database, profile.Sql, profile.Error);
        else
            Logger.Information("SQL {Elapsed} ms [{Database}]: {Sql}", (long)profile.Elapsed.TotalMilliseconds, profile.Database, profile.Sql);
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == ListenerName)
            lock (_subscriptions)
                _subscriptions.Add(listener.Subscribe(this));
    }

    public void OnNext(KeyValuePair<string, object?> e)
    {
        try
        {
            if (e.Value is not { } payload)
                return;
            var id = Get<Guid>(payload, "OperationId");
            if (e.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal))
            {
                _started[id] = Get<long>(payload, "Timestamp");
                return;
            }
            var after = e.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal);
            var error = e.Key.EndsWith("WriteCommandError", StringComparison.Ordinal);
            if (!(after || error) || !_started.TryRemove(id, out var start))
                return;
            var elapsed = Stopwatch.GetElapsedTime(start, Get<long>(payload, "Timestamp"));
            var command = Get<DbCommand>(payload, "Command");
            if (error || elapsed >= _threshold)
                _report(new SqlProfile(elapsed, command?.Connection?.Database, Text(command), error ? Get<Exception>(payload, "Exception")?.Message ?? "?" : null));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // profilowanie nie może przerwać zapytania
        }
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    /// <summary>Tekst polecenia w jednej linii, skrócony; procedura – nazwa.</summary>
    private static string Text(DbCommand? command)
    {
        if (command is null)
            return "?";
        var text = Spaces().Replace(command.CommandText ?? "", " ").Trim();
        if (command.CommandType == System.Data.CommandType.StoredProcedure)
            text = $"EXEC {text}";
        return text.Length > MaxText ? text[..MaxText] + "…" : text;
    }

    private static T? Get<T>(object payload, string name) =>
        payload.GetType().GetProperty(name)?.GetValue(payload) is T value ? value : default;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
