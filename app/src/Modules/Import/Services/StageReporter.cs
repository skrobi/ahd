using System.Diagnostics;
using System.Globalization;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Etap importu pliku na ekranie: nazwa etapu (np. „1/4 pobieranie na dysk”), szczegół (MB, wiersze) i czas trwania
/// etapu. Szczegół odświeżany najwyżej co 0,25 s, a co sekundę także bez nowych danych (np. gdy WebDAV pobiera cały
/// plik przed otwarciem) – widać, że import trwa i na którym etapie. Czasy etapów – do logu (Summary).
/// </summary>
public sealed class StageReporter : IDisposable
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    private readonly Action<string> _report;
    private readonly Timer _timer;
    private readonly Stopwatch _stage = new();
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly List<string> _timings = [];
    private readonly object _gate = new();
    private string _name = "";
    private string _detail = "";
    private long _lastDetail = -1000;
    private bool _disposed;

    public StageReporter(Action<string> report)
    {
        _report = report;
        _timer = new Timer(_ => Report(), null, 1000, 1000);
    }

    /// <summary>Nowy etap (poprzedni trafia do czasów etapów).</summary>
    public void Start(string name)
    {
        lock (_gate)
        {
            Close();
            _name = name;
            _detail = "";
            _lastDetail = -1000;
            _stage.Restart();
        }
        Report();
    }

    /// <summary>Postęp etapu (np. „35,2 z 86,0 MB”); na ekran najwyżej co 0,25 s, chyba że final (stan końcowy etapu).</summary>
    public void Detail(string detail) => Detail(detail, final: false);

    public void Detail(string detail, bool final)
    {
        lock (_gate)
        {
            _detail = detail;
            if (!final && _stage.ElapsedMilliseconds - _lastDetail < 250)
                return;
            _lastDetail = _stage.ElapsedMilliseconds;
        }
        Report();
    }

    /// <summary>Czasy etapów i czas całkowity, np. „1/4 pobieranie na dysk 12,3 s; … razem 40,1 s”.</summary>
    public string Summary
    {
        get
        {
            lock (_gate)
                return string.Join("; ", _timings.Append($"{_name} {Seconds(_stage.Elapsed)}").Append($"razem {Seconds(_total.Elapsed)}"));
        }
    }

    public static string Seconds(TimeSpan elapsed) => elapsed.TotalSeconds.ToString(elapsed.TotalSeconds < 10 ? "0.0" : "0", Polish) + " s";

    public static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("#,0.0", Polish);

    public static string Count(long value) => value.ToString("#,0", Polish);

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _timer.Dispose();
    }

    private void Close()
    {
        if (_name.Length > 0)
            _timings.Add($"{_name} {Seconds(_stage.Elapsed)}");
    }

    private void Report()
    {
        lock (_gate)
        {
            if (_disposed || _name.Length == 0)
                return;
            _report($"{_name}{(_detail.Length > 0 ? " – " + _detail : "")} · {Seconds(_stage.Elapsed)}");
        }
    }
}
