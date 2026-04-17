using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Controls
{
    public sealed partial class Rail : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(object),
                typeof(Rail),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(
                nameof(ItemTemplate),
                typeof(DataTemplate),
                typeof(Rail),
                new PropertyMetadata(null, OnItemTemplateChanged));

        public static readonly DependencyProperty ItemSpacingProperty =
            DependencyProperty.Register(
                nameof(ItemSpacing),
                typeof(double),
                typeof(Rail),
                new PropertyMetadata(14d, OnItemSpacingChanged));

        public static readonly DependencyProperty RailHeightProperty =
            DependencyProperty.Register(
                nameof(RailHeight),
                typeof(double),
                typeof(Rail),
                new PropertyMetadata(130d, OnRailHeightChanged));

        public static readonly DependencyProperty ScrollAmountProperty =
            DependencyProperty.Register(
                nameof(ScrollAmount),
                typeof(double),
                typeof(Rail),
                new PropertyMetadata(360d));

        public Rail()
        {
            InitializeComponent();
            ApplyItemsSource();
            ApplyItemTemplate();
            ApplyItemSpacing();
            ApplyRailHeight();
            Loaded += Rail_Loaded;
            SizeChanged += Rail_SizeChanged;
        }

        public object ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public DataTemplate ItemTemplate
        {
            get => (DataTemplate)GetValue(ItemTemplateProperty);
            set => SetValue(ItemTemplateProperty, value);
        }

        public double ItemSpacing
        {
            get => (double)GetValue(ItemSpacingProperty);
            set => SetValue(ItemSpacingProperty, value);
        }

        public double RailHeight
        {
            get => (double)GetValue(RailHeightProperty);
            set => SetValue(RailHeightProperty, value);
        }

        public double ScrollAmount
        {
            get => (double)GetValue(ScrollAmountProperty);
            set => SetValue(ScrollAmountProperty, value);
        }

        private void Rail_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateButtonState();
        }

        private void Rail_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateButtonState();
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            ScrollBy(-ScrollAmount);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            ScrollBy(ScrollAmount);
        }

        private void RailScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateButtonState();
        }

        private void ScrollBy(double delta)
        {
            var target = RailScrollViewer.HorizontalOffset + delta;
            target = Math.Max(0, Math.Min(target, RailScrollViewer.ScrollableWidth));
            RailScrollViewer.ChangeView(target, null, null, disableAnimation: false);
        }

        private void UpdateButtonState()
        {
            var hasOverflow = RailScrollViewer.ScrollableWidth > 0;
            PreviousButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            PreviousButton.IsEnabled = RailScrollViewer.HorizontalOffset > 0;
            NextButton.IsEnabled = RailScrollViewer.HorizontalOffset < RailScrollViewer.ScrollableWidth;
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Rail rail)
            {
                rail.ApplyItemsSource();
            }
        }

        private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Rail rail)
            {
                rail.ApplyItemTemplate();
            }
        }

        private static void OnItemSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Rail rail)
            {
                rail.ApplyItemSpacing();
            }
        }

        private static void OnRailHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Rail rail)
            {
                rail.ApplyRailHeight();
            }
        }

        private void ApplyItemsSource()
        {
            RailRepeater.ItemsSource = ItemsSource;
            UpdateButtonState();
        }

        private void ApplyItemTemplate()
        {
            RailRepeater.ItemTemplate = ItemTemplate;
        }

        private void ApplyItemSpacing()
        {
            RailStackLayout.Spacing = ItemSpacing;
        }

        private void ApplyRailHeight()
        {
            RailRoot.Height = RailHeight;
        }
    }
}
