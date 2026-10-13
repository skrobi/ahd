using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using PzlEv.Modules.MasterData.ViewModels;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.MasterData.Views;

public partial class CasMatrixWindow : Window
{
    private readonly CasMatrixViewModel _model;

    public CasMatrixWindow(CasMatrixViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.EditFormula = CasFormulaBuilderWindow.Edit;
        model.Loaded += BuildColumns;
        Loaded += async (_, _) => await model.Load();
        Closing += (_, e) =>
        {
            if (model.HasChanges
                && MessageBox.Show(this, "Formuły mają niezapisane zmiany. Zamknąć bez zapisu?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }

    /// <summary>Okno macierzy formuł; true – zapisano (ekran Słowniki odświeża tabelę).</summary>
    public static bool Edit(DictionaryService service)
    {
        var model = new CasMatrixViewModel(service);
        new CasMatrixWindow(model) { Owner = Application.Current?.MainWindow }.ShowDialog();
        return model.Saved;
    }

    /// <summary>Kolumny: Grupa, Opis, pole na każdą pulę (nagłówek obrócony, nazwa ze słownika pul).</summary>
    private void BuildColumns()
    {
        Matrix.Columns.Clear();
        Matrix.Columns.Add(new DataGridTextColumn { Header = "Grupa", Binding = new Binding(nameof(CasMatrixRow.Name)), Width = 170 });
        Matrix.Columns.Add(new DataGridTextColumn
        {
            Header = "Opis", Binding = new Binding(nameof(CasMatrixRow.Description)), Width = 220,
            ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis) } },
        });
        for (var i = 0; i < _model.Pools.Count; i++)
        {
            var pool = _model.Pools[i];
            Matrix.Columns.Add(new DataGridTemplateColumn
            {
                Header = new TextBlock { Text = pool.Name, LayoutTransform = new RotateTransform(-90), ToolTip = $"{pool.Name} ({(pool.IsFccom ? "FCCOM" : "koszt")})" },
                CellTemplate = Cell(i),
                Width = 34,
            });
        }
    }

    /// <summary>
    /// Pole puli: przycisk z polem wyboru (zaznaczone – pula naliczana), kliknięcie otwiera edytor formuły; zmienione –
    /// pogrubiona ramka, problem (zapętlenie, błąd formuły) – czerwone tło; podpowiedź – formuła do czytania.
    /// </summary>
    private static DataTemplate Cell(int index)
    {
        var cell = $"Cells[{index}]";
        return (DataTemplate)XamlReader.Parse($$$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button Command="{Binding DataContext.Open, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding {{{cell}}}}"
                      ToolTip="{Binding {{{cell}}}.Tip}" Cursor="Hand" Background="Transparent" BorderThickness="0" Padding="0"
                      HorizontalAlignment="Center" VerticalAlignment="Center">
                <Border x:Name="Box" Width="18" Height="18" CornerRadius="3" BorderThickness="1.5" BorderBrush="#FF8A9BA4" Background="White">
                  <TextBlock x:Name="Mark" Text="✓" FontWeight="Bold" FontSize="13" HorizontalAlignment="Center" VerticalAlignment="Center"
                             Foreground="White" Visibility="Collapsed" />
                </Border>
              </Button>
              <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding {{{cell}}}.IsApplied}" Value="True">
                  <Setter TargetName="Box" Property="Background" Value="#FF0A6A76" />
                  <Setter TargetName="Box" Property="BorderBrush" Value="#FF0A6A76" />
                  <Setter TargetName="Mark" Property="Visibility" Value="Visible" />
                </DataTrigger>
                <DataTrigger Binding="{Binding {{{cell}}}.IsChanged}" Value="True">
                  <Setter TargetName="Box" Property="BorderThickness" Value="3" />
                  <Setter TargetName="Box" Property="BorderBrush" Value="#FF9A5C05" />
                </DataTrigger>
                <DataTrigger Binding="{Binding {{{cell}}}.HasProblem}" Value="True">
                  <Setter TargetName="Box" Property="Background" Value="#FFB0271F" />
                </DataTrigger>
              </DataTemplate.Triggers>
            </DataTemplate>
            """);
    }
}
