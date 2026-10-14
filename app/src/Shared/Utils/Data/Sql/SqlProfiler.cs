using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Wykonane polecenie SQL: czas, baza, tekst (skrócony), błąd (null – bez błędu), krok operacji (StepTimer; null – brak).</summary>
public sealed record SqlProfile(TimeSpan Elapsed, string? Database, string Sql, string? Error, string? Operation = null);

/// <summary>
/// Profilowanie zapytań SQL (wszystkie bazy: środowiska, PZLPROD, PZLHRPROD): zdarzenia diagnostyczne
/// Microsoft.Data.SqlClient (WriteCommandBefore / After / Error) – czas wykonania polecenia od wysłania do odpowiedzi
/// (bez czytania wierszy wyniku), baza, tekst polecenia (skrócony, bez wartości parametrów), krok operacji (StepTimer).
/// Log główny – polecenia dłuższe niż próg i błędy; log SQL (logs\sql-RRRRMMDD.log) – każde polecenie; przy zamknięciu
/// aplikacji – podsumowanie sesji (SqlStatistics: polecenia o największym łącznym czasie, błędy). Włączane raz przy starcie.
/// </summary>

public sealed partial class SqlProfiler : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private const string ListenerName = "SqlClientDiagnosticListener";
    private const int MaxText = 600;

    private static readonly ILogger Logger = Log.ForContext("Module", "sql");
    private static readonly AsyncLocal<string?> CurrentOperation = new();
    private static SqlProfiler? _instance;
    private static ILogger? _all;
    private static TimeSpan _slow;
    private static readonly SqlStatistics Statistics = new();

    private readonly ConcurrentDictionary<Guid, (long Start, string? Operation)> _started = new();
    private readonly TimeSpan _threshold;
    private readonly Action<SqlProfile> _report;
    private readonly List<IDisposable> _subscriptions = [];

    private SqlProfiler(TimeSpan threshold, Action<SqlProfile> report)
    {
        _threshold = threshold;
        _report = report;
    }

    /// <summary>Krok operacji bieżącego wątku / zadania (StepTimer.Time) – dopisywany do poleceń SQL w logu.</summary>
    public static string? Operation
    {
        get => CurrentOperation.Value;
        set => CurrentOperation.Value = value;
    }

    /// <summary>
    /// Włącza profilowanie (wywołanie ponowne – bez zmian): log główny – polecenia dłuższe niż slow i błędy; all – każde
    /// polecenie (log SQL; null – bez niego).
    /// </summary>
    public static void Start(TimeSpan slow, ILogger? all = null)
    {
        if (_instance is not null)
            return;
        _slow = slow;
        _all = all;
        _instance = Watch(TimeSpan.Zero, Write);
        Logger.Information("Profilowanie SQL włączone: do logu polecenia dłuższe niż {Threshold} ms i błędy{All}", (int)slow.TotalMilliseconds,
            all is null ? "" : "; każde polecenie – log SQL (sql-RRRRMMDD.log)");
    }

    /// <summary>Kończy profilowanie i zapisuje podsumowanie sesji do logu głównego i logu SQL.</summary>
    public static void Stop()
    {
        if (_instance is null)
            return;
        _instance.Dispose();
        _instance = null;
        foreach (var line in Statistics.Summary(20))
        {
            Logger.Information("{Line}", line);
            _all?.Information("{Line}", line);
        }
        (_all as IDisposable)?.Dispose();   // log SQL – zapis na dysk przed zamknięciem
        _all = null;
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
        Statistics.Add(profile);
        var ms = (long)profile.Elapsed.TotalMilliseconds;
        var step = profile.Operation is null ? "" : $" ({profile.Operation})";
        if (profile.Error is not null)
        {
            Logger.Warning("SQL błąd po {Elapsed} ms [{Database}]{Step}: {Sql} – {Error}", ms, profile.Database, step, profile.Sql, profile.Error);
            _all?.Warning("BŁĄD {Elapsed,6} ms [{Database}]{Step}: {Sql} – {Error}", ms, profile.Database, step, profile.Sql, profile.Error);
            return;
        }
        if (profile.Elapsed >= _slow)
            Logger.Information("SQL {Elapsed} ms [{Database}]{Step}: {Sql}", ms, profile.Database, step, profile.Sql);
        _all?.Information("{Elapsed,6} ms [{Database}]{Step}: {Sql}", ms, profile.Database, step, profile.Sql);
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
                _started[id] = (Get<long>(payload, "Timestamp"), Operation);
                return;
            }
            var after = e.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal);
            var error = e.Key.EndsWith("WriteCommandError", StringComparison.Ordinal);
            if (!(after || error) || !_started.TryRemove(id, out var started))
                return;
            var elapsed = Stopwatch.GetElapsedTime(started.Start, Get<long>(payload, "Timestamp"));
            var command = Get<DbCommand>(payload, "Command");
            if (error || elapsed >= _threshold)
                _report(new SqlProfile(elapsed, command?.Connection?.Database, Text(command), error ? Get<Exception>(payload, "Exception")?.Message ?? "?" : null,
                    started.Operation));
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
