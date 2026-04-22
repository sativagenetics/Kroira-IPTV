#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Controls;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.ViewModels
{
    public sealed class SourceSetupCapabilityItemViewModel
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public StatusPillKind Kind { get; set; } = StatusPillKind.Neutral;
    }

    public sealed class SourceSetupIssueItemViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public StatusPillKind Kind { get; set; } = StatusPillKind.Neutral;
    }

    public partial class SourceOnboardingViewModel
    {
        private string _lastValidationSignature = string.Empty;
        private SourceSetupValidationSnapshot? _lastValidationSnapshot;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanTestSource))]
        private bool _isTestingSource;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasValidation))]
        [NotifyPropertyChangedFor(nameof(ValidationVisibility))]
        [NotifyPropertyChangedFor(nameof(HasValidationSafeReport))]
        private string _validationHeadlineText = string.Empty;

        [ObservableProperty]
        private string _validationSummaryText = string.Empty;

        [ObservableProperty]
        private string _validationConnectionText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ValidationTypeHintVisibility))]
        private string _validationTypeHintText = string.Empty;

        [ObservableProperty]
        private string _validationCapabilitySummaryText = string.Empty;

        [ObservableProperty]
        private StatusPillKind _validationStatusKind = StatusPillKind.Neutral;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ValidationCapabilitiesVisibility))]
        private ObservableCollection<SourceSetupCapabilityItemViewModel> _validationCapabilities = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ValidationIssuesVisibility))]
        private ObservableCollection<SourceSetupIssueItemViewModel> _validationIssues = new();

        public bool CanTestSource => !IsTestingSource;
        public bool HasValidation => !string.IsNullOrWhiteSpace(ValidationHeadlineText);
        public bool HasValidationSafeReport => !string.IsNullOrWhiteSpace(_lastValidationSnapshot?.SafeReportText);
        public Microsoft.UI.Xaml.Visibility ValidationVisibility => HasValidation ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility ValidationCapabilitiesVisibility => ValidationCapabilities.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility ValidationIssuesVisibility => ValidationIssues.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility ValidationTypeHintVisibility => string.IsNullOrWhiteSpace(ValidationTypeHintText) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        public string GetSafeValidationReport()
        {
            return _lastValidationSnapshot?.SafeReportText ?? string.Empty;
        }

        [RelayCommand]
        public async Task ValidateSourceAsync()
        {
            await ValidateCurrentDraftAsync(force: true);
        }

        private async Task<SourceSetupValidationSnapshot> ValidateCurrentDraftAsync(bool force)
        {
            var signature = BuildValidationSignature();
            if (!force &&
                _lastValidationSnapshot != null &&
                string.Equals(_lastValidationSignature, signature, StringComparison.Ordinal))
            {
                return _lastValidationSnapshot;
            }

            IsTestingSource = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var guidanceService = scope.ServiceProvider.GetRequiredService<ISourceGuidanceService>();
                var snapshot = await guidanceService.ValidateDraftAsync(BuildSetupDraft());
                ApplyValidationSnapshot(snapshot);
                _lastValidationSignature = signature;
                _lastValidationSnapshot = snapshot;
                StatusMessage = snapshot.CanSave
                    ? "Connection check completed. Capability preview is ready."
                    : snapshot.SummaryText;
                return snapshot;
            }
            catch (Exception ex)
            {
                ClearValidationSnapshot();
                StatusMessage = $"Could not test this source: {ex.Message}";
                throw;
            }
            finally
            {
                IsTestingSource = false;
            }
        }

        private void ApplyValidationSnapshot(SourceSetupValidationSnapshot snapshot)
        {
            ValidationHeadlineText = snapshot.HeadlineText;
            ValidationSummaryText = snapshot.SummaryText;
            ValidationConnectionText = snapshot.ConnectionText;
            ValidationTypeHintText = snapshot.TypeHintText;
            ValidationCapabilitySummaryText = snapshot.CapabilitySummaryText;
            ValidationStatusKind = snapshot.CanSave
                ? StatusPillKind.Healthy
                : snapshot.Issues.Any(issue => issue.Tone == SourceActivityTone.Failed)
                    ? StatusPillKind.Failed
                    : StatusPillKind.Warning;

            ValidationCapabilities = new ObservableCollection<SourceSetupCapabilityItemViewModel>(
                snapshot.Capabilities.Select(capability => new SourceSetupCapabilityItemViewModel
                {
                    Label = capability.Label,
                    Value = capability.Value,
                    Detail = capability.Detail,
                    Kind = MapTone(capability.Tone)
                }));

            ValidationIssues = new ObservableCollection<SourceSetupIssueItemViewModel>(
                snapshot.Issues.Select(issue => new SourceSetupIssueItemViewModel
                {
                    Title = issue.Title,
                    Detail = issue.Detail,
                    Kind = MapTone(issue.Tone)
                }));
        }

        private void ClearValidationSnapshot()
        {
            _lastValidationSignature = string.Empty;
            _lastValidationSnapshot = null;
            ValidationHeadlineText = string.Empty;
            ValidationSummaryText = string.Empty;
            ValidationConnectionText = string.Empty;
            ValidationTypeHintText = string.Empty;
            ValidationCapabilitySummaryText = string.Empty;
            ValidationStatusKind = StatusPillKind.Neutral;
            ValidationCapabilities = new ObservableCollection<SourceSetupCapabilityItemViewModel>();
            ValidationIssues = new ObservableCollection<SourceSetupIssueItemViewModel>();
        }

        private SourceSetupDraft BuildSetupDraft()
        {
            return new SourceSetupDraft
            {
                Name = SourceName,
                Type = IsM3U ? SourceType.M3U : IsXtream ? SourceType.Xtream : SourceType.Stalker,
                Url = IsM3U ? M3uUrlOrPath : IsXtream ? XtreamUrl : StalkerPortalUrl,
                Username = XtreamUsername,
                Password = XtreamPassword,
                ManualEpgUrl = ManualEpgUrl,
                EpgMode = SelectedGuideMode,
                ProxyScope = SelectedProxyMode,
                ProxyUrl = ProxyUrl,
                CompanionScope = SelectedCompanionScope,
                CompanionMode = SelectedCompanionMode,
                CompanionUrl = CompanionUrl,
                StalkerMacAddress = StalkerMacAddress,
                StalkerDeviceId = StalkerDeviceId,
                StalkerSerialNumber = StalkerSerialNumber,
                StalkerTimezone = StalkerTimezone,
                StalkerLocale = StalkerLocale
            };
        }

        private string BuildValidationSignature()
        {
            return string.Join(
                "|",
                SelectedFormatIndex,
                SourceName?.Trim() ?? string.Empty,
                M3uUrlOrPath?.Trim() ?? string.Empty,
                XtreamUrl?.Trim() ?? string.Empty,
                XtreamUsername?.Trim() ?? string.Empty,
                XtreamPassword ?? string.Empty,
                StalkerPortalUrl?.Trim() ?? string.Empty,
                StalkerMacAddress?.Trim() ?? string.Empty,
                StalkerDeviceId?.Trim() ?? string.Empty,
                StalkerSerialNumber?.Trim() ?? string.Empty,
                StalkerTimezone?.Trim() ?? string.Empty,
                StalkerLocale?.Trim() ?? string.Empty,
                SelectedEpgModeIndex,
                ManualEpgUrl?.Trim() ?? string.Empty,
                SelectedProxyModeIndex,
                ProxyUrl?.Trim() ?? string.Empty,
                SelectedCompanionScopeIndex,
                SelectedCompanionModeIndex,
                CompanionUrl?.Trim() ?? string.Empty);
        }

        private static StatusPillKind MapTone(SourceActivityTone tone)
        {
            return tone switch
            {
                SourceActivityTone.Healthy => StatusPillKind.Healthy,
                SourceActivityTone.Warning => StatusPillKind.Warning,
                SourceActivityTone.Failed => StatusPillKind.Failed,
                SourceActivityTone.Syncing => StatusPillKind.Syncing,
                _ => StatusPillKind.Neutral
            };
        }

        partial void OnSelectedFormatIndexChanged(int value) => ClearValidationSnapshot();
        partial void OnM3uUrlOrPathChanged(string value) => ClearValidationSnapshot();
        partial void OnXtreamUrlChanged(string value) => ClearValidationSnapshot();
        partial void OnXtreamUsernameChanged(string value) => ClearValidationSnapshot();
        partial void OnXtreamPasswordChanged(string value) => ClearValidationSnapshot();
        partial void OnStalkerPortalUrlChanged(string value) => ClearValidationSnapshot();
        partial void OnStalkerMacAddressChanged(string value) => ClearValidationSnapshot();
        partial void OnStalkerDeviceIdChanged(string value) => ClearValidationSnapshot();
        partial void OnStalkerSerialNumberChanged(string value) => ClearValidationSnapshot();
        partial void OnStalkerTimezoneChanged(string value) => ClearValidationSnapshot();
        partial void OnStalkerLocaleChanged(string value) => ClearValidationSnapshot();
        partial void OnManualEpgUrlChanged(string value) => ClearValidationSnapshot();
        partial void OnSelectedProxyModeIndexChanged(int value) => ClearValidationSnapshot();
        partial void OnProxyUrlChanged(string value) => ClearValidationSnapshot();
        partial void OnSelectedCompanionScopeIndexChanged(int value) => ClearValidationSnapshot();
        partial void OnSelectedCompanionModeIndexChanged(int value) => ClearValidationSnapshot();
        partial void OnCompanionUrlChanged(string value) => ClearValidationSnapshot();
        partial void OnSelectedEpgModeIndexChanged(int value) => ClearValidationSnapshot();
    }
}
