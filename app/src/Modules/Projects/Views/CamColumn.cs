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
/// Kolumna CAM tabeli struktury: pokazuje imię i nazwisko, edycja – lista osób z wyszukiwaniem po fragmencie USRID
/// albo imienia i nazwiska (LookupComboBox, ta sama co w tabeli słownika); zapisywany USRID. Pisanie w zaznaczonej
/// komórce zaczyna wyszukiwanie od wpisanego znaku.
/// </summary>
public sealed class CamColumn : DataGridColumn
{
    private const string Key = StructureEdits.Cam;

    public CamColumn()
    {
        Header = "CAM";
        Width = 180;
        SortMemberPath = Key;
        ClipboardContentBinding = new Binding($"[{Key}]");
    }

    /// <summary>Lista osób (USRID → imię i nazwisko) – ustawia widok po ustawieniu modelu.</summary>
    public IReadOnlyList<LookupOption> Persons { get; set; } = [];

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var text = new TextBlock { Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        text.SetBinding(TextBlock.TextProperty, new Binding($"[{Key}]") { Converter = new NameConverter(this) });
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"[{Key}]"));
        return text;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) => LookupComboBox.Create(Persons);

    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
    {
        if (editingElement is not ComboBox { DataContext: StructureRowViewModel row } combo
            || LookupComboBox.Prepare(combo, row[Key], Persons) is not { } box)
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
            row[Key] = LookupComboBox.ValueOf(combo, Persons);
        return true;
    }

    private sealed class NameConverter(CamColumn column) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string usrid ? column.Persons.FirstOrDefault(p => string.Equals(p.Value, usrid, StringComparison.OrdinalIgnoreCase))?.Label ?? usrid : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
