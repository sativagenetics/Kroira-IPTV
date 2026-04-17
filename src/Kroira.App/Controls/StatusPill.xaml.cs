using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kroira.App.Controls;

public enum StatusPillKind
{
    Neutral,
    Healthy,
    Standby,
    Syncing,
    Warning,
    Failed,
    Info,
    Live
}

public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusPill),
            new PropertyMetadata("STATUS", OnVisualChanged));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StatusPillKind), typeof(StatusPill),
            new PropertyMetadata(StatusPillKind.Neutral, OnVisualChanged));

    public StatusPill()
    {
        InitializeComponent();
        ApplyVisual();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public StatusPillKind Kind
    {
        get => (StatusPillKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusPill pill)
        {
            pill.ApplyVisual();
        }
    }

    private void ApplyVisual()
    {
        LabelText.Text = Label ?? string.Empty;

        var (fillKey, softKey) = Kind switch
        {
            StatusPillKind.Healthy => ("KroiraStateSuccessBrush", "KroiraStateSuccessSoftBrush"),
            StatusPillKind.Standby => ("KroiraStateNeutralBrush", "KroiraStateNeutralSoftBrush"),
            StatusPillKind.Syncing => ("KroiraStateInfoBrush", "KroiraStateInfoSoftBrush"),
            StatusPillKind.Warning => ("KroiraStateWarningBrush", "KroiraStateWarningSoftBrush"),
            StatusPillKind.Failed  => ("KroiraStateDangerBrush",  "KroiraStateDangerSoftBrush"),
            StatusPillKind.Info    => ("KroiraStateInfoBrush",    "KroiraStateInfoSoftBrush"),
            StatusPillKind.Live    => ("KroiraLiveDotBrush",      "KroiraStateDangerSoftBrush"),
            _                      => ("KroiraStateNeutralBrush", "KroiraStateNeutralSoftBrush"),
        };

        if (Application.Current.Resources.TryGetValue(fillKey, out var fill) && fill is Brush fillBrush)
        {
            Dot.Fill = fillBrush;
            LabelText.Foreground = fillBrush;
        }

        if (Application.Current.Resources.TryGetValue(softKey, out var soft) && soft is Brush softBrush)
        {
            PillRoot.Background = softBrush;
        }
    }
}
