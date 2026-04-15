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
        private bool _isM3U = true;

        [ObservableProperty]
        private string _m3uUrlOrPath = string.Empty;

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
                        Url = IsM3U ? M3uUrlOrPath : string.Empty,
                        Username = string.Empty,
                        Password = string.Empty
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

                    StatusMessage = "Source saved successfully!";
                    
                    SourceName = string.Empty;
                    M3uUrlOrPath = string.Empty;
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
