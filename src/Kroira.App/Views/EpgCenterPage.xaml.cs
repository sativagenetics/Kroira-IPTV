using System;
using System.Diagnostics;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace Kroira.App.Views
{
    public sealed partial class EpgCenterPage : Page, IRemoteNavigationPage
    {
        private const double TimelineProgramTop = 10d;
        private const double TimelineProgramHeight = 54d;
        private const double TimelineProgramHorizontalPadding = 7d;
        private const double TimelineProgramVerticalPadding = 5d;
        private static readonly Brush ProgramBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(0x4E, 0x11, 0x18, 0x27));
        private static readonly Brush ProgramBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x4D, 0xC1, 0x67, 0xFF));
        private static readonly Brush ProgramAccentBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xC1, 0x67, 0xFF));
        private static readonly Brush ProgramTextPrimaryBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF7, 0xF8, 0xFF));
        private static readonly Brush ProgramTextSecondaryBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xB9, 0xC2, 0xD6));
        private static readonly Brush ProgramTextTertiaryBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x78, 0x83, 0x9B));

        public EpgCenterViewModel ViewModel { get; }

        public EpgCenterPage()
        {
            InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<EpgCenterViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.LoadCommand.Execute(null);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshCommand.Execute(null);
        }

        private void RefreshGuideTimeline_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshGuideTimelineCommand.Execute(null);
        }

        private void JumpGuideToNow_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.JumpGuideToNowCommand.Execute(null);
        }

        private void TimelineProgram_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: GuideTimelineProgramViewModel program })
            {
                return;
            }

            var context = ViewModel.CreatePlaybackContext(program);
            if (context != null)
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), context);
            }
        }

        private void TimelineChannel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: GuideTimelineChannelViewModel channel })
            {
                return;
            }

            var context = ViewModel.CreatePlaybackContext(channel);
            if (context != null)
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), context);
            }
        }

        private void ClipToBounds_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyClipToBounds(sender as FrameworkElement);
        }

        private void ClipToBounds_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyClipToBounds(sender as FrameworkElement);
        }

        private void TimelineProgramCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            RenderTimelineProgramCanvas(sender as Canvas);
        }

        private void TimelineProgramCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderTimelineProgramCanvas(sender as Canvas);
        }

        private void TimelineProgramCanvas_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            RenderTimelineProgramCanvas(sender as Canvas);
        }

        private void RenderTimelineProgramCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            ApplyClipToBounds(canvas);
            canvas.Children.Clear();

            var row = canvas.Tag as GuideTimelineChannelViewModel ?? canvas.DataContext as GuideTimelineChannelViewModel;
            if (row == null)
            {
                return;
            }

            canvas.Width = row.TimelineWidth;
            canvas.Height = 76;

            foreach (var program in row.Programs)
            {
                var left = Math.Clamp(program.Left, 0, row.TimelineWidth);
                var width = Math.Clamp(program.Width, 0, Math.Max(0, row.TimelineWidth - left));
                if (width <= 0)
                {
                    continue;
                }

                var contentWidth = Math.Max(0, width - TimelineProgramHorizontalPadding * 2);
                Debug.Assert(contentWidth <= width, "EPG programme content width must fit inside block width.");
                Debug.Assert(left + width <= row.TimelineWidth + 0.5, "EPG programme block must fit inside timeline width.");

                var button = CreateTimelineProgramButton(program, width, contentWidth);
                Canvas.SetLeft(button, left);
                Canvas.SetTop(button, TimelineProgramTop);
                canvas.Children.Add(button);
            }
        }

        private Button CreateTimelineProgramButton(GuideTimelineProgramViewModel program, double width, double contentWidth)
        {
            var button = new Button
            {
                Width = width,
                MinWidth = 0,
                Height = TimelineProgramHeight,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Top,
                CommandParameter = program
            };
            button.Click += TimelineProgram_Click;
            ToolTipService.SetToolTip(button, program.ToolTipText);
            SetClip(button, width, TimelineProgramHeight);
            button.SizeChanged += ClipToBounds_SizeChanged;

            var root = new Grid
            {
                Width = width,
                Height = TimelineProgramHeight
            };
            SetClip(root, width, TimelineProgramHeight);
            root.SizeChanged += ClipToBounds_SizeChanged;

            root.Children.Add(new Border
            {
                Width = width,
                Height = TimelineProgramHeight,
                Background = ProgramBackgroundBrush,
                BorderBrush = ProgramBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9)
            });

            var contentRoot = new Grid
            {
                Width = width,
                Height = TimelineProgramHeight,
                Padding = new Thickness(
                    TimelineProgramHorizontalPadding,
                    TimelineProgramVerticalPadding,
                    TimelineProgramHorizontalPadding,
                    TimelineProgramVerticalPadding)
            };
            SetClip(contentRoot, width, TimelineProgramHeight);
            contentRoot.SizeChanged += ClipToBounds_SizeChanged;

            if (program.CompactVisualVisibility == Visibility.Visible)
            {
                contentRoot.Children.Add(new Border
                {
                    Width = Math.Max(2, Math.Min(12, contentWidth)),
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    CornerRadius = new CornerRadius(4),
                    Background = ProgramAccentBrush,
                    Opacity = 0.72
                });
            }
            else
            {
                var stack = new StackPanel
                {
                    Width = contentWidth,
                    MaxWidth = contentWidth,
                    Spacing = 1
                };
                SetClip(stack, contentWidth, Math.Max(0, TimelineProgramHeight - TimelineProgramVerticalPadding * 2));
                stack.SizeChanged += ClipToBounds_SizeChanged;

                if (program.TinyTextVisibility == Visibility.Visible)
                {
                    stack.Children.Add(CreateProgramTextBlock(program.TinyText, contentWidth, ProgramTextSecondaryBrush, 10, FontWeights.SemiBold));
                }

                if (program.TitleVisibility == Visibility.Visible)
                {
                    stack.Children.Add(CreateProgramTextBlock(program.Title, contentWidth, ProgramTextPrimaryBrush, 12, FontWeights.SemiBold));
                }

                if (program.TimeVisibility == Visibility.Visible)
                {
                    stack.Children.Add(CreateProgramTextBlock(program.TimeText, contentWidth, ProgramTextSecondaryBrush, 10, FontWeights.Normal));
                }

                if (program.DetailVisibility == Visibility.Visible)
                {
                    stack.Children.Add(CreateProgramTextBlock(program.DetailText, contentWidth, ProgramTextTertiaryBrush, 10, FontWeights.Normal));
                }

                contentRoot.Children.Add(stack);
            }

            root.Children.Add(contentRoot);
            button.Content = root;
            return button;
        }

        private static TextBlock CreateProgramTextBlock(string text, double width, Brush foreground, double fontSize, Windows.UI.Text.FontWeight fontWeight)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Width = width,
                MaxWidth = width,
                Foreground = foreground,
                FontSize = fontSize,
                FontWeight = fontWeight,
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            SetClip(textBlock, width, 18);
            return textBlock;
        }

        private static void ApplyClipToBounds(FrameworkElement element)
        {
            if (element == null)
            {
                return;
            }

            var width = Math.Max(0, element.ActualWidth);
            var height = Math.Max(0, element.ActualHeight);
            SetClip(element, width, height);
        }

        private static void SetClip(UIElement element, double width, double height)
        {
            element.Clip = width <= 0 || height <= 0
                ? null
                : new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        }

        private void SetManualOverride_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: ManualMatchCandidateViewModel candidate })
            {
                ViewModel.SetManualOverrideCommand.Execute(candidate);
            }
        }

        private void ClearManualOverride_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearManualOverrideCommand.Execute(null);
        }

        private void AddPresetGuide_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddSelectedPublicGuideCommand.Execute(null);
        }

        private void AddCustomGuide_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddCustomPublicGuideCommand.Execute(null);
        }

        private void ReplaceWithAutoDetected_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ReplaceWithAutoDetectedPublicGuidesCommand.Execute(null);
        }

        private void ClearPublicGuidePresets_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearPublicGuidePresetsCommand.Execute(null);
        }

        private void ClearReviewDecisions_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearReviewDecisionsCommand.Execute(null);
        }

        private void ApproveWeakMatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is EpgChannelCoverageViewModel item)
            {
                ViewModel.ApproveWeakMatchCommand.Execute(item);
            }
        }

        private void RejectWeakMatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is EpgChannelCoverageViewModel item)
            {
                ViewModel.RejectWeakMatchCommand.Execute(item);
            }
        }

        private void ClearMappingDecision_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is EpgChannelCoverageViewModel item)
            {
                ViewModel.ClearMappingDecisionCommand.Execute(item);
            }
        }

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusElement(RefreshButton);
        }

        public bool TryHandleBackRequest()
        {
            return false;
        }
    }
}
