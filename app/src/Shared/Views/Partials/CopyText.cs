using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PzlEv.Shared.Views.Partials;

/// <summary>
/// Kopiowanie treści komunikatu (TextBlock nie pozwala zaznaczyć tekstu): partials:CopyText.Enabled="True" dodaje menu
/// „Kopiuj” pod prawym przyciskiem myszy; kliknięty komunikat kopiuje też Ctrl+C. Kopiowany jest cały tekst komunikatu.
/// </summary>
public static class CopyText
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(CopyText), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text || e.NewValue is not true)
            return;
        var copy = new MenuItem { Header = "Kopiuj", InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => Copy(text);
        text.ContextMenu = new ContextMenu { Items = { copy } };
        // Ctrl+C po kliknięciu komunikatu (TextBlock dostaje fokus klawiatury tylko jako Focusable).
        text.Focusable = true;
        text.MouseLeftButtonDown += (_, _) => text.Focus();
        text.KeyDown += (_, args) =>
        {
            if (args.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Copy(text);
                args.Handled = true;
            }
        };
    }

    private static void Copy(TextBlock text)
    {
        if (string.IsNullOrEmpty(text.Text))
            return;
        try
        {
            Clipboard.SetText(text.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Schowek zajęty przez inny program – kopiowanie pominięte.
        }
    }
}
