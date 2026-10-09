using System.ComponentModel;
using System.Windows;

namespace PzlEv.Shell;

/// <summary>
/// Główne okno. Rozmiar z ShellWindow.xaml (1360 × 880, minimum 1000 × 600) jest dopasowywany do obszaru roboczego
/// ekranu: na tablecie albo przy dużym powiększeniu ekranu (np. 150–200%) okno wyśrodkowane wychodziło górą poza ekran
/// i pasek tytułu z przyciskami zamknij / minimalizuj / maksymalizuj był niewidoczny. Okno, które się nie mieści, jest
/// maksymalizowane; dopasowanie powtarza się po zmianie obszaru roboczego (obrót tabletu, zmiana skalowania, klawiatura
/// ekranowa, podłączenie monitora).
/// </summary>
public partial class ShellWindow : Window
{
    private readonly Size _preferred;
    private readonly Size _minimum;

    public ShellWindow()
    {
        InitializeComponent();
        _preferred = new Size(Width, Height);
        _minimum = new Size(MinWidth, MinHeight);
        FitToWorkArea();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            Dispatcher.BeginInvoke(FitToWorkArea);
    }

    private void FitToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(_minimum.Width, work.Width);
        MinHeight = Math.Min(_minimum.Height, work.Height);
        if (WindowState != WindowState.Normal)
            return;
        if (_preferred.Width > work.Width || _preferred.Height > work.Height)
        {
            // Nie mieści się – pełny obszar roboczy (pasek tytułu zawsze widoczny), rozmiar po przywróceniu – w granicach ekranu.
            Width = Math.Min(_preferred.Width, work.Width);
            Height = Math.Min(_preferred.Height, work.Height);
            Left = work.Left;
            Top = work.Top;
            WindowState = WindowState.Maximized;
            return;
        }
        Width = Math.Min(Width, work.Width);
        Height = Math.Min(Height, work.Height);
        if (IsLoaded)
        {
            // Okno przesunięte poza ekran (np. po obrocie) – z powrotem w obszar roboczy, pasek tytułu widoczny.
            Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        }
    }
}
