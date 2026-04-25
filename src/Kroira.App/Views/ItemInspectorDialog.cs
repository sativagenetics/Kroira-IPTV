#nullable enable
using System;
using System.Threading.Tasks;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Kroira.App.Views
{
    public sealed class ItemInspectorDialog : ContentDialog
    {
        private readonly PlayableItemInspectionSnapshot _snapshot;
        private readonly PlaybackLaunchContext _context;
        private readonly IExternalPlayerLaunchService _externalPlayerLaunchService;
        private readonly TextBlock _actionStatusText;

        private ItemInspectorDialog(
            PlayableItemInspectionSnapshot snapshot,
            PlaybackLaunchContext context,
            IExternalPlayerLaunchService externalPlayerLaunchService)
        {
            _snapshot = snapshot;
            _context = context;
            _externalPlayerLaunchService = externalPlayerLaunchService;
            _actionStatusText = new TextBlock
            {
                Foreground = (Brush)Application.Current.Resources["KroiraTextSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            };

            Title = LocalizedStrings.Get("ItemInspector.Title");
            PrimaryButtonText = LocalizedStrings.Get("GeneralCopySafeReportButton.Content");
            SecondaryButtonText = snapshot.SupportsExternalLaunch ? LocalizedStrings.Get("PlayerOpenExternalButton.Content") : string.Empty;
            CloseButtonText = LocalizedStrings.Get("General.Close");
            DefaultButton = ContentDialogButton.Close;
            MinWidth = 820;
            MaxWidth = 920;
            Content = BuildContent();

            PrimaryButtonClick += CopySafeReport_Click;
            SecondaryButtonClick += OpenExternal_Click;
        }

        public static async Task ShowAsync(
            XamlRoot xamlRoot,
            PlaybackLaunchContext context,
            PlayableItemInspectionRuntimeState? runtimeState = null)
        {
            using var scope = ((App)Application.Current).Services.CreateScope();
            var inspectionService = scope.ServiceProvider.GetRequiredService<IPlayableItemInspectionService>();
            var externalPlayerLaunchService = ((App)Application.Current).Services.GetRequiredService<IExternalPlayerLaunchService>();
            var snapshot = await inspectionService.BuildAsync(context, runtimeState);
            var dialog = new ItemInspectorDialog(snapshot, context.Clone(), externalPlayerLaunchService)
            {
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
        }

        private UIElement BuildContent()
        {
            var root = new StackPanel
            {
                Spacing = 14
            };

            root.Children.Add(new TextBlock
            {
                Text = _snapshot.Title,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["KroiraTextPrimaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(_snapshot.Subtitle))
            {
                root.Children.Add(new TextBlock
                {
                    Text = _snapshot.Subtitle,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["KroiraTextSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap
                });
            }

            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = _snapshot.StatusText,
                            Foreground = (Brush)Application.Current.Resources["KroiraTextPrimaryBrush"],
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = _snapshot.SafetyText,
                            Foreground = (Brush)Application.Current.Resources["KroiraTextSecondaryBrush"],
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            });

            if (!string.IsNullOrWhiteSpace(_snapshot.FailureText))
            {
                root.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x1F, 0xFF, 0x7B, 0x6A)),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x3A, 0xFF, 0x7B, 0x6A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Child = new TextBlock
                    {
                        Text = _snapshot.FailureText,
                        Foreground = (Brush)Application.Current.Resources["KroiraTextPrimaryBrush"],
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }

            foreach (var section in _snapshot.Sections)
            {
                if (section.Fields.Count == 0)
                {
                    continue;
                }

                var sectionPanel = new StackPanel
                {
                    Spacing = 8
                };

                sectionPanel.Children.Add(new TextBlock
                {
                    Text = section.Title.ToUpperInvariant(),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["KroiraAccentBrush"]
                });

                foreach (var field in section.Fields)
                {
                    sectionPanel.Children.Add(new Grid
                    {
                        ColumnSpacing = 12,
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = GridLength.Auto },
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                        },
                        Children =
                        {
                            new TextBlock
                            {
                                Text = field.Label,
                                Foreground = (Brush)Application.Current.Resources["KroiraTextSecondaryBrush"],
                                FontSize = 12,
                                Margin = new Thickness(0, 0, 0, 2)
                            },
                            new TextBlock
                            {
                                Text = field.Value,
                                Foreground = (Brush)Application.Current.Resources["KroiraTextPrimaryBrush"],
                                FontSize = 12,
                                TextWrapping = TextWrapping.Wrap
                            }.WithGridColumn(1)
                        }
                    });
                }

                root.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(12),
                    Child = sectionPanel
                });
            }

            root.Children.Add(_actionStatusText);

            return new ScrollViewer
            {
                Content = root,
                MaxHeight = 680,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private void CopySafeReport_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;

            try
            {
                var package = new DataPackage();
                package.SetText(_snapshot.SafeReportText);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                _actionStatusText.Text = LocalizedStrings.Get("ItemInspector.CopiedSafeReport");
            }
            catch (Exception ex)
            {
                _actionStatusText.Text = LocalizedStrings.Format("ItemInspector.CopyFailed", ex.Message);
            }
        }

        private async void OpenExternal_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();

            try
            {
                IsPrimaryButtonEnabled = false;
                IsSecondaryButtonEnabled = false;

                var result = await _externalPlayerLaunchService.LaunchAsync(
                    _context.Clone(),
                    preferCurrentResolvedStream: _snapshot.IsCurrentPlayback);
                if (result.Success)
                {
                    _actionStatusText.Text = result.Message;
                    Hide();
                    return;
                }

                _actionStatusText.Text = result.Message;
            }
            finally
            {
                IsPrimaryButtonEnabled = true;
                IsSecondaryButtonEnabled = true;
                deferral.Complete();
            }
        }
    }

    internal static class ItemInspectorDialogExtensions
    {
        public static T WithGridColumn<T>(this T element, int column)
            where T : FrameworkElement
        {
            Grid.SetColumn(element, column);
            return element;
        }
    }
}
