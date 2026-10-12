using System.Diagnostics;

namespace PzlEv.Shared.Utils.Data;

/// <summary>Czasy kroków operacji do logu (profilowanie): łączny czas i kroki od najdłuższego.</summary>
public sealed class StepTimer
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly List<(string Name, long Milliseconds)> _steps = [];

    public T Time<T>(string name, Func<T> work)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            return work();
        }
        finally
        {
            lock (_steps)
                _steps.Add((name, watch.ElapsedMilliseconds));
        }
    }

    /// <summary>„12345 ms – koszty ACWP 9000 ms, produkcja 2000 ms, …”.</summary>
    public string Summary
    {
        get
        {
            lock (_steps)
                return $"{_total.ElapsedMilliseconds} ms – " +
                       string.Join(", ", _steps.OrderByDescending(s => s.Milliseconds).Select(s => $"{s.Name} {s.Milliseconds} ms"));
        }
    }
}
