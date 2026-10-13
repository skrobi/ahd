using System.Windows;
using System.Windows.Controls;

namespace PzlEv.Shared.Views.Partials;

public partial class PageHeader : UserControl
{
    public static readonly DependencyProperty EyebrowProperty =
        DependencyProperty.Register(nameof(Eyebrow), typeof(string), typeof(PageHeader), new PropertyMetadata(null, (d, _) => ((PageHeader)d).UpdateLines()));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(null, (d, _) => ((PageHeader)d).UpdateLines()));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHeader));

    public PageHeader()
    {
        InitializeComponent();
        UpdateLines();
    }

    /// <summary>Pusta linia kontekstu albo opisu nie zajmuje miejsca.</summary>
    private void UpdateLines()
    {
        if (EyebrowText is null)
            return;
        EyebrowText.Visibility = string.IsNullOrEmpty(Eyebrow) ? Visibility.Collapsed : Visibility.Visible;
        SubtitleText.Visibility = string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }

    public string? Eyebrow
    {
        get => (string?)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Przyciski akcji ekranu (prawa strona nagłówka).</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
