using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PzlEv.Modules.Projects.Services;
using PzlEv.Modules.Projects.ViewModels;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Projects.Views;

/// <summary>
/// Kolumna tabeli struktury z wyborem z listy: CAM – pokazuje imię i nazwisko, edycja – lista osób z wyszukiwaniem po
/// fragmencie USRID albo imienia i nazwiska, zapisywany USRID; Cost Category – lista kategorii projektu („Kategorie
/// WBS”). Edycja – LookupComboBox (ta sama co w tabeli słownika). Pisanie w zaznaczonej komórce zaczyna wyszukiwanie
/// od wpisanego znaku.
/// </summary>
public sealed class LookupColumn : DataGridColumn
{
    private readonly string _key;

    /// <param name="key">Kolumna StructureEdits (np. StructureEdits.Cam).</param>
    public LookupColumn(string key, string header, double width)
    {
        _key = key;
        Header = header;
        Width = width;
        SortMemberPath = key;
        ClipboardContentBinding = new Binding($"[{key}]");
    }

    /// <summary>Wartości do wyboru (wartość → opis) – ustawia widok po ustawieniu modelu.</summary>
    public IReadOnlyList<LookupOption> Options { get; set; } = [];

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var text = new TextBlock { Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        text.SetBinding(TextBlock.TextProperty, new Binding($"[{_key}]") { Converter = new NameConverter(this) });
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"[{_key}]"));
        return text;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) => LookupComboBox.Create(Options);

    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
    {
        if (editingElement is not ComboBox { DataContext: StructureRowViewModel row } combo
            || LookupComboBox.Prepare(combo, row[_key], Options) is not { } box)
            return "";
        box.Focus();
        if (editingEventArgs is TextCompositionEventArgs typed)
        {
            box.Text = typed.Text;
            box.CaretIndex = box.Text.Length;
        }
        else
            box.SelectAll();
        return "";
    }

    protected override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is ComboBox { DataContext: StructureRowViewModel row } combo)
            row[_key] = LookupComboBox.ValueOf(combo, Options);
        return true;
    }

    private sealed class NameConverter(LookupColumn column) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string text ? column.Options.FirstOrDefault(p => string.Equals(p.Value, text, StringComparison.OrdinalIgnoreCase))?.Label ?? text : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
