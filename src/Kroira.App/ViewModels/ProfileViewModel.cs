#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public sealed partial class ProfileLockOptionViewModel : ObservableObject
    {
        private readonly Action<ProfileLockOptionViewModel, bool>? _onChanged;

        public ProfileLockOptionViewModel(string key, string label, string detail, bool isLocked, Action<ProfileLockOptionViewModel, bool>? onChanged)
        {
            Key = key;
            Label = label;
            Detail = detail;
            _isLocked = isLocked;
            _onChanged = onChanged;
        }

        public string Key { get; }
        public string Label { get; }
        public string Detail { get; }

        [ObservableProperty]
        private bool _isLocked;

        partial void OnIsLockedChanged(bool value)
        {
            _onChanged?.Invoke(this, value);
        }
    }

    public partial class ProfileViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IEntitlementService _entitlementService;
        private bool _isLoading;
        private bool _isSavingLocks;
        private int? _profileLimit;

        public ObservableCollection<AppProfile> Profiles { get; } = new();
        public ObservableCollection<ProfileLockOptionViewModel> SourceLocks { get; } = new();
        public ObservableCollection<ProfileLockOptionViewModel> CategoryLocks { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProfileSwitcherVisibility))]
        [NotifyPropertyChangedFor(nameof(ProfileCountText))]
        [NotifyPropertyChangedFor(nameof(CanDeleteSelectedProfile))]
        [NotifyPropertyChangedFor(nameof(ActiveProfileName))]
        [NotifyPropertyChangedFor(nameof(SelectedProfileTypeText))]
        private AppProfile? _selectedProfile;

        [ObservableProperty]
        private string _editableProfileName = string.Empty;

        [ObservableProperty]
        private bool _isKidsSafeMode;

        [ObservableProperty]
        private bool _hideLockedContent = true;

        [ObservableProperty]
        private string _pinDraft = string.Empty;

        [ObservableProperty]
        private string _unlockPinDraft = string.Empty;

        [ObservableProperty]
        private string _pinStatusText = "No PIN set.";

        [ObservableProperty]
        private string _accessStatusText = "All imported content is visible for this profile.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProfileActionStatusVisibility))]
        private string _profileActionStatusText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SetPinVisibility))]
        [NotifyPropertyChangedFor(nameof(ClearPinVisibility))]
        [NotifyPropertyChangedFor(nameof(UnlockVisibility))]
        [NotifyPropertyChangedFor(nameof(RelockVisibility))]
        private bool _hasPin;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UnlockVisibility))]
        [NotifyPropertyChangedFor(nameof(RelockVisibility))]
        private bool _isLockedContentUnlocked;

        public Visibility ProfileSwitcherVisibility => Profiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        public string ProfileCountText => Profiles.Count == 1 ? "1 profile" : $"{Profiles.Count} profiles";
        public Visibility SetPinVisibility => HasPin ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ClearPinVisibility => HasPin ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UnlockVisibility => HasPin && !IsLockedContentUnlocked ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RelockVisibility => HasPin && IsLockedContentUnlocked ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SourceLocksListVisibility => SourceLocks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SourceLocksEmptyVisibility => SourceLocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CategoryLocksListVisibility => CategoryLocks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CategoryLocksEmptyVisibility => CategoryLocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProfileActionStatusVisibility => string.IsNullOrWhiteSpace(ProfileActionStatusText) ? Visibility.Collapsed : Visibility.Visible;
        public string SourceLocksSummaryText => SourceLocks.Count == 1 ? "1 source can be restricted." : $"{SourceLocks.Count} sources can be restricted.";
        public string CategoryLocksSummaryText => CategoryLocks.Count == 1 ? "1 category can be restricted." : $"{CategoryLocks.Count} categories can be restricted.";
        public string ActiveProfileName => SelectedProfile?.Name ?? "Profile";
        public string SelectedProfileTypeText => SelectedProfile?.IsKidsProfile == true ? "Kids profile" : "Standard local profile";
        public string LocalProfileExplanationText => "Profiles are local to this Windows device. They separate favorites, resume state, PIN locks, and content access without adding online sign-in.";
        public bool CanDeleteSelectedProfile => SelectedProfile != null && Profiles.Count > 1;
        public bool CanAddProfiles => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.ProfilesMultiple) &&
                                      (!_profileLimit.HasValue || Profiles.Count < _profileLimit.Value);
        public bool CanManageParentalControls => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.ProfilesParentalControls);

        public ProfileViewModel(IServiceProvider serviceProvider, IEntitlementService entitlementService)
        {
            _serviceProvider = serviceProvider;
            _entitlementService = entitlementService;
            _profileLimit = _entitlementService.GetLimit(EntitlementLimitKeys.ProfilesMaxCount);
        }

        public void RefreshLocalizedText()
        {
            foreach (var propertyName in new[]
            {
                nameof(ProfileSwitcherVisibility),
                nameof(ProfileCountText),
                nameof(CanDeleteSelectedProfile),
                nameof(ActiveProfileName),
                nameof(SelectedProfileTypeText),
                nameof(SourceLocksSummaryText),
                nameof(CategoryLocksSummaryText),
                nameof(LocalProfileExplanationText),
                nameof(CanAddProfiles),
                nameof(CanManageParentalControls)
            })
            {
                OnPropertyChanged(propertyName);
            }
        }

        partial void OnSelectedProfileChanged(AppProfile? value)
        {
            if (_isLoading || value == null)
            {
                return;
            }

            _ = SwitchProfileAsync(value);
        }

        partial void OnIsKidsSafeModeChanged(bool value)
        {
            if (_isLoading || SelectedProfile == null)
            {
                return;
            }

            _ = UpdateKidsSafeModeAsync(value);
        }

        partial void OnHideLockedContentChanged(bool value)
        {
            if (_isLoading || SelectedProfile == null)
            {
                return;
            }

            _ = UpdateHideLockedContentAsync(value);
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

            _isLoading = true;
            try
            {
                Profiles.Clear();
                var profiles = await profileService.GetProfilesAsync(db);
                foreach (var profile in profiles)
                {
                    Profiles.Add(profile);
                }

                var activeProfile = await profileService.GetActiveProfileAsync(db);
                var selectedProfile = ResolveSelectableProfile(activeProfile);
                SelectedProfile = selectedProfile;
                await LoadSelectedProfileStateAsync(db, profileService, selectedProfile);
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("PROFILE", ex, "load failed");
                ProfileActionStatusText = $"Profile load failed: {ex.Message}";
                SourceLocks.Clear();
                CategoryLocks.Clear();
                OnPropertyChanged(nameof(SourceLocksListVisibility));
                OnPropertyChanged(nameof(SourceLocksEmptyVisibility));
                OnPropertyChanged(nameof(SourceLocksSummaryText));
                OnPropertyChanged(nameof(CategoryLocksListVisibility));
                OnPropertyChanged(nameof(CategoryLocksEmptyVisibility));
                OnPropertyChanged(nameof(CategoryLocksSummaryText));
            }
            finally
            {
                _isLoading = false;
                OnPropertyChanged(nameof(ProfileSwitcherVisibility));
                OnPropertyChanged(nameof(ProfileCountText));
                OnPropertyChanged(nameof(CanDeleteSelectedProfile));
                OnPropertyChanged(nameof(CanAddProfiles));
                OnPropertyChanged(nameof(CanManageParentalControls));
            }
        }

        [RelayCommand]
        public async Task AddProfileAsync()
        {
            await CreateProfileAsync(false);
        }

        [RelayCommand]
        public async Task AddKidsProfileAsync()
        {
            await CreateProfileAsync(true);
        }

        [RelayCommand]
        public async Task SaveProfileNameAsync()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

            await profileService.RenameProfileAsync(db, SelectedProfile.Id, EditableProfileName);
            await LoadAsync();
        }

        [RelayCommand]
        public async Task DeleteSelectedProfileAsync()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            if (!CanDeleteSelectedProfile)
            {
                ProfileActionStatusText = "At least one profile must remain.";
                return;
            }

            var deletedProfileName = SelectedProfile.Name;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

                var deleted = await profileService.DeleteProfileAsync(db, SelectedProfile.Id);
                ProfileActionStatusText = deleted
                    ? $"Deleted profile {deletedProfileName}."
                    : "This profile could not be deleted. At least one profile must remain.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("PROFILE", ex, $"delete failed profile_id={SelectedProfile.Id}");
                ProfileActionStatusText = $"Profile deletion failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task SetPinAsync()
        {
            if (SelectedProfile == null || string.IsNullOrWhiteSpace(PinDraft) || !CanManageParentalControls)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

            await profileService.SetPinAsync(db, SelectedProfile.Id, PinDraft);
            PinDraft = string.Empty;
            PinStatusText = "PIN saved. Locked content can be unlocked per session.";
            await LoadSelectedProfileStateAsync(db, profileService, await profileService.GetActiveProfileAsync(db));
        }

        [RelayCommand]
        public async Task ClearPinAsync()
        {
            if (SelectedProfile == null || !CanManageParentalControls)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

            await profileService.ClearPinAsync(db, SelectedProfile.Id);
            PinStatusText = "PIN cleared. Locked content stays hidden unless you remove locks.";
            await LoadSelectedProfileStateAsync(db, profileService, await profileService.GetActiveProfileAsync(db));
        }

        [RelayCommand]
        public async Task UnlockLockedContentAsync()
        {
            if (SelectedProfile == null || string.IsNullOrWhiteSpace(UnlockPinDraft) || !CanManageParentalControls)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

            var unlocked = await profileService.UnlockLockedContentAsync(db, SelectedProfile.Id, UnlockPinDraft);
            UnlockPinDraft = string.Empty;
            PinStatusText = unlocked
                ? "Locked content is visible for this session."
                : "PIN did not match this profile.";
            await LoadSelectedProfileStateAsync(db, profileService, await profileService.GetActiveProfileAsync(db));
        }

        [RelayCommand]
        public async Task RelockContentAsync()
        {
            if (SelectedProfile == null || !CanManageParentalControls)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

            profileService.RelockProfile(SelectedProfile.Id);
            PinStatusText = "Locked content is hidden again.";
            await LoadSelectedProfileStateAsync(db, profileService, await profileService.GetActiveProfileAsync(db));
        }

        private async Task CreateProfileAsync(bool isKidsProfile)
        {
            if (!CanAddProfiles)
            {
                PinStatusText = _profileLimit.HasValue
                    ? $"This entitlement supports up to {_profileLimit.Value} profile{(_profileLimit.Value == 1 ? string.Empty : "s")}."
                    : "Profile creation is unavailable for this entitlement.";
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var existingCount = await db.AppProfiles.CountAsync();
            var profile = await profileService.CreateProfileAsync(
                db,
                isKidsProfile ? $"Kids {existingCount + 1}" : $"Profile {existingCount + 1}",
                isKidsProfile);
            await profileService.SwitchProfileAsync(db, profile.Id);
            await LoadAsync();
        }

        private async Task SwitchProfileAsync(AppProfile profile)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();

            await profileService.SwitchProfileAsync(db, profile.Id);
            await LoadSelectedProfileStateAsync(db, profileService, profile);
        }

        private async Task LoadSelectedProfileStateAsync(AppDbContext db, IProfileStateService profileService, AppProfile? profile)
        {
            if (profile == null)
            {
                return;
            }

            _isLoading = true;
            try
            {
                var selectedProfile = ResolveSelectableProfile(profile);
                SelectedProfile = selectedProfile;
                EditableProfileName = selectedProfile.Name;

                var controls = await profileService.GetParentalControlsAsync(db, selectedProfile.Id);
                var access = await profileService.GetAccessSnapshotAsync(db);

                HasPin = !string.IsNullOrWhiteSpace(controls.PinHash);
                IsLockedContentUnlocked = access.IsUnlocked;
                IsKidsSafeMode = selectedProfile.IsKidsProfile || controls.IsKidsSafeMode;
                HideLockedContent = controls.HideLockedContent;
                PinStatusText = HasPin
                    ? IsLockedContentUnlocked
                        ? "PIN-protected locks are temporarily visible."
                        : "PIN set. Enter it to reveal locked content for this session."
                    : "No PIN set.";

                await BuildSourceLocksAsync(db, profileService, selectedProfile.Id, access);
                await BuildCategoryLocksAsync(db, profileService, selectedProfile.Id, access);
                AccessStatusText = BuildAccessStatus(access);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private AppProfile ResolveSelectableProfile(AppProfile profile)
        {
            return Profiles.FirstOrDefault(item => item.Id == profile.Id) ?? profile;
        }

        private async Task BuildSourceLocksAsync(AppDbContext db, IProfileStateService profileService, int profileId, ProfileAccessSnapshot access)
        {
            SourceLocks.Clear();
            var sources = await db.SourceProfiles
                .AsNoTracking()
                .OrderBy(source => source.Name)
                .ToListAsync();

            foreach (var source in sources)
            {
                SourceLocks.Add(new ProfileLockOptionViewModel(
                    source.Id.ToString(),
                    source.Name,
                    source.Type.ToString(),
                    access.LockedSourceIds.Contains(source.Id),
                    (option, isLocked) =>
                    {
                        _ = SaveSourceLocksAsync(profileService, profileId);
                    }));
            }

            OnPropertyChanged(nameof(SourceLocksListVisibility));
            OnPropertyChanged(nameof(SourceLocksEmptyVisibility));
            OnPropertyChanged(nameof(SourceLocksSummaryText));
        }

        private async Task BuildCategoryLocksAsync(AppDbContext db, IProfileStateService profileService, int profileId, ProfileAccessSnapshot access)
        {
            CategoryLocks.Clear();

            var liveCategories = await db.ChannelCategories
                .AsNoTracking()
                .OrderBy(category => category.Name)
                .Select(category => category.Name)
                .Distinct()
                .ToListAsync();
            var movieCategories = await db.Movies
                .AsNoTracking()
                .Where(movie => movie.CategoryName != string.Empty)
                .OrderBy(movie => movie.CategoryName)
                .Select(movie => movie.CategoryName)
                .Distinct()
                .ToListAsync();
            var seriesCategories = await db.Series
                .AsNoTracking()
                .Where(series => series.CategoryName != string.Empty)
                .OrderBy(series => series.CategoryName)
                .Select(series => series.CategoryName)
                .Distinct()
                .ToListAsync();

            AddCategoryLockOptions(liveCategories, ProfileDomains.Live, profileService, profileId, access);
            AddCategoryLockOptions(movieCategories, ProfileDomains.Movies, profileService, profileId, access);
            AddCategoryLockOptions(seriesCategories, ProfileDomains.Series, profileService, profileId, access);

            OnPropertyChanged(nameof(CategoryLocksListVisibility));
            OnPropertyChanged(nameof(CategoryLocksEmptyVisibility));
            OnPropertyChanged(nameof(CategoryLocksSummaryText));
        }

        private void AddCategoryLockOptions(
            IEnumerable<string> categories,
            string domain,
            IProfileStateService profileService,
            int profileId,
            ProfileAccessSnapshot access)
        {
            foreach (var category in categories.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var key = ProfileStateService.MakeCategoryLockKey(domain, category);
                CategoryLocks.Add(new ProfileLockOptionViewModel(
                    key,
                    category,
                    domain,
                    access.LockedCategoryKeys.Contains(key),
                    (option, isLocked) =>
                    {
                        _ = SaveCategoryLocksAsync(profileService, profileId);
                    }));
            }
        }

        private async Task SaveSourceLocksAsync(IProfileStateService profileService, int profileId)
        {
            if (_isLoading || _isSavingLocks || !CanManageParentalControls)
            {
                return;
            }

            _isSavingLocks = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                try
                {
                    await profileService.SetLockedSourceIdsAsync(
                        db,
                        profileId,
                        SourceLocks.Where(option => option.IsLocked).Select(option => int.Parse(option.Key)));
                    AccessStatusText = BuildAccessStatus(await profileService.GetAccessSnapshotAsync(db));
                    ProfileActionStatusText = string.Empty;
                }
                catch (Exception ex)
                {
                    RuntimeEventLogger.Log("PROFILE", ex, $"source lock save failed profile_id={profileId}");
                    ProfileActionStatusText = $"Source lock update failed: {ex.Message}";
                }
            }
            finally
            {
                _isSavingLocks = false;
            }
        }

        private async Task SaveCategoryLocksAsync(IProfileStateService profileService, int profileId)
        {
            if (_isLoading || _isSavingLocks || !CanManageParentalControls)
            {
                return;
            }

            _isSavingLocks = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                try
                {
                    await profileService.SetLockedCategoryKeysAsync(
                        db,
                        profileId,
                        CategoryLocks.Where(option => option.IsLocked).Select(option => option.Key));
                    AccessStatusText = BuildAccessStatus(await profileService.GetAccessSnapshotAsync(db));
                    ProfileActionStatusText = string.Empty;
                }
                catch (Exception ex)
                {
                    RuntimeEventLogger.Log("PROFILE", ex, $"category lock save failed profile_id={profileId}");
                    ProfileActionStatusText = $"Category lock update failed: {ex.Message}";
                }
            }
            finally
            {
                _isSavingLocks = false;
            }
        }

        private async Task UpdateKidsSafeModeAsync(bool value)
        {
            if (!CanManageParentalControls)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            if (SelectedProfile == null)
            {
                return;
            }

            await profileService.SetKidsSafeModeAsync(db, SelectedProfile.Id, value);
            AccessStatusText = BuildAccessStatus(await profileService.GetAccessSnapshotAsync(db));
        }

        private async Task UpdateHideLockedContentAsync(bool value)
        {
            if (!CanManageParentalControls)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            if (SelectedProfile == null)
            {
                return;
            }

            await profileService.SetHideLockedContentAsync(db, SelectedProfile.Id, value);
            AccessStatusText = BuildAccessStatus(await profileService.GetAccessSnapshotAsync(db));
        }

        private static string BuildAccessStatus(ProfileAccessSnapshot access)
        {
            var parts = new List<string>();
            if (access.IsKidsSafeMode)
            {
                parts.Add("Kids-safe mode filters adult and non-primary catalog items.");
            }

            var lockedCount = access.LockedSourceIds.Count + access.LockedCategoryKeys.Count;
            if (lockedCount > 0)
            {
                parts.Add(access.IsUnlocked
                    ? $"{lockedCount} lock(s) are visible for this session."
                    : $"{lockedCount} lock(s) are currently hidden.");
            }

            if (parts.Count == 0)
            {
                return "All imported content is visible for this profile.";
            }

            return string.Join(" ", parts);
        }
    }
}
