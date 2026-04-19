#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Kroira.App.Data;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public partial class MediaLibraryViewModel : ObservableObject, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMediaJobService _mediaJobService;
        private readonly DispatcherQueue _dispatcherQueue;

        public ObservableCollection<MediaLibraryRecordingItem> Recordings { get; } = new();
        public ObservableCollection<MediaLibraryDownloadItem> Downloads { get; } = new();

        [ObservableProperty]
        private string _rootPath = string.Empty;

        [ObservableProperty]
        private string _storageSummary = "Loading storage...";

        [ObservableProperty]
        private string _activitySummary = string.Empty;

        [ObservableProperty]
        private Visibility _recordingsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _downloadsVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _emptyVisibility = Visibility.Visible;

        public MediaLibraryViewModel(IServiceProvider serviceProvider, IMediaJobService mediaJobService)
        {
            _serviceProvider = serviceProvider;
            _mediaJobService = mediaJobService;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _mediaJobService.JobsChanged += MediaJobService_JobsChanged;
        }

        public async Task LoadAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var profileId = await profileService.GetActiveProfileIdAsync(db);
            var snapshot = await _mediaJobService.LoadLibrarySnapshotAsync(profileId);

            RootPath = snapshot.RootPath;
            StorageSummary = $"{FormatBytes(snapshot.TotalBytes)} stored";
            ActivitySummary = $"{snapshot.ActiveRecordingCount} recording{(snapshot.ActiveRecordingCount == 1 ? string.Empty : "s")} active • {snapshot.ActiveDownloadCount} download{(snapshot.ActiveDownloadCount == 1 ? string.Empty : "s")} active";

            Recordings.Clear();
            foreach (var item in snapshot.Recordings)
            {
                Recordings.Add(item);
            }

            Downloads.Clear();
            foreach (var item in snapshot.Downloads)
            {
                Downloads.Add(item);
            }

            RecordingsVisibility = Recordings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DownloadsVisibility = Downloads.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyVisibility = Recordings.Count == 0 && Downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public Task RetryRecordingAsync(int jobId) => _mediaJobService.RetryRecordingAsync(jobId);
        public Task RetryDownloadAsync(int jobId) => _mediaJobService.RetryDownloadAsync(jobId);
        public Task CancelRecordingAsync(int jobId) => _mediaJobService.CancelRecordingAsync(jobId);
        public Task CancelDownloadAsync(int jobId) => _mediaJobService.CancelDownloadAsync(jobId);
        public Task DeleteRecordingAsync(int jobId) => _mediaJobService.DeleteRecordingAsync(jobId);
        public Task DeleteDownloadAsync(int jobId) => _mediaJobService.DeleteDownloadAsync(jobId);

        private void MediaJobService_JobsChanged(object? sender, EventArgs e)
        {
            _dispatcherQueue.TryEnqueue(async () => await LoadAsync());
        }

        public void Dispose()
        {
            _mediaJobService.JobsChanged -= MediaJobService_JobsChanged;
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var suffixIndex = 0;
            while (value >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                value /= 1024;
                suffixIndex++;
            }

            return $"{value:0.#} {suffixes[suffixIndex]}";
        }
    }
}
