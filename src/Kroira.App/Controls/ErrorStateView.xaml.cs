#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Controls
{
    public sealed partial class ErrorStateView : UserControl
    {
        public static readonly DependencyProperty IconGlyphProperty =
            DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(ErrorStateView), new PropertyMetadata("\uE783"));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(ErrorStateView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(ErrorStateView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty RetryActionLabelProperty =
            DependencyProperty.Register(nameof(RetryActionLabel), typeof(string), typeof(ErrorStateView), new PropertyMetadata("Retry"));

        public static readonly DependencyProperty DiagnosticsActionLabelProperty =
            DependencyProperty.Register(nameof(DiagnosticsActionLabel), typeof(string), typeof(ErrorStateView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty RetryActionVisibilityProperty =
            DependencyProperty.Register(nameof(RetryActionVisibility), typeof(Visibility), typeof(ErrorStateView), new PropertyMetadata(Visibility.Visible));

        public static readonly DependencyProperty DiagnosticsActionVisibilityProperty =
            DependencyProperty.Register(nameof(DiagnosticsActionVisibility), typeof(Visibility), typeof(ErrorStateView), new PropertyMetadata(Visibility.Collapsed));

        public event RoutedEventHandler? RetryClick;
        public event RoutedEventHandler? DiagnosticsClick;

        public ErrorStateView()
        {
            InitializeComponent();
        }

        public string IconGlyph
        {
            get => (string)GetValue(IconGlyphProperty);
            set => SetValue(IconGlyphProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public string RetryActionLabel
        {
            get => (string)GetValue(RetryActionLabelProperty);
            set => SetValue(RetryActionLabelProperty, value);
        }

        public string DiagnosticsActionLabel
        {
            get => (string)GetValue(DiagnosticsActionLabelProperty);
            set => SetValue(DiagnosticsActionLabelProperty, value);
        }

        public Visibility RetryActionVisibility
        {
            get => (Visibility)GetValue(RetryActionVisibilityProperty);
            set => SetValue(RetryActionVisibilityProperty, value);
        }

        public Visibility DiagnosticsActionVisibility
        {
            get => (Visibility)GetValue(DiagnosticsActionVisibilityProperty);
            set => SetValue(DiagnosticsActionVisibilityProperty, value);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            RetryClick?.Invoke(this, e);
        }

        private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            DiagnosticsClick?.Invoke(this, e);
        }
    }
}
