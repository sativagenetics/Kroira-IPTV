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

            if (string.Equals(source.Type, SourceType.Xtream.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Type, SourceType.Stalker.ToString(), StringComparison.OrdinalIgnoreCase))
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

            var proxyOptions = new[]
            {
                new ProxyModeOption(SourceProxyScope.Disabled, "Direct routing", "Use direct provider routing for import, guide, probe, and playback requests."),
                new ProxyModeOption(SourceProxyScope.PlaybackOnly, "Playback only", "Route playback through the proxy while refresh and guide traffic stay direct."),
                new ProxyModeOption(SourceProxyScope.PlaybackAndProbing, "Playback + probes", "Route playback and bounded operational probes through the proxy."),
                new ProxyModeOption(SourceProxyScope.AllRequests, "All requests", "Route import, guide, probe, and playback traffic through the proxy.")
            };

            var companionOptions = new[]
            {
                new CompanionScopeOption(SourceCompanionScope.Disabled, "Disabled", "Keep direct playback as the only path for this source."),
                new CompanionScopeOption(SourceCompanionScope.PlaybackOnly, "Playback only", "Resolve the provider stream first, then hand playback to the local companion relay."),
                new CompanionScopeOption(SourceCompanionScope.PlaybackAndProbing, "Playback + probes", "Resolve the provider stream first, then use the local companion relay for playback and bounded health probes.")
            };

            var companionModeOptions = new[]
            {
                new CompanionModeOption(SourceCompanionRelayMode.Relay, "Pass-through relay", "Ask the companion to relay the already resolved upstream stream without buffering hints."),
                new CompanionModeOption(SourceCompanionRelayMode.Buffered, "Buffered relay", "Ask the companion to stabilize the already resolved upstream stream through a buffered relay.")
            };

            var modeComboBox = new ComboBox
            {
                ItemsSource = modeOptions,
                DisplayMemberPath = nameof(GuideModeOption.Label),
                SelectedItem = modeOptions.FirstOrDefault(option => option.Mode == draft.ActiveMode) ?? modeOptions[0]
            };

            var proxyComboBox = new ComboBox
            {
                Header = "Routing policy",
                ItemsSource = proxyOptions,
                DisplayMemberPath = nameof(ProxyModeOption.Label),
                SelectedItem = proxyOptions.FirstOrDefault(option => option.Scope == draft.ProxyScope) ?? proxyOptions[0]
            };

            var manualUrlBox = new TextBox
            {
                Header = "Manual XMLTV URL",
                PlaceholderText = "https://... or C:\\guide.xml",
                Text = draft.ManualEpgUrl
            };

            var proxyUrlBox = new TextBox
            {
                Header = "Proxy URL",
                PlaceholderText = "http://proxy-host:port or socks5://proxy-host:port",
                Text = draft.ProxyUrl
            };

            var companionComboBox = new ComboBox
            {
                Header = "Local companion relay",
                ItemsSource = companionOptions,
                DisplayMemberPath = nameof(CompanionScopeOption.Label),
                SelectedItem = companionOptions.FirstOrDefault(option => option.Scope == draft.CompanionScope) ?? companionOptions[0]
            };

            var companionModeComboBox = new ComboBox
            {
                Header = "Companion behavior",
                ItemsSource = companionModeOptions,
                DisplayMemberPath = nameof(CompanionModeOption.Label),
                SelectedItem = companionModeOptions.FirstOrDefault(option => option.Mode == draft.CompanionMode) ?? companionModeOptions[1]
            };

            var companionUrlBox = new TextBox
            {
                Header = "Companion endpoint",
                PlaceholderText = "http://127.0.0.1:9318/kroira-companion",
                Text = draft.CompanionUrl
            };

            var modeDescription = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Application.Current.Resources["KroiraTextSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush)
            };

            var proxyDescription = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Application.Current.Resources["KroiraTextSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush)
            };

            var companionDescription = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Application.Current.Resources["KroiraTextSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush)
            };

            var companionModeDescription = new TextBlock
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

            void RefreshProxyInputs()
            {
                if (proxyComboBox.SelectedItem is ProxyModeOption option)
                {
                    proxyUrlBox.IsEnabled = option.Scope != SourceProxyScope.Disabled;
                    proxyDescription.Text = option.Description;
                }
            }

            void RefreshCompanionInputs()
            {
                if (companionComboBox.SelectedItem is CompanionScopeOption option)
                {
                    var enabled = option.Scope != SourceCompanionScope.Disabled;
                    companionModeComboBox.IsEnabled = enabled;
                    companionUrlBox.IsEnabled = enabled;
                    companionModeComboBox.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                    companionModeDescription.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                    companionUrlBox.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                    companionDescription.Text = option.Description;
                }

                if (companionModeComboBox.SelectedItem is CompanionModeOption modeOption)
                {
                    companionModeDescription.Text = modeOption.Description;
                }
            }

            modeComboBox.SelectionChanged += (_, _) => RefreshGuideInputs();
            proxyComboBox.SelectionChanged += (_, _) => RefreshProxyInputs();
            companionComboBox.SelectionChanged += (_, _) => RefreshCompanionInputs();
            companionModeComboBox.SelectionChanged += (_, _) => RefreshCompanionInputs();
            RefreshGuideInputs();
            RefreshProxyInputs();
            RefreshCompanionInputs();

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
                                ? "Choose how this playlist should resolve XMLTV guide data and how the source should be routed operationally."
                                : draft.SourceType == SourceType.Stalker
                                    ? "Choose whether this Stalker portal should use a manual XMLTV feed and how its requests should be routed operationally."
                                : "Choose whether guide data should come from the provider or a manual XMLTV override, then decide how routing should behave.",
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
                        manualUrlBox,
                        new Border
                        {
                            Height = 1,
                            Margin = new Thickness(0, 4, 0, 4),
                            Background = Application.Current.Resources["KroiraBorderBrush"] as Microsoft.UI.Xaml.Media.Brush
                        },
                        proxyComboBox,
                        proxyDescription,
                        proxyUrlBox,
                        new Border
                        {
                            Height = 1,
                            Margin = new Thickness(0, 4, 0, 4),
                            Background = Application.Current.Resources["KroiraBorderBrush"] as Microsoft.UI.Xaml.Media.Brush
                        },
                        companionComboBox,
                        companionDescription,
                        companionModeComboBox,
                        companionModeDescription,
                        companionUrlBox
                    }
                }
            };

            dialog.Title = $"Guide settings - {draft.SourceName}";
            var result = await ShowContentDialogAsync(dialog);
            if (result is not ContentDialogResult.Primary and not ContentDialogResult.Secondary)
            {
                return;
            }

            draft.ActiveMode = (modeComboBox.SelectedItem as GuideModeOption)?.Mode ?? EpgActiveMode.Detected;
            draft.ManualEpgUrl = manualUrlBox.Text?.Trim() ?? string.Empty;
            draft.ProxyScope = (proxyComboBox.SelectedItem as ProxyModeOption)?.Scope ?? SourceProxyScope.Disabled;
            draft.ProxyUrl = proxyUrlBox.Text?.Trim() ?? string.Empty;
            draft.CompanionScope = (companionComboBox.SelectedItem as CompanionScopeOption)?.Scope ?? SourceCompanionScope.Disabled;
            draft.CompanionMode = (companionModeComboBox.SelectedItem as CompanionModeOption)?.Mode ?? SourceCompanionRelayMode.Buffered;
            draft.CompanionUrl = companionUrlBox.Text?.Trim() ?? string.Empty;

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

        private sealed class ProxyModeOption
        {
            public ProxyModeOption(SourceProxyScope scope, string label, string description)
            {
                Scope = scope;
                Label = label;
                Description = description;
            }

            public SourceProxyScope Scope { get; }
            public string Label { get; }
            public string Description { get; }
        }

        private sealed class CompanionScopeOption
        {
            public CompanionScopeOption(SourceCompanionScope scope, string label, string description)
            {
                Scope = scope;
                Label = label;
                Description = description;
            }

            public SourceCompanionScope Scope { get; }
            public string Label { get; }
            public string Description { get; }
        }

        private sealed class CompanionModeOption
        {
            public CompanionModeOption(SourceCompanionRelayMode mode, string label, string description)
            {
                Mode = mode;
                Label = label;
                Description = description;
            }

            public SourceCompanionRelayMode Mode { get; }
            public string Label { get; }
            public string Description { get; }
        }
    }
}
