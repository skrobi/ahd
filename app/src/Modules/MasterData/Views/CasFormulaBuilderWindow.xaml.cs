using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PzlEv.Modules.MasterData.ViewModels;

namespace PzlEv.Modules.MasterData.Views;

/// <summary>Edytor formuły z klocków: kliknięcie i przeciąganie klocków (paleta → formuła, przestawianie w formule), klawiatura.</summary>
public partial class CasFormulaBuilderWindow : Window
{
    private readonly CasFormulaBuilderViewModel _model;
    private Point? _pressed;
    private object? _pressedItem;
    private bool _dragged;

    public CasFormulaBuilderWindow(CasFormulaBuilderViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
    }

    public FormulaEdit? Result { get; private set; }

    /// <summary>Okno edytora; null – anulowano.</summary>
    public static FormulaEdit? Edit(CasFormulaBuilderViewModel model)
    {
        var window = new CasFormulaBuilderWindow(model) { Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) };
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!_model.IsValid)
            return;
        Result = new FormulaEdit(_model.Tokens, Remove: false);
        DialogResult = true;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        Result = new FormulaEdit(null, Remove: true);
        DialogResult = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox)
            return;
        Action? action = e.Key switch
        {
            Key.Back => _model.Backspace,
            Key.Delete => _model.Delete,
            Key.Left => _model.Left,
            Key.Right => _model.Right,
            Key.Home => _model.Home,
            Key.End => _model.End,
            _ => null,
        };
        if (action is null)
            return;
        action();
        e.Handled = true;
    }

    // ---------- paleta: kliknięcie albo przeciągnięcie ----------

    private void OnPalettePress(object sender, MouseButtonEventArgs e) => Press(sender, e);

    private void OnPaletteMove(object sender, MouseEventArgs e) => DragIfMoved(e);

    private void OnPaletteClick(object sender, MouseButtonEventArgs e)
    {
        if (!_dragged && (sender as FrameworkElement)?.DataContext is PaletteBlock block)
            _model.Insert(block, null);
        Release();
    }

    // ---------- klocki formuły: kliknięcie ustawia kursor za klockiem, przeciągnięcie przestawia ----------

    private void OnBlockPress(object sender, MouseButtonEventArgs e) => Press(sender, e);

    private void OnBlockMove(object sender, MouseEventArgs e) => DragIfMoved(e);

    private void OnBlockClick(object sender, MouseButtonEventArgs e)
    {
        if (!_dragged && (sender as FrameworkElement)?.DataContext is FormulaBlock block)
        {
            _model.SetCursor(block.Index + 1);
            e.Handled = true;
        }
        Release();
    }

    /// <summary>Kliknięcie w wolne miejsce formuły – kursor na końcu.</summary>
    private void OnAreaClick(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled)
            _model.End();
    }

    private void Press(object sender, MouseButtonEventArgs e)
    {
        _pressed = e.GetPosition(this);
        _pressedItem = (sender as FrameworkElement)?.DataContext;
        _dragged = false;
    }

    private void Release()
    {
        _pressed = null;
        _pressedItem = null;
    }

    private void DragIfMoved(MouseEventArgs e)
    {
        if (_pressed is not { } start || _pressedItem is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        if (_pressedItem is PaletteBlock { IsEnabled: false })
            return;
        _dragged = true;
        var item = _pressedItem;
        Release();
        DragDrop.DoDragDrop(this, new DataObject(typeof(object), item), DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var item = e.Data.GetData(typeof(object));
        e.Effects = item is PaletteBlock ? DragDropEffects.Copy : item is FormulaBlock { IsBlock: true } ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var at = DropIndex(e);
        switch (e.Data.GetData(typeof(object)))
        {
            case PaletteBlock block:
                _model.Insert(block, at);
                break;
            case FormulaBlock { IsBlock: true } block:
                _model.Move(block.Index, at);
                break;
        }
        e.Handled = true;
    }

    /// <summary>Pozycja wstawienia: klocek pod kursorem myszy – przed nim (lewa połowa) albo za nim; poza klockami – koniec.</summary>
    private int DropIndex(DragEventArgs e)
    {
        var point = e.GetPosition(FormulaArea);
        for (var element = FormulaArea.InputHitTest(point) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { Tag: FormulaBlock block } panel)
            {
                if (block.IsEnd)
                    return block.Index;
                var x = e.GetPosition(panel).X;
                return x < panel.ActualWidth / 2 ? block.Index : block.Index + 1;
            }
            if (ReferenceEquals(element, FormulaArea))
                break;
        }
        return _model.Blocks.Count - 1;   // znacznik końca
    }
}
