using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Kroira.App.Controls
{
    public sealed partial class Rail : UserControl
    {
        private static readonly string StartupLogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "startup-log.txt");

        private double _lastLoggedHorizontalOffset = -1;
        private string _lastPointerEnterKey = string.Empty;
        private bool _suppressOffsetLog;

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
            RailRoot.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(RailRoot_PointerEntered), true);
            RailRoot.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(RailRoot_PointerWheelChanged), true);
            RailRoot.GotFocus += RailRoot_GotFocus;
            RailRoot.BringIntoViewRequested += RailRoot_BringIntoViewRequested;
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

        public bool TryFocusPrimaryItem()
        {
            if (RailRepeater.ItemsSource == null)
            {
                return false;
            }

            if (RailRepeater.TryGetElement(0) is FrameworkElement element)
            {
                if (element is Control control && control.Focus(FocusState.Keyboard))
                {
                    return true;
                }

                if (element.Focus(FocusState.Keyboard))
                {
                    return true;
                }
            }

            return RailRoot.Focus(FocusState.Keyboard);
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
            LogRail($"arrow previous click beforeH={RailScrollViewer.HorizontalOffset:0.##}");
            ScrollBy(-ScrollAmount);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            LogRail($"arrow next click beforeH={RailScrollViewer.HorizontalOffset:0.##}");
            ScrollBy(ScrollAmount);
        }

        private void RailScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!_suppressOffsetLog && Math.Abs(RailScrollViewer.HorizontalOffset - _lastLoggedHorizontalOffset) > 0.1)
            {
                LogRail($"horizontal offset changed offset={RailScrollViewer.HorizontalOffset:0.##} scrollable={RailScrollViewer.ScrollableWidth:0.##} intermediate={e.IsIntermediate}");
                _lastLoggedHorizontalOffset = RailScrollViewer.HorizontalOffset;
            }

            UpdateButtonState();
        }

        private void ScrollBy(double delta)
        {
            var target = RailScrollViewer.HorizontalOffset + delta;
            target = Math.Max(0, Math.Min(target, RailScrollViewer.ScrollableWidth));
            LogRail($"scrollBy delta={delta:0.##} from={RailScrollViewer.HorizontalOffset:0.##} to={target:0.##}");
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

        private void RailRoot_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            var sourceName = DescribeSource(e.OriginalSource);
            if (string.Equals(sourceName, _lastPointerEnterKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastPointerEnterKey = sourceName;
            LogRail($"pointer enter source={sourceName} h={RailScrollViewer.HorizontalOffset:0.##}");
        }

        private void RailRoot_GotFocus(object sender, RoutedEventArgs e)
        {
            LogRail($"focus source={DescribeSource(e.OriginalSource)} h={RailScrollViewer.HorizontalOffset:0.##}");
        }

        private void RailRoot_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
        {
            LogRail($"bring-into-view source={DescribeSource(args.OriginalSource)} h={RailScrollViewer.HorizontalOffset:0.##}");
        }

        private void RailRoot_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(RailRoot).Properties.MouseWheelDelta;
            var sourceName = DescribeSource(e.OriginalSource);
            var parentScrollViewer = FindAncestorVerticalScrollViewer();
            LogRail(
                $"wheel source={sourceName} delta={delta} beforeH={RailScrollViewer.HorizontalOffset:0.##} beforeParentV={(parentScrollViewer?.VerticalOffset ?? -1):0.##}");

            if (parentScrollViewer == null || delta == 0)
            {
                return;
            }

            var targetVerticalOffset = parentScrollViewer.VerticalOffset - delta;
            targetVerticalOffset = Math.Max(0, Math.Min(targetVerticalOffset, parentScrollViewer.ScrollableHeight));
            _suppressOffsetLog = true;
            try
            {
                parentScrollViewer.ChangeView(null, targetVerticalOffset, null, disableAnimation: true);
            }
            finally
            {
                _suppressOffsetLog = false;
            }

            e.Handled = true;
            LogRail(
                $"wheel rerouted source={sourceName} delta={delta} afterH={RailScrollViewer.HorizontalOffset:0.##} afterParentV={targetVerticalOffset:0.##}");
        }

        private ScrollViewer? FindAncestorVerticalScrollViewer()
        {
            DependencyObject current = this;
            while ((current = VisualTreeHelper.GetParent(current)) != null)
            {
                if (current is ScrollViewer scrollViewer &&
                    !ReferenceEquals(scrollViewer, RailScrollViewer) &&
                    scrollViewer.VerticalScrollMode != ScrollMode.Disabled)
                {
                    return scrollViewer;
                }
            }

            return null;
        }

        private string DescribeSource(object originalSource)
        {
            if (originalSource is not FrameworkElement element)
            {
                return originalSource?.GetType().Name ?? "null";
            }

            if (!string.IsNullOrWhiteSpace(element.Name))
            {
                return $"{element.GetType().Name}#{element.Name}";
            }

            if (element.DataContext != null)
            {
                return $"{element.GetType().Name}:{element.DataContext.GetType().Name}";
            }

            return element.GetType().Name;
        }

        private void LogRail(string message)
        {
            try
            {
                var railName = string.IsNullOrWhiteSpace(Name) ? "Rail" : Name;
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] HOME RAIL {railName} {message}";
                Debug.WriteLine(line);
                File.AppendAllText(StartupLogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
