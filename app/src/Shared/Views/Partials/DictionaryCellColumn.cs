using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Dictionaries;

namespace PzlEv.Shared.Views.Partials;

/// <summary>
/// Kolumna tabeli słownika (DictionaryGrid) według typu kolumny: tekst, liczba (do prawej), lista wyboru (Choice –
/// lista z możliwością wpisania), kolumna powiązana z innym słownikiem (Lookup – lista z wyszukiwaniem po wartości
/// i opisie: zapisuje wartość, np. USRID, pokazuje opis, np. imię i nazwisko). Komórka z problemem jest podświetlona
/// (ERROR – czerwony, WARNING – żółty), opis w podpowiedzi. Edycja jak w Excelu: pisanie zastępuje zawartość, Esc cofa,
/// wartość trafia do wiersza dopiero przy zatwierdzeniu komórki.
/// </summary>
public sealed class DictionaryCellColumn : DataGridColumn
{
    private static readonly IValueConverter LevelBackground = new PzlEv.Shared.Utils.Ui.Converters.LevelBrushConverter();

    public DictionaryCellColumn(int index, DictColumn column, IReadOnlyList<LookupOption>? options)
    {
        Index = index;
        Column = column;
        Options = options;
        Header = column.Key ? $"{column.Name} *" : column.Name;
        SortMemberPath = $"Cells[{index}].SortKey";
        ClipboardContentBinding = new Binding($"Cells[{index}].Value");
        MinWidth = 80;
    }

    public int Index { get; }

    public DictColumn Column { get; }

    public IReadOnlyList<LookupOption>? Options { get; }

    private string Path(string property) => $"Cells[{Index}].{property}";

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var text = new TextBlock { Padding = new Thickness(4, 1, 4, 1), VerticalAlignment = VerticalAlignment.Center };
        text.SetBinding(TextBlock.TextProperty, new Binding(Path(nameof(DictCellViewModel.Display))));
        if (Column.Type is ColumnType.Decimal or ColumnType.Integer)
            text.TextAlignment = TextAlignment.Right;
        var border = new Border { Child = text };
        border.SetBinding(Border.BackgroundProperty, new Binding(Path(nameof(DictCellViewModel.Level))) { Converter = LevelBackground, ConverterParameter = "cell" });
        border.SetBinding(FrameworkElement.ToolTipProperty, new Binding(Path(nameof(DictCellViewModel.Problem))));
        return border;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        if (Options is { } options)
            return LookupComboBox.Create(options);
        if (Column.Choices is { } choices)
        {
            var combo = new ComboBox { IsEditable = true, ItemsSource = choices, BorderThickness = new Thickness(0) };
            combo.SetBinding(ComboBox.TextProperty, new Binding(Path(nameof(DictCellViewModel.Value))) { UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
            return combo;
        }
        var box = new TextBox { BorderThickness = new Thickness(0), Padding = new Thickness(2, 0, 2, 0) };
        if (Column.Type is ColumnType.Decimal or ColumnType.Integer)
            box.TextAlignment = TextAlignment.Right;
        box.SetBinding(TextBox.TextProperty, new Binding(Path(nameof(DictCellViewModel.Value))) { UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        return box;
    }

    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
    {
        var box = editingElement as TextBox;
        if (editingElement is ComboBox combo)
        {
            combo.ApplyTemplate();
            box = Options is not null && combo.DataContext is DictRowViewModel row
                ? LookupComboBox.Prepare(combo, row[Index], Options)
                : combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;
        }
        if (box is null)
            return "";
        box.Focus();
        if (editingEventArgs is TextCompositionEventArgs typed)
        {
            // Jak w Excelu: pisanie w zaznaczonej komórce zastępuje jej zawartość.
            box.Text = typed.Text;
            box.CaretIndex = box.Text.Length;
        }
        else
            box.SelectAll();
        return "";
    }

    protected override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (Options is { } options && editingElement is ComboBox { DataContext: DictRowViewModel row } combo)
        {
            var value = LookupComboBox.ValueOf(combo, options);
            row[Index] = value;
            return true;
        }
        var property = editingElement is ComboBox ? ComboBox.TextProperty : TextBox.TextProperty;
        BindingOperations.GetBindingExpression(editingElement, property)?.UpdateSource();
        return true;
    }
}
