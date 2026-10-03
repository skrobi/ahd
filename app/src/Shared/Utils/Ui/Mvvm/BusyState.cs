using System.Windows.Input;
using System.Windows.Threading;

namespace PzlEv.Shared.Utils.Ui.Mvvm;

/// <summary>
/// Stan operacji w tle na ekranie: praca (baza, pliki) wykonuje się poza wątkiem okna, a ekran pokazuje „Trwa: …”
/// z licznikiem sekund (Shared/Views/Partials/BusyBar.xaml), więc kliknięcie ma widoczny skutek i okno nie zamarza.
/// Na czas operacji polecenia ekranu są nieaktywne (CanExecute sprawdza IsBusy). Wynik wraca do wątku okna.
/// </summary>
public sealed class BusyState : ObservableObject
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _started;
    private string _text = "";
    private int _running;

    public BusyState() => _timer.Tick += (_, _) => OnPropertyChanged(nameof(Display));

    public bool IsBusy => _running > 0;

    /// <summary>Opis z czasem trwania, np. „Trwa: Zapisywanie słownika… (12 s)”.</summary>
    public string Display
    {
        get
        {
            var seconds = (int)(DateTime.Now - _started).TotalSeconds;
            return seconds < 2 ? $"Trwa: {_text}" : $"Trwa: {_text} ({seconds} s)";
        }
    }

    /// <summary>Wykonuje pracę w tle; wynik wraca do wątku okna (kontynuacja po await).</summary>
    public async Task<T> Run<T>(string text, Func<T> work)
    {
        Start(text);
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            Stop();
        }
    }

    public Task Run(string text, Action work) => Run(text, () => { work(); return true; });

    private void Start(string text)
    {
        if (_running++ == 0)
        {
            _started = DateTime.Now;
            _timer.Start();
            Mouse.OverrideCursor = Cursors.AppStarting;   // okno działa, praca trwa w tle
        }
        _text = text;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Display));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Stop()
    {
        if (--_running == 0)
        {
            _timer.Stop();
            Mouse.OverrideCursor = null;
        }
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Display));
        CommandManager.InvalidateRequerySuggested();
    }
}
