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
    private static readonly IValueConverter LevelBackground = new LevelBackgroundConverter();

    public DictionaryCellColumn(int index, DictColumn column, IReadOnlyList<LookupOption>? options)
    {
        Index = index;
        Column = column;
        Options = options;
        Header = column.Key ? $"{column.Name} *" : column.Name;
        SortMemberPath = $"Cells[{index}].Display";
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
        border.SetBinding(Border.BackgroundProperty, new Binding(Path(nameof(DictCellViewModel.Level))) { Converter = LevelBackground });
        border.SetBinding(FrameworkElement.ToolTipProperty, new Binding(Path(nameof(DictCellViewModel.Problem))));
        return border;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        if (Options is { } options)
            return LookupEditor(options, dataItem as DictRowViewModel);
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

    /// <summary>Lista z wyszukiwaniem: wpisany tekst zawęża listę (fragment wartości albo opisu).</summary>
    private static ComboBox LookupEditor(IReadOnlyList<LookupOption> options, DictRowViewModel? row)
    {
        var view = new ListCollectionView(options.ToList());
        var combo = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = false,
            ItemsSource = view,
            DisplayMemberPath = nameof(LookupOption.Text),
            BorderThickness = new Thickness(0),
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

    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
    {
        var box = editingElement as TextBox;
        if (editingElement is ComboBox combo)
        {
            combo.ApplyTemplate();
            box = combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;
            if (Options is not null && combo.DataContext is DictRowViewModel row)
            {
                var value = row[Index];
                var current = Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
                combo.SelectedItem = current;
                combo.Text = current?.Text ?? value ?? "";
            }
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
            row[Index] = combo.SelectedItem is LookupOption chosen && chosen.Text == combo.Text
                ? chosen.Value
                : DictionaryCells.Resolve(combo.Text, options);
            return true;
        }
        var property = editingElement is ComboBox ? ComboBox.TextProperty : TextBox.TextProperty;
        BindingOperations.GetBindingExpression(editingElement, property)?.UpdateSource();
        return true;
    }

    private sealed class LevelBackgroundConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
        {
            "crit" => Application.Current?.TryFindResource("CritSoft") as Brush ?? Brushes.MistyRose,
            "warn" => Application.Current?.TryFindResource("WarnSoft") as Brush ?? Brushes.LightYellow,
            _ => Brushes.Transparent,
        };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
