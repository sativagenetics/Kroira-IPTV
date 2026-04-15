using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Kroira.App.ViewModels
{
    public partial class SourceOnboardingViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty]
        private string _sourceName = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsM3U))]
        [NotifyPropertyChangedFor(nameof(M3UVisibility))]
        [NotifyPropertyChangedFor(nameof(XtreamVisibility))]
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
        private string _epgUrl = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasStatus))]
        [NotifyPropertyChangedFor(nameof(StatusVisibility))]
        private string _statusMessage = string.Empty;

        public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
        public Microsoft.UI.Xaml.Visibility StatusVisibility => HasStatus ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public SourceOnboardingViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task SaveSourceAsync()
        {
            if (string.IsNullOrWhiteSpace(SourceName))
            {
                StatusMessage = "Name is required.";
                return;
            }

            if (IsM3U && string.IsNullOrWhiteSpace(M3uUrlOrPath))
            {
                StatusMessage = "M3U URL or File Path is required.";
                return;
            }

            if (!IsM3U && (string.IsNullOrWhiteSpace(XtreamUrl) || string.IsNullOrWhiteSpace(XtreamUsername) || string.IsNullOrWhiteSpace(XtreamPassword)))
            {
                StatusMessage = "Server URL, Username, and Password are required for Xtream.";
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                using var transaction = await db.Database.BeginTransactionAsync();

                try
                {
                    var profile = new SourceProfile
                    {
                        Name = SourceName,
                        Type = IsM3U ? SourceType.M3U : SourceType.Xtream,
                        LastSync = null
                    };

                    db.SourceProfiles.Add(profile);
                    await db.SaveChangesAsync();

                    var creds = new SourceCredential
                    {
                        SourceProfileId = profile.Id,
                        Url = IsM3U ? M3uUrlOrPath : XtreamUrl,
                        Username = IsM3U ? string.Empty : XtreamUsername,
                        Password = IsM3U ? string.Empty : XtreamPassword,
                        EpgUrl = EpgUrl
                    };
                    db.SourceCredentials.Add(creds);

                    var sync = new SourceSyncState
                    {
                        SourceProfileId = profile.Id,
                        LastAttempt = DateTime.UtcNow,
                        HttpStatusCode = 0,
                        ErrorLog = string.Empty
                    };
                    db.SourceSyncStates.Add(sync);

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    int savedId = profile.Id;
                    bool isM3u = profile.Type == SourceType.M3U;

                    StatusMessage = "Source saved. Importing...";

                    // Auto-import after save so user doesn't need a separate manual step
                    try
                    {
                        using var importScope = _serviceProvider.CreateScope();
                        var importDb = importScope.ServiceProvider.GetRequiredService<AppDbContext>();

                        if (isM3u)
                        {
                            var m3uParser = importScope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IM3uParserService>();
                            await m3uParser.ParseAndImportM3uAsync(importDb, savedId);
                        }
                        else
                        {
                            var xtreamParser = importScope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXtreamParserService>();
                            await xtreamParser.ParseAndImportXtreamAsync(importDb, savedId);
                        }

                        StatusMessage = "Source saved and imported successfully!";
                    }
                    catch (Exception importEx)
                    {
                        StatusMessage = $"Source saved, but import failed: {importEx.Message}";
                    }

                    SourceName = string.Empty;
                    M3uUrlOrPath = string.Empty;
                    EpgUrl = string.Empty;
                    XtreamUrl = string.Empty;
                    XtreamUsername = string.Empty;
                    XtreamPassword = string.Empty;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving: {ex.Message}";
            }
        }

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
