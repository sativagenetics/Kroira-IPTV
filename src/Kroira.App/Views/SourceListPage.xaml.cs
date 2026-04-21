using System;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Models;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Kroira.App.Views
{
    public sealed partial class SourceListPage : Page
    {
        public SourceListViewModel ViewModel { get; }

        public SourceListPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SourceListViewModel>();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.LoadSourcesCommand.Execute(null);
        }

        private void AddNewSource_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(SourceOnboardingPage));
        }

        private void DeleteSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                ViewModel.DeleteSourceCommand.Execute(id);
            }
        }

        private void ParseSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.ParseSourceCommand.Execute(id);
            }
        }

        private void SyncSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: int id })
            {
                return;
            }

            var source = ViewModel.Sources.FirstOrDefault(item => item.Id == id);
            if (source == null)
            {
                return;
            }

            if (string.Equals(source.Type, SourceType.Xtream.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SyncXtreamCommand.Execute(id);
                return;
            }

            ViewModel.ParseSourceCommand.Execute(id);
        }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                this.Frame.Navigate(typeof(ChannelBrowserPage), id);
            }
        }

        private void SyncEpgSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                ViewModel.SyncEpgCommand.Execute(id);
            }
        }

        private void SyncXtreamSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.SyncXtreamCommand.Execute(id);
            }
        }

        private void SyncXtreamVodSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                ViewModel.SyncXtreamVodCommand.Execute(id);
            }
        }

        private async void EditGuideSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: int id })
            {
                return;
            }

            var draft = await ViewModel.GetGuideSettingsAsync(id);
            if (draft == null)
            {
                await ShowMessageAsync("Guide settings", "Source guide settings could not be loaded.");
                return;
            }

            var modeOptions = new[]
            {
                new GuideModeOption(EpgActiveMode.Detected, "Detected from provider", "Use the provider-advertised or provider-derived XMLTV URL."),
                new GuideModeOption(EpgActiveMode.Manual, "Manual override", "Use your own XMLTV URL and keep the detected URL on file."),
                new GuideModeOption(EpgActiveMode.None, "No guide", "Disable guide syncing for this source.")
            };

            var modeComboBox = new ComboBox
            {
                ItemsSource = modeOptions,
                DisplayMemberPath = nameof(GuideModeOption.Label),
                SelectedItem = modeOptions.FirstOrDefault(option => option.Mode == draft.ActiveMode) ?? modeOptions[0]
            };

            var manualUrlBox = new TextBox
            {
                Header = "Manual XMLTV URL",
                PlaceholderText = "https://... or C:\\guide.xml",
                Text = draft.ManualEpgUrl
            };

            var modeDescription = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Application.Current.Resources["KroiraTextSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush)
            };

            void RefreshGuideInputs()
            {
                if (modeComboBox.SelectedItem is GuideModeOption option)
                {
                    manualUrlBox.IsEnabled = option.Mode == EpgActiveMode.Manual;
                    modeDescription.Text = option.Description;
                }
            }

            modeComboBox.SelectionChanged += (_, _) => RefreshGuideInputs();
            RefreshGuideInputs();

            var dialog = new ContentDialog
            {
                Title = $"Guide settings · {draft.SourceName}",
                PrimaryButtonText = "Save and sync",
                SecondaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = draft.SourceType == SourceType.M3U
                                ? "Choose how this playlist should resolve XMLTV guide data."
                                : "Choose whether guide data should come from the provider or a manual XMLTV override.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(draft.DetectedEpgUrl)
                                ? "Detected XMLTV URL: none recorded yet"
                                : $"Detected XMLTV URL: {draft.DetectedEpgUrl}",
                            TextWrapping = TextWrapping.Wrap
                        },
                        modeComboBox,
                        modeDescription,
                        manualUrlBox
                    }
                }
            };

            var result = await ShowContentDialogAsync(dialog);
            if (result is not ContentDialogResult.Primary and not ContentDialogResult.Secondary)
            {
                return;
            }

            draft.ActiveMode = (modeComboBox.SelectedItem as GuideModeOption)?.Mode ?? EpgActiveMode.Detected;
            draft.ManualEpgUrl = manualUrlBox.Text?.Trim() ?? string.Empty;

            try
            {
                await ViewModel.SaveGuideSettingsAsync(draft, syncNow: result == ContentDialogResult.Primary);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Guide settings failed", ex.Message);
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            await ShowContentDialogAsync(new ContentDialog
            {
                Title = title,
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
                Content = message
            });
        }

        private static Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
        {
            var completion = new TaskCompletionSource<ContentDialogResult>();
            var operation = dialog.ShowAsync();
            operation.Completed = (info, status) =>
            {
                switch (status)
                {
                    case AsyncStatus.Completed:
                        completion.TrySetResult(info.GetResults());
                        break;
                    case AsyncStatus.Canceled:
                        completion.TrySetCanceled();
                        break;
                    case AsyncStatus.Error:
                        completion.TrySetException(info.ErrorCode);
                        break;
                }
            };

            return completion.Task;
        }

        private sealed class GuideModeOption
        {
            public GuideModeOption(EpgActiveMode mode, string label, string description)
            {
                Mode = mode;
                Label = label;
                Description = description;
            }

            public EpgActiveMode Mode { get; }
            public string Label { get; }
            public string Description { get; }
        }
    }
}
