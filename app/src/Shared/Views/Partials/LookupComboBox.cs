using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Shared.Views.Partials;

/// <summary>
/// Lista z wyszukiwaniem do edycji komórki powiązanej z innym słownikiem (np. CAM – słownik Osoby): wpisany tekst
/// zawęża listę (fragment wartości albo opisu), zapisywana jest wartość (USRID), widoczny opis (imię i nazwisko).
/// Wspólna dla tabeli słownika (DictionaryCellColumn) i tabeli struktury projektu.
/// </summary>
public static class LookupComboBox
{
    public static ComboBox Create(IReadOnlyList<LookupOption> options)
    {
        var view = new ListCollectionView(options.ToList());
        var combo = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = false,
            ItemsSource = view,
            DisplayMemberPath = nameof(LookupOption.Text),
            BorderThickness = new System.Windows.Thickness(0),
            MaxDropDownHeight = 320,
        };
        combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (!combo.IsKeyboardFocusWithin || combo.SelectedItem is LookupOption chosen && chosen.Text == combo.Text)
                return;
            var search = combo.Text.Trim();
            view.Filter = o => ((LookupOption)o).Matches(search);
            combo.IsDropDownOpen = !view.IsEmpty;
        }));
        return combo;
    }

    /// <summary>Stan listy przed edycją: bieżąca wartość komórki zaznaczona, w polu jej tekst. Zwraca pole tekstowe listy.</summary>
    public static TextBox? Prepare(ComboBox combo, string? value, IReadOnlyList<LookupOption> options)
    {
        combo.ApplyTemplate();
        var current = options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
        combo.SelectedItem = current;
        combo.Text = current?.Text ?? value ?? "";
        return combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;
    }

    /// <summary>
    /// Wartość do zapisania po edycji: wybrana pozycja; wpisany USRID, opis albo tekst pozycji (DictionaryCells.Resolve);
    /// fragment, który zostawił na liście jedną pozycję, wybiera ją (jak autouzupełnianie w Excelu); inaczej tekst.
    /// </summary>
    public static string? ValueOf(ComboBox combo, IReadOnlyList<LookupOption> options)
    {
        var value = combo.SelectedItem is LookupOption chosen && chosen.Text == combo.Text
            ? chosen.Value
            : DictionaryCells.Resolve(combo.Text, options);
        if (value is not null && !options.Any(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase))
            && combo.ItemsSource is ListCollectionView { Count: 1 } view && view.GetItemAt(0) is LookupOption only)
            value = only.Value;
        return value;
    }
}
