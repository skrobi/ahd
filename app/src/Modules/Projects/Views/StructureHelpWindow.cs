using System.Windows;
using System.Windows.Controls;
using PzlEv.Modules.Projects.Services;

namespace PzlEv.Modules.Projects.Views;

/// <summary>
/// Okno „Struktura – skróty i opis kolumn” (przycisk „?” nad tabelą struktury): obsługa tabeli jak w Excelu i opis kolumn
/// w grupach (StructureColumns – ten sam opis co podpowiedź nagłówka). Bez modelu – treść z opisu kolumn.
/// </summary>
public sealed class StructureHelpWindow : Window
{
    public const string Shortcuts =
        "Pisanie zastępuje komórkę, Enter / Tab – dalej (zapis wiersza od razu), Esc – cofnij wiersz, Ctrl+C / Ctrl+V – kopiuj / wklej blok " +
        "(jedna komórka wypełnia zaznaczenie; komórki, których nie można zmienić, są pomijane), Delete – wyczyść (bez WP), Ctrl+D – wypełnij w dół, " +
        "Ctrl+Z – cofnij wklejenie / Delete / Ctrl+D, Alt+→ / Alt+← – rozwiń / zwiń wiersz, CAM i Cost Category – wpisz fragment, lista się zawęża. " +
        "Podświetlenie: czerwone – błąd, żółte – ostrzeżenie; wiersz żółty – niezapisany. Grupy kolumn: „−” nad pierwszą kolumną grupy zwija ją " +
        "do kolumny „+ Grupa”; kolumny kursywą – z danych (tylko do odczytu).";

    public StructureHelpWindow()
    {
        Title = "Struktura – skróty i opis kolumn";
        Width = 760;
        Height = 640;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Surface");

        var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        panel.Children.Add(Heading("Obsługa tabeli (jak w Excelu)", top: 0));
        panel.Children.Add(Text(Shortcuts, indent: 0));
        foreach (var group in StructureColumns.Groups)
        {
            panel.Children.Add(Heading($"{group.Name}{(group.Collapsed ? " (domyślnie zwinięta)" : "")}", top: 14));
            panel.Children.Add(Text(group.Description, indent: 0, muted: true));
            foreach (var column in StructureColumns.All.Where(c => c.Group == group.Name))
                panel.Children.Add(Text($"• {column.Header}: {column.Description}{(column.FromView ? " [tylko do odczytu]" : "")}", indent: 12));
        }
        var close = new Button { Content = "Zamknij", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 20, 14) };
        close.SetResourceReference(StyleProperty, "Btn");
        close.Click += (_, _) => Close();
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    private static TextBlock Heading(string text, double top) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, top, 0, 4), TextWrapping = TextWrapping.Wrap };

    private static TextBlock Text(string text, double indent, bool muted = false)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(indent, 0, 0, 3), TextWrapping = TextWrapping.Wrap };
        if (muted)
            block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }
}
