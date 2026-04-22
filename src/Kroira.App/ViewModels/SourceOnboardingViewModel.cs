using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.ViewModels
{
    public partial class SourceOnboardingViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IEntitlementService _entitlementService;

        [ObservableProperty]
        private string _sourceName = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsM3U))]
        [NotifyPropertyChangedFor(nameof(M3UVisibility))]
        [NotifyPropertyChangedFor(nameof(XtreamVisibility))]
        [NotifyPropertyChangedFor(nameof(GuideModeSummaryText))]
        private int _selectedFormatIndex = 0;

        public bool IsM3U => SelectedFormatIndex == 0;
        public Microsoft.UI.Xaml.Visibility M3UVisibility => IsM3U ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility XtreamVisibility => !IsM3U ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        [ObservableProperty]
        private string _m3uUrlOrPath = string.Empty;

        [ObservableProperty]
        private string _xtreamUrl = string.Empty;

        [ObservableProperty]
        private string _xtreamUsername = string.Empty;

        [ObservableProperty]
        private string _xtreamPassword = string.Empty;

        [ObservableProperty]
        private string _manualEpgUrl = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProxyUrlVisibility))]
        [NotifyPropertyChangedFor(nameof(ProxySummaryText))]
        private int _selectedProxyModeIndex = 0;

        [ObservableProperty]
        private string _proxyUrl = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ManualEpgVisibility))]
        [NotifyPropertyChangedFor(nameof(GuideModeSummaryText))]
        private int _selectedEpgModeIndex = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasStatus))]
        [NotifyPropertyChangedFor(nameof(StatusVisibility))]
        private string _statusMessage = string.Empty;

        public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
        public Microsoft.UI.Xaml.Visibility StatusVisibility => HasStatus ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public bool CanSaveSource => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.SourcesAdd);
        public Microsoft.UI.Xaml.Visibility ManualEpgVisibility => SelectedEpgModeIndex == 1
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility ProxyUrlVisibility => SelectedProxyMode != SourceProxyScope.Disabled
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        public string GuideModeSummaryText => SelectedEpgModeIndex switch
        {
            1 => "Manual override uses your XMLTV URL and keeps any detected provider URL on file.",
            2 => "No guide disables XMLTV syncing for this source.",
            _ => IsM3U
                ? "Detected mode uses the XMLTV URL advertised by the playlist when one exists."
                : "Detected mode uses the XMLTV URL derived from your Xtream provider credentials."
        };
        public string ProxySummaryText => SelectedProxyMode switch
        {
            SourceProxyScope.PlaybackOnly => "Playback requests prefer the proxy while imports and guide sync stay direct.",
            SourceProxyScope.PlaybackAndProbing => "Playback and light probe traffic use the proxy; imports and guide sync stay direct.",
            SourceProxyScope.AllRequests => "Import, guide, probe, and playback requests all route through the proxy.",
            _ => "Direct routing keeps this source on its normal provider endpoints."
        };

        public SourceOnboardingViewModel(IServiceProvider serviceProvider, IEntitlementService entitlementService)
        {
            _serviceProvider = serviceProvider;
            _entitlementService = entitlementService;
        }

        [RelayCommand]
        public async Task SaveSourceAsync()
        {
            if (!CanSaveSource)
            {
                StatusMessage = "Adding sources is not available on this tier.";
                return;
            }

            if (IsM3U && string.IsNullOrWhiteSpace(M3uUrlOrPath))
            {
                StatusMessage = "Add an M3U URL or choose a local playlist file.";
                return;
            }

            if (!IsM3U && (string.IsNullOrWhiteSpace(XtreamUrl) || string.IsNullOrWhiteSpace(XtreamUsername) || string.IsNullOrWhiteSpace(XtreamPassword)))
            {
                StatusMessage = "Enter the Xtream server URL, username, and password.";
                return;
            }

            if (SelectedGuideMode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(ManualEpgUrl))
            {
                StatusMessage = "Add a manual XMLTV URL to use manual guide mode.";
                return;
            }

            if (SelectedProxyMode != SourceProxyScope.Disabled && string.IsNullOrWhiteSpace(ProxyUrl))
            {
                StatusMessage = "Add a proxy URL to use this routing policy.";
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sourceLimit = _entitlementService.GetLimit(EntitlementLimitKeys.SourcesMaxCount);
                if (sourceLimit.HasValue)
                {
                    var existingSourceCount = await db.SourceProfiles.CountAsync();
                    if (existingSourceCount >= sourceLimit.Value)
                    {
                        StatusMessage = $"This tier supports up to {sourceLimit.Value} source{(sourceLimit.Value == 1 ? string.Empty : "s")}.";
                        return;
                    }
                }

                var lifecycleService = scope.ServiceProvider.GetRequiredService<ISourceLifecycleService>();
                StatusMessage = "Saving your source and starting the first sync...";

                var result = await lifecycleService.CreateSourceAsync(new SourceCreateRequest
                {
                    Name = SourceName,
                    Type = IsM3U ? SourceType.M3U : SourceType.Xtream,
                    Url = IsM3U ? M3uUrlOrPath : XtreamUrl,
                    Username = IsM3U ? string.Empty : XtreamUsername,
                    Password = IsM3U ? string.Empty : XtreamPassword,
                    ManualEpgUrl = ManualEpgUrl,
                    EpgMode = SelectedGuideMode,
                    M3uImportMode = M3uImportMode.LiveMoviesAndSeries,
                    ProxyScope = SelectedProxyMode,
                    ProxyUrl = ProxyUrl
                });

                StatusMessage = result.Message;
                SourceName = string.Empty;
                M3uUrlOrPath = string.Empty;
                ManualEpgUrl = string.Empty;
                SelectedEpgModeIndex = 0;
                SelectedProxyModeIndex = 0;
                ProxyUrl = string.Empty;
                XtreamUrl = string.Empty;
                XtreamUsername = string.Empty;
                XtreamPassword = string.Empty;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not save this source: {ex.Message}";
            }
        }

        private EpgActiveMode SelectedGuideMode => SelectedEpgModeIndex switch
        {
            1 => EpgActiveMode.Manual,
            2 => EpgActiveMode.None,
            _ => EpgActiveMode.Detected
        };

        private SourceProxyScope SelectedProxyMode => SelectedProxyModeIndex switch
        {
            1 => SourceProxyScope.PlaybackOnly,
            2 => SourceProxyScope.PlaybackAndProbing,
            3 => SourceProxyScope.AllRequests,
            _ => SourceProxyScope.Disabled
        };

        [RelayCommand]
        public async Task BrowseLocalFileAsync()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var window = ((Kroira.App.App)Microsoft.UI.Xaml.Application.Current).MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add(".m3u");
            picker.FileTypeFilter.Add(".m3u8");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                M3uUrlOrPath = file.Path;
            }
        }
    }
}
