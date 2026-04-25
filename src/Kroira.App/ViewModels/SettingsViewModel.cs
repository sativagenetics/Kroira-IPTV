#nullable enable
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace Kroira.App.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IEntitlementService _entitlementService;
        private readonly IRemoteNavigationService _remoteNavigationService;
        private readonly IServiceProvider _serviceProvider;
        private bool _isLoadingLanguage;
        private bool _isLoadingAppearance;
        private bool _isLoadingAutoRefresh;
        private bool _isLoadingRemoteMode;

        public ObservableCollection<LanguageOptionViewModel> Languages { get; } = new();

        public ObservableCollection<AppAppearanceOptionViewModel> ThemeOptions { get; } = new();
        public ObservableCollection<AppAppearanceOptionViewModel> AccentOptions { get; } = new();
        public ObservableCollection<AutoRefreshIntervalOptionViewModel> AutoRefreshIntervalOptions { get; } = new();

        [ObservableProperty]
        private Visibility _freeTierVisibility;

        [ObservableProperty]
        private Visibility _proTierVisibility;

        [ObservableProperty]
        private string _licenseStatusDescription = string.Empty;

        [ObservableProperty]
        private LanguageOptionViewModel _selectedLanguage = new LanguageOptionViewModel(AppLanguageService.SystemDefaultLanguageCode, string.Empty);

        [ObservableProperty]
        private string _languageStatusText = LocalizedStrings.Get("Settings_Language_Status_Initial");

        [ObservableProperty]
        private AppAppearanceOptionViewModel? _selectedThemeOption;

        [ObservableProperty]
        private AppAppearanceOptionViewModel? _selectedAccentOption;

        [ObservableProperty]
        private string _appearanceStatusText = LocalizedStrings.Get("Settings_Appearance_Status_Initial");

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBackupIdle))]
        [NotifyPropertyChangedFor(nameof(CanStartBackupAction))]
        private bool _isBackupBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBackupStatus))]
        private string _backupStatusText = LocalizedStrings.Get("Settings_Backup_Status_Initial");

        [ObservableProperty]
        private string _resourceStatusText = LocalizedStrings.Get("Settings_Resources_Status_Ready");

        [ObservableProperty]
        private bool _autoRefreshEnabled = true;

        [ObservableProperty]
        private bool _runAutoRefreshAfterLaunch = true;

        [ObservableProperty]
        private AutoRefreshIntervalOptionViewModel? _selectedAutoRefreshInterval;

        [ObservableProperty]
        private string _autoRefreshStatusText = LocalizedStrings.Get("Settings_AutoRefresh_Status_Initial");

        [ObservableProperty]
        private bool _remoteModeEnabled = true;

        [ObservableProperty]
        private string _remoteModeStatusText = LocalizedStrings.Get("Settings_RemoteMode_Status_Initial");

        public bool IsBackupIdle => !IsBackupBusy;
        public bool CanUseBackupRestore => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.LibraryBackupRestore);
        public bool CanUseThemePresets => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.AppearanceThemes);
        public bool CanUseAccentPacks => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.AppearanceAccentPacks);
        public bool CanStartBackupAction => CanUseBackupRestore && !IsBackupBusy;
        public bool HasBackupStatus => !string.IsNullOrWhiteSpace(BackupStatusText);
        public string AppName => AppSubmissionInfo.AppName;
        public string ReleaseVersionText => $"Release {AppSubmissionInfo.ReleaseVersion}";
        public string ShortDescription => AppSubmissionInfo.ShortDescription;
        public string ProductDescription => AppSubmissionInfo.ProductDescription;
        public string HelpStepOne => AppSubmissionInfo.HelpStepOne;
        public string HelpStepTwo => AppSubmissionInfo.HelpStepTwo;
        public string HelpStepThree => AppSubmissionInfo.HelpStepThree;
        public string HelpStepFour => AppSubmissionInfo.HelpStepFour;
        public string PrivacySummaryText => AppSubmissionInfo.PrivacySummary;
        public string CredentialHandlingText => AppSubmissionInfo.CredentialHandlingSummary;
        public string SanitizedLogsText => AppSubmissionInfo.SanitizedLogsSummary;
        public string TelemetryText => AppSubmissionInfo.TelemetrySummary;
        public string MetadataProviderText => AppSubmissionInfo.MetadataProviderSummary;
        public string PrivacyPolicyDisplayText => AppSubmissionInfo.PrivacyPolicyDisplayText;
        public bool CanOpenPrivacyPolicy => AppSubmissionInfo.HasPrivacyPolicyUrl;
        public string SupportPageDisplayText => AppSubmissionInfo.SupportPageDisplayText;
        public bool CanOpenSupportPage => AppSubmissionInfo.HasSupportPageUrl;
        public string SupportEmailText => AppSubmissionInfo.SupportEmailDisplayText;
        public bool CanEmailSupport => AppSubmissionInfo.HasSupportEmail;
        public string SupportSummaryText => AppSubmissionInfo.SupportSummary;
        public string SupportAuthenticationFailureText => AppSubmissionInfo.SupportAuthenticationFailure;
        public string SupportNoChannelsText => AppSubmissionInfo.SupportNoChannels;
        public string SupportNoEpgText => AppSubmissionInfo.SupportNoEpg;
        public string SupportWrongEpgText => AppSubmissionInfo.SupportWrongEpg;
        public string SupportStreamDoesNotPlayText => AppSubmissionInfo.SupportStreamDoesNotPlay;
        public string SupportStoreInstallText => AppSubmissionInfo.SupportStoreInstall;
        public string SupportResetAppDataText => AppSubmissionInfo.SupportResetAppData;
        public string SupportExportDiagnosticsText => AppSubmissionInfo.SupportExportDiagnostics;
        public string LegalDisclaimerText => AppSubmissionInfo.LegalDisclaimer;
        public string RunFullTrustJustificationText => AppSubmissionInfo.RunFullTrustJustification;

        partial void OnSelectedLanguageChanged(LanguageOptionViewModel value)
        {
            if (!_isLoadingLanguage && value != null)
            {
                _ = SaveLanguageAsync(value.Code);
            }
        }

        partial void OnSelectedThemeOptionChanged(AppAppearanceOptionViewModel? value)
        {
            if (!_isLoadingAppearance && value != null)
            {
                _ = SaveAppearanceAsync();
            }
        }

        partial void OnSelectedAccentOptionChanged(AppAppearanceOptionViewModel? value)
        {
            if (!_isLoadingAppearance && value != null)
            {
                _ = SaveAppearanceAsync();
            }
        }

        partial void OnAutoRefreshEnabledChanged(bool value)
        {
            if (!_isLoadingAutoRefresh)
            {
                _ = SaveAutoRefreshAsync();
            }
        }

        partial void OnRunAutoRefreshAfterLaunchChanged(bool value)
        {
            if (!_isLoadingAutoRefresh)
            {
                _ = SaveAutoRefreshAsync();
            }
        }

        partial void OnSelectedAutoRefreshIntervalChanged(AutoRefreshIntervalOptionViewModel? value)
        {
            if (!_isLoadingAutoRefresh && value != null)
            {
                _ = SaveAutoRefreshAsync();
            }
        }

        partial void OnRemoteModeEnabledChanged(bool value)
        {
            if (!_isLoadingRemoteMode)
            {
                _ = SaveRemoteModeAsync();
            }
        }

        public SettingsViewModel(
            IEntitlementService entitlementService,
            IRemoteNavigationService remoteNavigationService,
            IServiceProvider serviceProvider)
        {
            _entitlementService = entitlementService;
            _remoteNavigationService = remoteNavigationService;
            _serviceProvider = serviceProvider;
            RefreshLocalizedOptionCollections();
            UpdateState();
        }

        [RelayCommand]
        public async Task LoadSettingsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var appearanceService = scope.ServiceProvider.GetRequiredService<IAppAppearanceService>();
            var autoRefreshService = scope.ServiceProvider.GetRequiredService<ISourceAutoRefreshService>();
            var activeProfile = await profileService.GetActiveProfileAsync(db);
            var languageCode = await AppLanguageService.GetLanguageAsync(db, activeProfile.Id);
            await AppLanguageService.SetLanguageAsync(db, languageCode, activeProfile.Id);
            var appearance = await appearanceService.LoadAsync(db);
            var autoRefresh = await autoRefreshService.LoadSettingsAsync(db);
            await _remoteNavigationService.InitializeAsync();

            RefreshLocalizedOptionCollections(
                languageCode,
                appearance.ThemePresetKey,
                appearance.AccentPresetKey,
                autoRefresh.IntervalHours);
            LanguageStatusText = string.Equals(languageCode, AppLanguageService.SystemDefaultLanguageCode, StringComparison.OrdinalIgnoreCase)
                ? LocalizedStrings.Get("Settings_Language_Status_System")
                : LocalizedStrings.Format("Settings_Language_Status_Selected", SelectedLanguage.DisplayName);

            AppearanceStatusText = LocalizedStrings.Format(
                "Settings_Appearance_Status_Active",
                SelectedThemeOption?.DisplayName ?? LocalizedStrings.Get("Appearance_Theme_Cinema_Name"),
                SelectedAccentOption?.DisplayName ?? LocalizedStrings.Get("Appearance_Accent_Gold_Name"));

            _isLoadingAutoRefresh = true;
            AutoRefreshEnabled = autoRefresh.IsEnabled;
            RunAutoRefreshAfterLaunch = autoRefresh.RunAfterLaunch;
            _isLoadingAutoRefresh = false;
            AutoRefreshStatusText = autoRefresh.IsEnabled
                ? LocalizedStrings.Format("Settings_AutoRefresh_Status_Running", autoRefresh.IntervalHours)
                : LocalizedStrings.Get("Settings_AutoRefresh_Status_Off");

            _isLoadingRemoteMode = true;
            RemoteModeEnabled = _remoteNavigationService.IsRemoteModeEnabled;
            _isLoadingRemoteMode = false;
            RemoteModeStatusText = RemoteModeEnabled
                ? LocalizedStrings.Get("Settings_RemoteMode_Status_On")
                : LocalizedStrings.Get("Settings_RemoteMode_Status_Off");
        }

        private async Task SaveLanguageAsync(string languageCode)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfile = await profileService.GetActiveProfileAsync(db);
            await AppLanguageService.SetLanguageAsync(db, languageCode, activeProfile.Id);
            RefreshLocalizedOptionCollections(
                languageCode,
                SelectedThemeOption?.Key,
                SelectedAccentOption?.Key,
                SelectedAutoRefreshInterval?.Hours);
            RefreshLocalizedStaticProperties();
            UpdateState();
            LanguageStatusText = LocalizedStrings.Get("Settings_Language_Status_RestartRequired");
            AppearanceStatusText = LocalizedStrings.Format(
                "Settings_Appearance_Status_Active",
                SelectedThemeOption?.DisplayName ?? LocalizedStrings.Get("Appearance_Theme_Cinema_Name"),
                SelectedAccentOption?.DisplayName ?? LocalizedStrings.Get("Appearance_Accent_Gold_Name"));
            AutoRefreshStatusText = AutoRefreshEnabled
                ? LocalizedStrings.Format("Settings_AutoRefresh_Status_Running", SelectedAutoRefreshInterval?.Hours ?? 6)
                : LocalizedStrings.Get("Settings_AutoRefresh_Status_Off");
            RemoteModeStatusText = RemoteModeEnabled
                ? LocalizedStrings.Get("Settings_RemoteMode_Status_On")
                : LocalizedStrings.Get("Settings_RemoteMode_Status_Off");
        }

        private async Task SaveAppearanceAsync()
        {
            if ((SelectedThemeOption == null && SelectedAccentOption == null) ||
                (!CanUseThemePresets && !CanUseAccentPacks))
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appearanceService = scope.ServiceProvider.GetRequiredService<IAppAppearanceService>();
            var current = await appearanceService.LoadAsync(db);
            current.ThemePresetKey = CanUseThemePresets ? SelectedThemeOption?.Key ?? current.ThemePresetKey : current.ThemePresetKey;
            current.AccentPresetKey = CanUseAccentPacks ? SelectedAccentOption?.Key ?? current.AccentPresetKey : current.AccentPresetKey;
            await appearanceService.SaveAsync(db, current);

            AppearanceStatusText = LocalizedStrings.Format(
                "Settings_Appearance_Status_Active",
                SelectedThemeOption?.DisplayName ?? LocalizedStrings.Get("Appearance_Theme_Cinema_Name"),
                SelectedAccentOption?.DisplayName ?? LocalizedStrings.Get("Appearance_Accent_Gold_Name"));
        }

        private async Task SaveAutoRefreshAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var autoRefreshService = scope.ServiceProvider.GetRequiredService<ISourceAutoRefreshService>();
            var settings = new SourceAutoRefreshSettings
            {
                IsEnabled = AutoRefreshEnabled,
                RunAfterLaunch = RunAutoRefreshAfterLaunch,
                IntervalHours = SelectedAutoRefreshInterval?.Hours ?? 6
            };
            await autoRefreshService.SaveSettingsAsync(db, settings);
            AutoRefreshStatusText = settings.IsEnabled
                ? LocalizedStrings.Format("Settings_AutoRefresh_Status_Running", settings.IntervalHours)
                : LocalizedStrings.Get("Settings_AutoRefresh_Status_Off");
        }

        private async Task SaveRemoteModeAsync()
        {
            await _remoteNavigationService.SetRemoteModeEnabledAsync(RemoteModeEnabled);
            RemoteModeStatusText = RemoteModeEnabled
                ? LocalizedStrings.Get("Settings_RemoteMode_Status_On")
                : LocalizedStrings.Get("Settings_RemoteMode_Status_Off");
        }

        private void RefreshLocalizedOptionCollections(
            string? selectedLanguageCode = null,
            string? selectedThemeKey = null,
            string? selectedAccentKey = null,
            int? selectedAutoRefreshHours = null)
        {
            selectedLanguageCode ??= SelectedLanguage?.Code ?? AppLanguageService.SystemDefaultLanguageCode;
            selectedThemeKey ??= SelectedThemeOption?.Key;
            selectedAccentKey ??= SelectedAccentOption?.Key;
            selectedAutoRefreshHours ??= SelectedAutoRefreshInterval?.Hours ?? 6;

            var wasLoadingLanguage = _isLoadingLanguage;
            var wasLoadingAppearance = _isLoadingAppearance;
            var wasLoadingAutoRefresh = _isLoadingAutoRefresh;
            _isLoadingLanguage = true;
            _isLoadingAppearance = true;
            _isLoadingAutoRefresh = true;
            try
            {
                Languages.Clear();
                foreach (var language in AppLanguageService.SupportedLanguages)
                {
                    Languages.Add(new LanguageOptionViewModel(
                        language.Code,
                        LocalizedStrings.Get(language.DisplayNameResourceKey)));
                }

                var normalizedLanguage = AppLanguageService.NormalizeLanguageCode(selectedLanguageCode);
                SelectedLanguage = Languages.FirstOrDefault(language => string.Equals(language.Code, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
                    ?? Languages.First(language => language.Code == AppLanguageService.SystemDefaultLanguageCode);

                ThemeOptions.Clear();
                var appearanceService = _serviceProvider.GetRequiredService<IAppAppearanceService>();
                foreach (var option in appearanceService.ThemeOptions)
                {
                    ThemeOptions.Add(new AppAppearanceOptionViewModel(option.Key, option.DisplayName, option.Description));
                }

                AccentOptions.Clear();
                foreach (var option in appearanceService.AccentOptions)
                {
                    AccentOptions.Add(new AppAppearanceOptionViewModel(option.Key, option.DisplayName, option.Description));
                }

                SelectedThemeOption = ThemeOptions.FirstOrDefault(option => string.Equals(option.Key, selectedThemeKey, StringComparison.OrdinalIgnoreCase))
                    ?? ThemeOptions.FirstOrDefault();
                SelectedAccentOption = AccentOptions.FirstOrDefault(option => string.Equals(option.Key, selectedAccentKey, StringComparison.OrdinalIgnoreCase))
                    ?? AccentOptions.FirstOrDefault();

                AutoRefreshIntervalOptions.Clear();
                foreach (var hours in new[] { 1, 3, 6, 12, 24 })
                {
                    AutoRefreshIntervalOptions.Add(new AutoRefreshIntervalOptionViewModel(hours));
                }

                SelectedAutoRefreshInterval = AutoRefreshIntervalOptions.FirstOrDefault(option => option.Hours == selectedAutoRefreshHours)
                    ?? AutoRefreshIntervalOptions.FirstOrDefault();
            }
            finally
            {
                _isLoadingLanguage = wasLoadingLanguage;
                _isLoadingAppearance = wasLoadingAppearance;
                _isLoadingAutoRefresh = wasLoadingAutoRefresh;
            }
        }

        private void RefreshLocalizedStaticProperties()
        {
            foreach (var propertyName in new[]
            {
                nameof(ShortDescription),
                nameof(ProductDescription),
                nameof(HelpStepOne),
                nameof(HelpStepTwo),
                nameof(HelpStepThree),
                nameof(HelpStepFour),
                nameof(PrivacySummaryText),
                nameof(CredentialHandlingText),
                nameof(SanitizedLogsText),
                nameof(TelemetryText),
                nameof(MetadataProviderText),
                nameof(PrivacyPolicyDisplayText),
                nameof(SupportPageDisplayText),
                nameof(SupportEmailText),
                nameof(SupportSummaryText),
                nameof(SupportAuthenticationFailureText),
                nameof(SupportNoChannelsText),
                nameof(SupportNoEpgText),
                nameof(SupportWrongEpgText),
                nameof(SupportStreamDoesNotPlayText),
                nameof(SupportStoreInstallText),
                nameof(SupportResetAppDataText),
                nameof(SupportExportDiagnosticsText),
                nameof(LegalDisclaimerText),
                nameof(RunFullTrustJustificationText)
            })
            {
                OnPropertyChanged(propertyName);
            }
        }

        [RelayCommand]
        private async Task UpgradeAsync()
        {
            bool success = await _entitlementService.PurchaseProLicenseAsync();
            if (success)
            {
                UpdateState();
            }
        }

        private void UpdateState()
        {
            bool isPro = string.Equals(_entitlementService.CurrentTierKey, "pro", StringComparison.OrdinalIgnoreCase);
            ProTierVisibility = isPro ? Visibility.Visible : Visibility.Collapsed;
            FreeTierVisibility = !isPro ? Visibility.Visible : Visibility.Collapsed;

            var backupRestoreState = CanUseBackupRestore ? LocalizedStrings.Get("General_Available") : LocalizedStrings.Get("General_NotAvailable");
            var appearanceState = CanUseThemePresets || CanUseAccentPacks ? LocalizedStrings.Get("General_Available") : LocalizedStrings.Get("General_NotAvailable");
            LicenseStatusDescription = LocalizedStrings.Format(
                "Settings_License_Status",
                _entitlementService.CurrentTierDisplayName,
                backupRestoreState,
                appearanceState);
        }

        public async Task ExportBackupAsync(string filePath)
        {
            LogBackup($"export command entered path='{filePath}' busy={IsBackupBusy}");

            if (!CanUseBackupRestore)
            {
                BackupStatusText = LocalizedStrings.Get("Settings_Backup_Export_NotAvailable");
                LogBackup("export command denied by entitlement");
                return;
            }

            if (IsBackupBusy)
            {
                LogBackup("export command ignored because backup is already busy");
                return;
            }

            IsBackupBusy = true;
            BackupStatusText = LocalizedStrings.Get("Settings_Backup_Exporting");
            LogBackup("export status set busy");

            try
            {
                var result = await Task.Run(async () =>
                {
                    LogBackup("export background task started");
                    using var scope = _serviceProvider.CreateScope();
                    var backupService = scope.ServiceProvider.GetRequiredService<IBackupPackageService>();
                    LogBackup("export service resolved on background thread");
                    return await backupService.ExportAsync(filePath);
                });
                LogBackup($"export background task completed file='{result.FilePath}' sources={result.SourceCount} profiles={result.ProfileCount} favorites={result.FavoriteCount} watch={result.WatchStateCount}");

                BackupStatusText =
                    LocalizedStrings.Format(
                        "Settings_Backup_Export_Success",
                        result.SourceCount,
                        result.ProfileCount,
                        result.FavoriteCount,
                        result.WatchStateCount);
                LogBackup("export status updated success");
            }
            catch (Exception ex)
            {
                TryDeleteEmptyBackupFile(filePath);
                BackupStatusText = LocalizedStrings.Format("Settings_Backup_Export_Failed", ex.Message);
                LogBackup($"export failed type={ex.GetType().Name} message='{ex.Message}'");
            }
            finally
            {
                IsBackupBusy = false;
                LogBackup("export status cleared busy");
            }
        }

        public async Task RestoreBackupAsync(string filePath)
        {
            if (!CanUseBackupRestore)
            {
                BackupStatusText = LocalizedStrings.Get("Settings_Backup_Restore_NotAvailable");
                return;
            }

            if (IsBackupBusy)
            {
                return;
            }

            IsBackupBusy = true;
            BackupStatusText = LocalizedStrings.Get("Settings_Backup_Restoring");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IBackupPackageService>();
                var result = await backupService.RestoreAsync(filePath);
                await LoadSettingsAsync();

                var builder = new StringBuilder();
                builder.Append(
                    LocalizedStrings.Format(
                        "Settings_Backup_Restore_Success",
                        result.SourceCount,
                        result.ProfileCount,
                        result.FavoriteCount,
                        result.WatchStateCount));

                if (result.SourceSyncFailureCount > 0)
                {
                    builder.Append(' ');
                    builder.Append(LocalizedStrings.Format("Settings_Backup_Restore_SourceFailures", result.SourceSyncFailureCount));
                }

                if (result.Warnings.Count > 0)
                {
                    builder.Append($" {result.Warnings[0]}");
                }

                BackupStatusText = builder.ToString();
            }
            catch (Exception ex)
            {
                BackupStatusText = LocalizedStrings.Format("Settings_Backup_Restore_Failed", ex.Message);
            }
            finally
            {
                IsBackupBusy = false;
            }
        }

        public string AppVersionBuildText
        {
            get
            {
                try
                {
                    var version = Package.Current.Id.Version;
                    return $"{version.Major}.{version.Minor}.{version.Build} (Build {version.Revision})";
                }
                catch
                {
                    var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return ver != null
                        ? $"{ver.Major}.{ver.Minor}.{ver.Build} (Build {ver.Revision}, unpackaged)"
                        : "1.0.0 (Build 0)";
                }
            }
        }

        [RelayCommand]
        public async Task OpenPrivacyPolicyAsync()
        {
            await OpenExternalUriAsync(
                AppSubmissionInfo.TryCreatePrivacyPolicyUri(out var uri) ? uri : null,
                LocalizedStrings.Get("Settings_Privacy_Missing"),
                LocalizedStrings.Get("Settings_Privacy_OpenFailed"));
        }

        [RelayCommand]
        public async Task OpenSupportAsync()
        {
            await OpenExternalUriAsync(
                AppSubmissionInfo.TryCreateSupportPageUri(out var uri) ? uri : null,
                LocalizedStrings.Get("Settings_Support_Missing"),
                LocalizedStrings.Get("Settings_Support_OpenFailed"));
        }

        [RelayCommand]
        public async Task OpenSupportEmailAsync()
        {
            await OpenExternalUriAsync(
                AppSubmissionInfo.TryCreateSupportEmailUri(out var uri) ? uri : null,
                LocalizedStrings.Get("Settings_SupportEmail_Missing"),
                LocalizedStrings.Get("Settings_SupportEmail_OpenFailed"));
        }

        private void LogBackup(string message)
        {
            BackupRuntimeLogger.Log("SETTINGS EXPORT", message);
        }

        private async Task OpenExternalUriAsync(Uri? uri, string missingMessage, string failureMessage)
        {
            if (uri == null)
            {
                ResourceStatusText = missingMessage;
                return;
            }

            try
            {
                var launched = await Windows.System.Launcher.LaunchUriAsync(uri);
                ResourceStatusText = launched
                    ? LocalizedStrings.Get("Settings_Resources_Status_Ready")
                    : failureMessage;
            }
            catch
            {
                ResourceStatusText = failureMessage;
            }
        }

        private void TryDeleteEmptyBackupFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var info = new FileInfo(filePath);
                    if (info.Length == 0)
                    {
                        File.Delete(filePath);
                        LogBackup($"deleted empty backup file path='{filePath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                LogBackup($"failed to delete empty backup file path='{filePath}' message='{ex.Message}'");
            }
        }
    }

    public sealed class LanguageOptionViewModel
    {
        public LanguageOptionViewModel(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public string Code { get; }
        public string DisplayName { get; }
    }

    public sealed class AppAppearanceOptionViewModel
    {
        public AppAppearanceOptionViewModel(string key, string displayName, string description)
        {
            Key = key;
            DisplayName = displayName;
            Description = description;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
    }

    public sealed class AutoRefreshIntervalOptionViewModel
    {
        public AutoRefreshIntervalOptionViewModel(int hours)
        {
            Hours = hours;
        }

        public int Hours { get; }
        public string DisplayName => Hours == 1
            ? LocalizedStrings.Get("Settings_AutoRefresh_EveryHour")
            : LocalizedStrings.Format("Settings_AutoRefresh_EveryHours", Hours);
    }
}
