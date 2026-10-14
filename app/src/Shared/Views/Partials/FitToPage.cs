using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PzlEv.Shared.Views.Partials;

/// <summary>
/// Tabela do dołu okna: wysokość elementu (np. DataGrid) liczona od jego górnej krawędzi do dołu strony (ScrollViewer
/// powłoki, bez dolnego marginesu strony) – przy zmianie rozmiaru okna, zwinięciu menu, pokazaniu elementu i zmianie
/// wysokości elementów nad nim. Strona się nie przewija, wiersze przewija sama tabela; najmniej MinHeight.
/// </summary>
public static class FitToPage
{
    /// <summary>Podpina dopasowanie wysokości element do dołu strony, na której leży owner.</summary>
    public static void Attach(FrameworkElement owner, FrameworkElement element)
    {
        ScrollViewer? host = null;
        void Fit()
        {
            if (host is null || !element.IsVisible)
                return;
            var top = element.TranslatePoint(new Point(0, 0), host).Y + host.VerticalOffset;
            var height = Math.Max(element.MinHeight, Math.Floor(host.ViewportHeight - top - host.Padding.Bottom - 4));
            if (Math.Abs(element.Height - height) > 1 || double.IsNaN(element.Height))
                element.Height = height;
        }
        void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => Fit();

        void OnLoaded()
        {
            host ??= FindAncestor<ScrollViewer>(owner);
            if (host is not null)
            {
                host.SizeChanged -= OnHostSizeChanged;
                host.SizeChanged += OnHostSizeChanged;
            }
            owner.Dispatcher.BeginInvoke(Fit, DispatcherPriority.Loaded);
        }

        owner.Loaded += (_, _) => OnLoaded();
        if (owner.IsLoaded)
            OnLoaded();
        element.IsVisibleChanged += (_, _) => owner.Dispatcher.BeginInvoke(Fit, DispatcherPriority.Loaded);
        owner.LayoutUpdated += (_, _) => Fit();   // zmiana wysokości elementów nad tabelą
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found)
                return found;
        return null;
    }
}
