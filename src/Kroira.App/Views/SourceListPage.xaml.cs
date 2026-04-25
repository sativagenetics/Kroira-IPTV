using System;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.ApplicationModel.DataTransfer;

namespace Kroira.App.Views
{
    public sealed partial class SourceListPage : Page, ILocalizationRefreshable
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

        private void OpenEpgCenter_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(EpgCenterPage));
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

        private async void CopyActivityReport_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: int id })
            {
                return;
            }

            var report = ViewModel.GetSafeActivityReport(id);
            if (string.IsNullOrWhiteSpace(report))
            {
                await ShowMessageAsync(LocalizedStrings.Get("Sources_ActivityReportUnavailableTitle"), LocalizedStrings.Get("Sources_ActivityReportUnavailableMessage"));
                return;
            }

            await CopyTextAsync(report, LocalizedStrings.Get("Sources_ActivityReportUnavailableTitle"));
        }

        private async void DiagnosticsAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: SourceRecommendedActionItemViewModel action })
            {
                return;
            }

            switch (action.ActionType)
            {
                case SourceRecommendedActionType.ResyncSource:
                    SyncSourceById(action.SourceId);
                    break;

                case SourceRecommendedActionType.ConfigureEpg:
                    await OpenGuideSettingsAsync(action.SourceId);
                    break;

                case SourceRecommendedActionType.OpenManualEpgMatch:
                    this.Frame.Navigate(typeof(EpgCenterPage));
                    break;

                case SourceRecommendedActionType.RefreshMetadata:
                    RefreshMetadataById(action.SourceId);
                    break;

                case SourceRecommendedActionType.RunStreamProbe:
                    await RunStreamProbeAsync(action.SourceId);
                    break;

                case SourceRecommendedActionType.ExportDiagnostics:
                    await CopyDiagnosticsReportAsync(action.SourceId);
                    break;

                case SourceRecommendedActionType.RemoveSource:
                    ViewModel.DeleteSourceCommand.Execute(action.SourceId);
                    break;
            }
        }

        private async void CopyRepairReport_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: int id })
            {
                return;
            }

            var report = ViewModel.GetSafeRepairReport(id);
            if (string.IsNullOrWhiteSpace(report))
            {
                await ShowMessageAsync(LocalizedStrings.Get("Sources_RepairReportUnavailableTitle"), LocalizedStrings.Get("Sources_RepairReportUnavailableMessage"));
                return;
            }

            await CopyTextAsync(report, LocalizedStrings.Get("Sources_RepairReportUnavailableTitle"));
        }

        private async Task CopyDiagnosticsReportAsync(int id)
        {
            var report = ViewModel.GetSafeDiagnosticsReport(id);
            if (string.IsNullOrWhiteSpace(report))
            {
                await ShowMessageAsync(LocalizedStrings.Get("Sources_DiagnosticsReportUnavailableTitle"), LocalizedStrings.Get("Sources_DiagnosticsReportUnavailableMessage"));
                return;
            }

            await CopyTextAsync(report, LocalizedStrings.Get("Sources_DiagnosticsReportUnavailableTitle"));
        }

        private void SyncSourceById(int id)
        {
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

        private void RefreshMetadataById(int id)
        {
            var source = ViewModel.Sources.FirstOrDefault(item => item.Id == id);
            if (source != null && source.CanRunXtreamVodOnly)
            {
                ViewModel.SyncXtreamVodCommand.Execute(id);
                return;
            }

            SyncSourceById(id);
        }

        private async Task RunStreamProbeAsync(int id)
        {
            try
            {
                var result = await ViewModel.ApplyRepairActionAsync(id, SourceRepairActionType.RunStreamProbe);
                if (result == null)
                {
                    return;
                }

                var detail = string.IsNullOrWhiteSpace(result.ChangeText)
                    ? result.DetailText
                    : $"{result.DetailText}{Environment.NewLine}{Environment.NewLine}{result.ChangeText}".Trim();
                await ShowMessageAsync(result.HeadlineText, detail);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(LocalizedStrings.Get("Sources_StreamProbeFailedTitle"), ex.Message);
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

            await OpenGuideSettingsAsync(id);
        }

        private async void RepairAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: SourceRepairActionItemViewModel action })
            {
                return;
            }

            if (action.Kind == SourceRepairActionKind.Review || action.ActionType == SourceRepairActionType.ReviewGuideSettings)
            {
                await OpenGuideSettingsAsync(action.SourceId);
                return;
            }

            try
            {
                var result = await ViewModel.ApplyRepairActionAsync(action.SourceId, action.ActionType);
                if (result == null)
                {
                    return;
                }

                var detail = string.IsNullOrWhiteSpace(result.ChangeText)
                    ? result.DetailText
                    : $"{result.DetailText}{Environment.NewLine}{Environment.NewLine}{result.ChangeText}".Trim();
                await ShowMessageAsync(result.HeadlineText, detail);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(LocalizedStrings.Get("Sources_RepairAttemptFailedTitle"), ex.Message);
            }
        }

        private async Task OpenGuideSettingsAsync(int id)
        {
            var draft = await ViewModel.GetGuideSettingsAsync(id);
            if (draft == null)
            {
                await ShowMessageAsync(LocalizedStrings.Get("Sources_GuideSettingsTitle"), LocalizedStrings.Get("Sources_GuideSettingsLoadFailed"));
                return;
            }

            var modeOptions = new[]
            {
                new GuideModeOption(EpgActiveMode.Detected, LocalizedStrings.Get("Sources_GuideMode_Detected_Label"), LocalizedStrings.Get("Sources_GuideMode_Detected_Description")),
                new GuideModeOption(EpgActiveMode.Manual, LocalizedStrings.Get("Sources_GuideMode_Manual_Label"), LocalizedStrings.Get("Sources_GuideMode_Manual_Description")),
                new GuideModeOption(EpgActiveMode.None, LocalizedStrings.Get("Sources_GuideMode_None_Label"), LocalizedStrings.Get("Sources_GuideMode_None_Description"))
            };

            var proxyOptions = new[]
            {
                new ProxyModeOption(SourceProxyScope.Disabled, LocalizedStrings.Get("Sources_ProxyMode_Direct_Label"), LocalizedStrings.Get("Sources_ProxyMode_Direct_Description")),
                new ProxyModeOption(SourceProxyScope.PlaybackOnly, LocalizedStrings.Get("Sources_ProxyMode_PlaybackOnly_Label"), LocalizedStrings.Get("Sources_ProxyMode_PlaybackOnly_Description")),
                new ProxyModeOption(SourceProxyScope.PlaybackAndProbing, LocalizedStrings.Get("Sources_ProxyMode_PlaybackProbes_Label"), LocalizedStrings.Get("Sources_ProxyMode_PlaybackProbes_Description")),
                new ProxyModeOption(SourceProxyScope.AllRequests, LocalizedStrings.Get("Sources_ProxyMode_AllRequests_Label"), LocalizedStrings.Get("Sources_ProxyMode_AllRequests_Description"))
            };

            var companionOptions = new[]
            {
                new CompanionScopeOption(SourceCompanionScope.Disabled, LocalizedStrings.Get("General_Disabled"), LocalizedStrings.Get("Sources_Companion_Disabled_Description")),
                new CompanionScopeOption(SourceCompanionScope.PlaybackOnly, LocalizedStrings.Get("Sources_ProxyMode_PlaybackOnly_Label"), LocalizedStrings.Get("Sources_Companion_PlaybackOnly_Description")),
                new CompanionScopeOption(SourceCompanionScope.PlaybackAndProbing, LocalizedStrings.Get("Sources_ProxyMode_PlaybackProbes_Label"), LocalizedStrings.Get("Sources_Companion_PlaybackProbes_Description"))
            };

            var companionModeOptions = new[]
            {
                new CompanionModeOption(SourceCompanionRelayMode.Relay, LocalizedStrings.Get("Sources_CompanionMode_Relay_Label"), LocalizedStrings.Get("Sources_CompanionMode_Relay_Description")),
                new CompanionModeOption(SourceCompanionRelayMode.Buffered, LocalizedStrings.Get("Sources_CompanionMode_Buffered_Label"), LocalizedStrings.Get("Sources_CompanionMode_Buffered_Description"))
            };

            var modeComboBox = new ComboBox
            {
                ItemsSource = modeOptions,
                DisplayMemberPath = nameof(GuideModeOption.Label),
                SelectedItem = modeOptions.FirstOrDefault(option => option.Mode == draft.ActiveMode) ?? modeOptions[0]
            };

            var proxyComboBox = new ComboBox
            {
                Header = LocalizedStrings.Get("Sources_RoutingPolicyHeader"),
                ItemsSource = proxyOptions,
                DisplayMemberPath = nameof(ProxyModeOption.Label),
                SelectedItem = proxyOptions.FirstOrDefault(option => option.Scope == draft.ProxyScope) ?? proxyOptions[0]
            };

            var manualUrlBox = new TextBox
            {
                Header = LocalizedStrings.Get("Sources_ManualXmltvUrlHeader"),
                PlaceholderText = "https://... or C:\\guide.xml",
                Text = draft.ManualEpgUrl
            };

            var fallbackUrlBox = new TextBox
            {
                Header = LocalizedStrings.Get("Sources_FallbackXmltvUrlsHeader"),
                PlaceholderText = LocalizedStrings.Get("Sources_FallbackXmltvUrlsPlaceholder"),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 88,
                Text = draft.FallbackEpgUrls
            };

            var proxyUrlBox = new TextBox
            {
                Header = LocalizedStrings.Get("Sources_ProxyUrlHeader"),
                PlaceholderText = "http://proxy-host:port or socks5://proxy-host:port",
                Text = draft.ProxyUrl
            };

            var companionComboBox = new ComboBox
            {
                Header = LocalizedStrings.Get("Sources_CompanionRelayHeader"),
                ItemsSource = companionOptions,
                DisplayMemberPath = nameof(CompanionScopeOption.Label),
                SelectedItem = companionOptions.FirstOrDefault(option => option.Scope == draft.CompanionScope) ?? companionOptions[0]
            };

            var companionModeComboBox = new ComboBox
            {
                Header = LocalizedStrings.Get("Sources_CompanionBehaviorHeader"),
                ItemsSource = companionModeOptions,
                DisplayMemberPath = nameof(CompanionModeOption.Label),
                SelectedItem = companionModeOptions.FirstOrDefault(option => option.Mode == draft.CompanionMode) ?? companionModeOptions[1]
            };

            var companionUrlBox = new TextBox
            {
                Header = LocalizedStrings.Get("Sources_CompanionEndpointHeader"),
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
                    fallbackUrlBox.IsEnabled = option.Mode != EpgActiveMode.None;
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
                Title = LocalizedStrings.Format("Sources_GuideSettingsForTitle", draft.SourceName),
                PrimaryButtonText = LocalizedStrings.Get("Sources_SaveAndSync"),
                SecondaryButtonText = LocalizedStrings.Get("General_Save"),
                CloseButtonText = LocalizedStrings.Get("General_Cancel"),
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
                                ? LocalizedStrings.Get("Sources_GuideSettings_M3uMessage")
                                : draft.SourceType == SourceType.Stalker
                                    ? LocalizedStrings.Get("Sources_GuideSettings_StalkerMessage")
                                : LocalizedStrings.Get("Sources_GuideSettings_DefaultMessage"),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(draft.DetectedEpgUrl)
                                ? LocalizedStrings.Get("Sources_DetectedXmltvNone")
                                : LocalizedStrings.Format("Sources_DetectedXmltvUrl", draft.DetectedEpgUrl),
                            TextWrapping = TextWrapping.Wrap
                        },
                        modeComboBox,
                        modeDescription,
                        manualUrlBox,
                        fallbackUrlBox,
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

            dialog.Title = LocalizedStrings.Format("Sources_GuideSettingsForTitle", draft.SourceName);
            var result = await ShowContentDialogAsync(dialog);
            if (result is not ContentDialogResult.Primary and not ContentDialogResult.Secondary)
            {
                return;
            }

            draft.ActiveMode = (modeComboBox.SelectedItem as GuideModeOption)?.Mode ?? EpgActiveMode.Detected;
            draft.ManualEpgUrl = manualUrlBox.Text?.Trim() ?? string.Empty;
            draft.FallbackEpgUrls = fallbackUrlBox.Text?.Trim() ?? string.Empty;
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
                await ShowMessageAsync(LocalizedStrings.Get("Sources_GuideSettingsFailedTitle"), ex.Message);
            }
        }

        private async Task CopyTextAsync(string text, string unavailableTitle)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                await ShowMessageAsync(unavailableTitle, LocalizedStrings.Get("Sources_NothingSafeToCopy"));
                return;
            }

            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                Clipboard.Flush();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(LocalizedStrings.Get("Sources_CopyFailedTitle"), ex.Message);
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            await ShowContentDialogAsync(new ContentDialog
            {
                Title = title,
                CloseButtonText = LocalizedStrings.Get("General_Close"),
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

        public void RefreshLocalizedContent()
        {
            ViewModel.RefreshLocalizedLabelsIfNeeded();
            XamlRuntimeLocalizer.Apply(this);
        }
    }
}
