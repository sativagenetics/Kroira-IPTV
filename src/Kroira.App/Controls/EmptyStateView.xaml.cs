#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Controls
{
    public sealed partial class EmptyStateView : UserControl
    {
        public static readonly DependencyProperty IconGlyphProperty =
            DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(EmptyStateView), new PropertyMetadata("\uE946"));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyStateView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyStateView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty PrimaryActionLabelProperty =
            DependencyProperty.Register(nameof(PrimaryActionLabel), typeof(string), typeof(EmptyStateView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SecondaryActionLabelProperty =
            DependencyProperty.Register(nameof(SecondaryActionLabel), typeof(string), typeof(EmptyStateView), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty PrimaryActionVisibilityProperty =
            DependencyProperty.Register(nameof(PrimaryActionVisibility), typeof(Visibility), typeof(EmptyStateView), new PropertyMetadata(Visibility.Collapsed));

        public static readonly DependencyProperty SecondaryActionVisibilityProperty =
            DependencyProperty.Register(nameof(SecondaryActionVisibility), typeof(Visibility), typeof(EmptyStateView), new PropertyMetadata(Visibility.Collapsed));

        public event RoutedEventHandler? PrimaryActionClick;
        public event RoutedEventHandler? SecondaryActionClick;

        public EmptyStateView()
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

        public string PrimaryActionLabel
        {
            get => (string)GetValue(PrimaryActionLabelProperty);
            set => SetValue(PrimaryActionLabelProperty, value);
        }

        public string SecondaryActionLabel
        {
            get => (string)GetValue(SecondaryActionLabelProperty);
            set => SetValue(SecondaryActionLabelProperty, value);
        }

        public Visibility PrimaryActionVisibility
        {
            get => (Visibility)GetValue(PrimaryActionVisibilityProperty);
            set => SetValue(PrimaryActionVisibilityProperty, value);
        }

        public Visibility SecondaryActionVisibility
        {
            get => (Visibility)GetValue(SecondaryActionVisibilityProperty);
            set => SetValue(SecondaryActionVisibilityProperty, value);
        }

        private void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
        {
            PrimaryActionClick?.Invoke(this, e);
        }

        private void SecondaryActionButton_Click(object sender, RoutedEventArgs e)
        {
            SecondaryActionClick?.Invoke(this, e);
        }
    }
}
