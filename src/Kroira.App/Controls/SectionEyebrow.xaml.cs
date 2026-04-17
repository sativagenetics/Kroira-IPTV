using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Controls;

public sealed partial class SectionEyebrow : UserControl
{
    public static readonly DependencyProperty EyebrowProperty =
        DependencyProperty.Register(nameof(Eyebrow), typeof(string), typeof(SectionEyebrow),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionEyebrow),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(SectionEyebrow),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public SectionEyebrow()
    {
        InitializeComponent();
        ApplyText();
    }

    public string Eyebrow
    {
        get => (string)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SectionEyebrow s)
        {
            s.ApplyText();
        }
    }

    private void ApplyText()
    {
        EyebrowText.Text = Eyebrow ?? string.Empty;
        EyebrowText.Visibility = string.IsNullOrWhiteSpace(Eyebrow) ? Visibility.Collapsed : Visibility.Visible;

        TitleText.Text = Title ?? string.Empty;

        CaptionText.Text = Caption ?? string.Empty;
        CaptionText.Visibility = string.IsNullOrWhiteSpace(Caption) ? Visibility.Collapsed : Visibility.Visible;
    }
}
