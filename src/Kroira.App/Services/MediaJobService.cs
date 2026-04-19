using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public static class MediaJobStatuses
    {
        public const string Pending = "Pending";
        public const string Scheduled = "Scheduled";
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Retrying = "Retrying";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Canceled = "Canceled";
    }

    public sealed class MediaLibraryRecordingItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int PlaybackContentId { get; set; }
        public PlaybackContentType PlaybackContentType { get; set; }
        public bool CanPlay { get; set; }
        public bool CanRetry { get; set; }
        public bool CanCancel { get; set; }
        public bool CanDelete { get; set; }
    }

    public sealed class MediaLibraryDownloadItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int PlaybackContentId { get; set; }
        public PlaybackContentType PlaybackContentType { get; set; }
        public bool CanPlay { get; set; }
        public bool CanRetry { get; set; }
        public bool CanCancel { get; set; }
        public bool CanDelete { get; set; }
    }

    public sealed class MediaLibrarySnapshot
    {
        public string RootPath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public int ActiveRecordingCount { get; set; }
        public int ActiveDownloadCount { get; set; }
        public IReadOnlyList<MediaLibraryRecordingItem> Recordings { get; set; } = Array.Empty<MediaLibraryRecordingItem>();
        public IReadOnlyList<MediaLibraryDownloadItem> Downloads { get; set; } = Array.Empty<MediaLibraryDownloadItem>();
    }

    public interface IMediaJobService
    {
        event EventHandler JobsChanged;
        void Start();
        Task<RecordingJob> ScheduleRecordingAsync(int channelId, string channelName, string streamUrl, DateTime startLocal, TimeSpan duration);
        Task<DownloadJob> QueueDownloadAsync(PlaybackContentType contentType, int contentId, string title, string subtitle, string streamUrl);
        Task<MediaLibrarySnapshot> LoadLibrarySnapshotAsync(int profileId);
        Task RetryRecordingAsync(int jobId);
        Task RetryDownloadAsync(int jobId);
        Task CancelRecordingAsync(int jobId);
        Task CancelDownloadAsync(int jobId);
        Task DeleteRecordingAsync(int jobId);
        Task DeleteDownloadAsync(int jobId);
    }

    public sealed class MediaJobService : IMediaJobService, IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan MinimumRecordableWindow = TimeSpan.FromSeconds(20);

        private readonly IServiceProvider _serviceProvider;
        private readonly IEntitlementService _entitlementService;
        private readonly SemaphoreSlim _processGate = new(1, 1);
        private readonly Dictionary<int, CancellationTokenSource> _activeRecordingTokens = new();
        private readonly Dictionary<int, CancellationTokenSource> _activeDownloadTokens = new();
        private CancellationTokenSource _loopCancellationTokenSource;
        private Task _processingLoopTask = Task.CompletedTask;
        private bool _started;
        private bool _disposed;

        public MediaJobService(IServiceProvider serviceProvider, IEntitlementService entitlementService)
        {
            _serviceProvider = serviceProvider;
            _entitlementService = entitlementService;
        }

        public event EventHandler JobsChanged;

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _loopCancellationTokenSource = new CancellationTokenSource();
            _processingLoopTask = Task.Run(() => ProcessingLoopAsync(_loopCancellationTokenSource.Token));
            _ = ProcessJobsAsync();
        }

        public async Task<RecordingJob> ScheduleRecordingAsync(int channelId, string channelName, string streamUrl, DateTime startLocal, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Recording duration must be greater than zero.");
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var profileId = await profileService.GetActiveProfileIdAsync(db);
            var startUtc = NormalizeLocalTime(startLocal);
            var endUtc = startUtc + duration;
            var now = DateTime.UtcNow;
            if (endUtc <= now)
            {
                throw new InvalidOperationException("Recording end time must be in the future.");
            }

            var rootPath = await GetMediaRootPathAsync(db);
            Directory.CreateDirectory(Path.Combine(rootPath, "Recordings"));

            var extension = InferFileExtension(streamUrl, ".ts");
            var outputPath = BuildOutputPath(rootPath, "Recordings", channelName, startUtc, extension);
            var tempOutputPath = outputPath + ".partial";

            var job = new RecordingJob
            {
                ProfileId = profileId,
                ChannelId = channelId,
                ChannelName = channelName ?? string.Empty,
                StreamUrl = streamUrl ?? string.Empty,
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                RequestedAtUtc = now,
                UpdatedAtUtc = now,
                OutputPath = outputPath,
                TempOutputPath = tempOutputPath,
                FileName = Path.GetFileName(outputPath),
                Status = startUtc <= now ? MediaJobStatuses.Pending : MediaJobStatuses.Scheduled
            };

            db.RecordingJobs.Add(job);
            await db.SaveChangesAsync();
            RaiseJobsChanged();
            _ = ProcessJobsAsync();
            return job;
        }

        public async Task<DownloadJob> QueueDownloadAsync(PlaybackContentType contentType, int contentId, string title, string subtitle, string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                throw new InvalidOperationException("Stream URL is required.");
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var profileId = await profileService.GetActiveProfileIdAsync(db);
            var now = DateTime.UtcNow;
            var rootPath = await GetMediaRootPathAsync(db);
            Directory.CreateDirectory(Path.Combine(rootPath, "Downloads"));

            var extension = InferFileExtension(streamUrl, ".mp4");
            var outputPath = BuildOutputPath(rootPath, "Downloads", title, now, extension);
            var tempOutputPath = outputPath + ".partial";

            var job = new DownloadJob
            {
                ProfileId = profileId,
                ContentType = contentType,
                ContentId = contentId,
                Title = title ?? string.Empty,
                Subtitle = subtitle ?? string.Empty,
                StreamUrl = streamUrl,
                RequestedAtUtc = now,
                UpdatedAtUtc = now,
                OutputPath = outputPath,
                TempOutputPath = tempOutputPath,
                FileName = Path.GetFileName(outputPath),
                Status = MediaJobStatuses.Queued
            };

            db.DownloadJobs.Add(job);
            await db.SaveChangesAsync();
            RaiseJobsChanged();
            _ = ProcessJobsAsync();
            return job;
        }

        public async Task<MediaLibrarySnapshot> LoadLibrarySnapshotAsync(int profileId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rootPath = await GetMediaRootPathAsync(db);

            var recordings = await db.RecordingJobs
                .Where(job => job.ProfileId == profileId)
                .OrderByDescending(job => job.RequestedAtUtc)
                .ToListAsync();

            var downloads = await db.DownloadJobs
                .Where(job => job.ProfileId == profileId)
                .OrderByDescending(job => job.RequestedAtUtc)
                .ToListAsync();

            return new MediaLibrarySnapshot
            {
                RootPath = rootPath,
                TotalBytes = recordings.Sum(job => ExistingFileSize(job.OutputPath, job.FileSizeBytes)) +
                             downloads.Sum(job => ExistingFileSize(job.OutputPath, job.FileSizeBytes)),
                ActiveRecordingCount = recordings.Count(job => string.Equals(job.Status, MediaJobStatuses.Running, StringComparison.OrdinalIgnoreCase)),
                ActiveDownloadCount = downloads.Count(job => string.Equals(job.Status, MediaJobStatuses.Running, StringComparison.OrdinalIgnoreCase)),
                Recordings = recordings.Select(BuildRecordingItem).ToList(),
                Downloads = downloads.Select(BuildDownloadItem).ToList()
            };
        }

        public async Task RetryRecordingAsync(int jobId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.RecordingJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            job.Status = job.StartTimeUtc <= DateTime.UtcNow ? MediaJobStatuses.Pending : MediaJobStatuses.Scheduled;
            job.NextRetryAtUtc = null;
            job.RetryCount = 0;
            job.LastError = string.Empty;
            job.UpdatedAtUtc = DateTime.UtcNow;
            TryDeleteFile(job.TempOutputPath);
            await db.SaveChangesAsync();
            RaiseJobsChanged();
            _ = ProcessJobsAsync();
        }

        public async Task RetryDownloadAsync(int jobId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            job.Status = MediaJobStatuses.Queued;
            job.NextRetryAtUtc = null;
            job.RetryCount = 0;
            job.LastError = string.Empty;
            job.UpdatedAtUtc = DateTime.UtcNow;
            TryDeleteFile(job.TempOutputPath);
            await db.SaveChangesAsync();
            RaiseJobsChanged();
            _ = ProcessJobsAsync();
        }

        public async Task CancelRecordingAsync(int jobId)
        {
            if (_activeRecordingTokens.TryGetValue(jobId, out var tokenSource))
            {
                tokenSource.Cancel();
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.RecordingJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            job.Status = MediaJobStatuses.Canceled;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            RaiseJobsChanged();
        }

        public async Task CancelDownloadAsync(int jobId)
        {
            if (_activeDownloadTokens.TryGetValue(jobId, out var tokenSource))
            {
                tokenSource.Cancel();
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            job.Status = MediaJobStatuses.Canceled;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            RaiseJobsChanged();
        }

        public async Task DeleteRecordingAsync(int jobId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.RecordingJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            TryDeleteFile(job.TempOutputPath);
            TryDeleteFile(job.OutputPath);
            db.RecordingJobs.Remove(job);
            await db.SaveChangesAsync();
            RaiseJobsChanged();
        }

        public async Task DeleteDownloadAsync(int jobId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            TryDeleteFile(job.TempOutputPath);
            TryDeleteFile(job.OutputPath);
            db.DownloadJobs.Remove(job);
            await db.SaveChangesAsync();
            RaiseJobsChanged();
        }

        private async Task ProcessingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessJobsAsync();
                    await Task.Delay(PollInterval, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch
                {
                }
            }
        }

        private async Task ProcessJobsAsync()
        {
            if (_disposed)
            {
                return;
            }

            await _processGate.WaitAsync();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;

                var staleRecordings = await db.RecordingJobs
                    .Where(job =>
                        job.Status == MediaJobStatuses.Pending ||
                        job.Status == MediaJobStatuses.Scheduled ||
                        job.Status == MediaJobStatuses.Retrying)
                    .Where(job => job.EndTimeUtc <= now)
                    .ToListAsync();

                foreach (var job in staleRecordings)
                {
                    job.Status = MediaJobStatuses.Failed;
                    job.LastError = "Recording window passed before capture could start.";
                    job.UpdatedAtUtc = now;
                }

                if (staleRecordings.Count > 0)
                {
                    await db.SaveChangesAsync();
                    RaiseJobsChanged();
                }

                var recordingLimit = Math.Max(_entitlementService.GetLimit(EntitlementLimitKeys.RecordingConcurrentJobs) ?? 1, 1);
                var availableRecordingSlots = Math.Max(recordingLimit - _activeRecordingTokens.Count, 0);
                if (availableRecordingSlots > 0)
                {
                    var dueRecordings = await db.RecordingJobs
                        .Where(job =>
                            job.Status == MediaJobStatuses.Pending ||
                            job.Status == MediaJobStatuses.Scheduled ||
                            job.Status == MediaJobStatuses.Retrying)
                        .Where(job => job.StartTimeUtc <= now)
                        .Where(job => job.EndTimeUtc > now)
                        .Where(job => job.NextRetryAtUtc == null || job.NextRetryAtUtc <= now)
                        .OrderBy(job => job.StartTimeUtc)
                        .Take(availableRecordingSlots)
                        .ToListAsync();

                    foreach (var job in dueRecordings)
                    {
                        if (!_activeRecordingTokens.ContainsKey(job.Id))
                        {
                            await StartRecordingJobAsync(job.Id);
                        }
                    }
                }

                var downloadLimit = Math.Max(_entitlementService.GetLimit(EntitlementLimitKeys.DownloadConcurrentJobs) ?? 1, 1);
                var availableDownloadSlots = Math.Max(downloadLimit - _activeDownloadTokens.Count, 0);
                if (availableDownloadSlots > 0)
                {
                    var dueDownloads = await db.DownloadJobs
                        .Where(job =>
                            job.Status == MediaJobStatuses.Queued ||
                            job.Status == MediaJobStatuses.Retrying)
                        .Where(job => job.NextRetryAtUtc == null || job.NextRetryAtUtc <= now)
                        .OrderBy(job => job.RequestedAtUtc)
                        .Take(availableDownloadSlots)
                        .ToListAsync();

                    foreach (var job in dueDownloads)
                    {
                        if (!_activeDownloadTokens.ContainsKey(job.Id))
                        {
                            await StartDownloadJobAsync(job.Id);
                        }
                    }
                }
            }
            finally
            {
                _processGate.Release();
            }
        }

        private async Task StartRecordingJobAsync(int jobId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.RecordingJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (job.EndTimeUtc <= now || job.EndTimeUtc - now < MinimumRecordableWindow)
            {
                job.Status = MediaJobStatuses.Failed;
                job.LastError = "Recording window is too short to capture usable media.";
                job.UpdatedAtUtc = now;
                await db.SaveChangesAsync();
                RaiseJobsChanged();
                return;
            }

            job.Status = MediaJobStatuses.Running;
            job.StartedAtUtc = now;
            job.UpdatedAtUtc = now;
            job.LastError = string.Empty;
            TryDeleteFile(job.TempOutputPath);
            await db.SaveChangesAsync();

            var cancellationTokenSource = new CancellationTokenSource();
            _activeRecordingTokens[job.Id] = cancellationTokenSource;
            RaiseJobsChanged();

            _ = Task.Run(async () =>
            {
                try
                {
                    using var session = new HeadlessMpvCaptureSession();
                    var result = await session.RunAsync(
                        job.StreamUrl,
                        job.TempOutputPath,
                        job.EndTimeUtc - DateTime.UtcNow,
                        cancellationTokenSource.Token);

                    await FinalizeRecordingJobAsync(job.Id, result);
                }
                catch (Exception ex)
                {
                    await FinalizeRecordingJobAsync(job.Id, new HeadlessMpvCaptureResult
                    {
                        IsSuccess = false,
                        Message = ex.Message
                    });
                }
                finally
                {
                    cancellationTokenSource.Dispose();
                    _activeRecordingTokens.Remove(job.Id);
                    RaiseJobsChanged();
                    _ = ProcessJobsAsync();
                }
            });
        }

        private async Task StartDownloadJobAsync(int jobId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(job.StreamUrl))
            {
                job.Status = MediaJobStatuses.Failed;
                job.LastError = "Download stream URL is missing.";
                job.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                RaiseJobsChanged();
                return;
            }

            var now = DateTime.UtcNow;
            job.Status = MediaJobStatuses.Running;
            job.StartedAtUtc = now;
            job.UpdatedAtUtc = now;
            job.LastError = string.Empty;
            TryDeleteFile(job.TempOutputPath);
            await db.SaveChangesAsync();

            var cancellationTokenSource = new CancellationTokenSource();
            _activeDownloadTokens[job.Id] = cancellationTokenSource;
            RaiseJobsChanged();

            _ = Task.Run(async () =>
            {
                try
                {
                    using var session = new HeadlessMpvCaptureSession();
                    var result = await session.RunAsync(
                        job.StreamUrl,
                        job.TempOutputPath,
                        null,
                        cancellationTokenSource.Token);

                    await FinalizeDownloadJobAsync(job.Id, result);
                }
                catch (Exception ex)
                {
                    await FinalizeDownloadJobAsync(job.Id, new HeadlessMpvCaptureResult
                    {
                        IsSuccess = false,
                        Message = ex.Message
                    });
                }
                finally
                {
                    cancellationTokenSource.Dispose();
                    _activeDownloadTokens.Remove(job.Id);
                    RaiseJobsChanged();
                    _ = ProcessJobsAsync();
                }
            });
        }

        private async Task FinalizeRecordingJobAsync(int jobId, HeadlessMpvCaptureResult result)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.RecordingJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            job.UpdatedAtUtc = now;

            if (result.IsSuccess)
            {
                FinalizeOutput(job.TempOutputPath, job.OutputPath, out var fileSizeBytes);
                job.FileSizeBytes = fileSizeBytes;
                job.CompletedAtUtc = now;
                job.NextRetryAtUtc = null;
                job.Status = MediaJobStatuses.Completed;
                job.LastError = string.Empty;
            }
            else if (result.IsCanceled)
            {
                TryDeleteFile(job.TempOutputPath);
                job.NextRetryAtUtc = null;
                job.Status = MediaJobStatuses.Canceled;
                job.LastError = string.Empty;
            }
            else
            {
                TryDeleteFile(job.TempOutputPath);
                job.RetryCount++;
                job.LastError = string.IsNullOrWhiteSpace(result.Message) ? "Recording failed." : result.Message;
                if (job.RetryCount <= job.MaxRetryCount && job.EndTimeUtc > now + MinimumRecordableWindow)
                {
                    job.Status = MediaJobStatuses.Retrying;
                    job.NextRetryAtUtc = now + RetryDelay;
                }
                else
                {
                    job.Status = MediaJobStatuses.Failed;
                    job.NextRetryAtUtc = null;
                }
            }

            await db.SaveChangesAsync();
            RaiseJobsChanged();
        }

        private async Task FinalizeDownloadJobAsync(int jobId, HeadlessMpvCaptureResult result)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DownloadJobs.FirstOrDefaultAsync(item => item.Id == jobId);
            if (job == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            job.UpdatedAtUtc = now;

            if (result.IsSuccess)
            {
                FinalizeOutput(job.TempOutputPath, job.OutputPath, out var fileSizeBytes);
                job.FileSizeBytes = fileSizeBytes;
                job.CompletedAtUtc = now;
                job.NextRetryAtUtc = null;
                job.Status = MediaJobStatuses.Completed;
                job.LastError = string.Empty;
            }
            else if (result.IsCanceled)
            {
                TryDeleteFile(job.TempOutputPath);
                job.NextRetryAtUtc = null;
                job.Status = MediaJobStatuses.Canceled;
                job.LastError = string.Empty;
            }
            else
            {
                TryDeleteFile(job.TempOutputPath);
                job.RetryCount++;
                job.LastError = string.IsNullOrWhiteSpace(result.Message) ? "Download failed." : result.Message;
                if (job.RetryCount <= job.MaxRetryCount)
                {
                    job.Status = MediaJobStatuses.Retrying;
                    job.NextRetryAtUtc = now + RetryDelay;
                }
                else
                {
                    job.Status = MediaJobStatuses.Failed;
                    job.NextRetryAtUtc = null;
                }
            }

            await db.SaveChangesAsync();
            RaiseJobsChanged();
        }

        private async Task<string> GetMediaRootPathAsync(AppDbContext db)
        {
            const string storageKey = "MediaLibrary.RootPath";
            var configured = await db.AppSettings
                .AsNoTracking()
                .Where(setting => setting.Key == storageKey)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            var rootPath = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "MediaLibrary")
                : configured.Trim();

            Directory.CreateDirectory(rootPath);
            return rootPath;
        }

        private static MediaLibraryRecordingItem BuildRecordingItem(RecordingJob job)
        {
            var canPlay = string.Equals(job.Status, MediaJobStatuses.Completed, StringComparison.OrdinalIgnoreCase) &&
                          File.Exists(job.OutputPath);

            return new MediaLibraryRecordingItem
            {
                Id = job.Id,
                Title = string.IsNullOrWhiteSpace(job.ChannelName) ? $"Channel {job.ChannelId}" : job.ChannelName,
                Status = job.Status,
                Detail = BuildRecordingDetail(job),
                OutputPath = job.OutputPath,
                FileSizeBytes = ExistingFileSize(job.OutputPath, job.FileSizeBytes),
                PlaybackContentId = -job.Id,
                PlaybackContentType = PlaybackContentType.Channel,
                CanPlay = canPlay,
                CanRetry = string.Equals(job.Status, MediaJobStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(job.Status, MediaJobStatuses.Canceled, StringComparison.OrdinalIgnoreCase),
                CanCancel = string.Equals(job.Status, MediaJobStatuses.Running, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(job.Status, MediaJobStatuses.Pending, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(job.Status, MediaJobStatuses.Scheduled, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(job.Status, MediaJobStatuses.Retrying, StringComparison.OrdinalIgnoreCase),
                CanDelete = !string.Equals(job.Status, MediaJobStatuses.Running, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static MediaLibraryDownloadItem BuildDownloadItem(DownloadJob job)
        {
            var canPlay = string.Equals(job.Status, MediaJobStatuses.Completed, StringComparison.OrdinalIgnoreCase) &&
                          File.Exists(job.OutputPath);

            return new MediaLibraryDownloadItem
            {
                Id = job.Id,
                Title = job.Title,
                Subtitle = job.Subtitle,
                Status = job.Status,
                Detail = BuildDownloadDetail(job),
                OutputPath = job.OutputPath,
                FileSizeBytes = ExistingFileSize(job.OutputPath, job.FileSizeBytes),
                PlaybackContentId = job.ContentId,
                PlaybackContentType = job.ContentType,
                CanPlay = canPlay,
                CanRetry = string.Equals(job.Status, MediaJobStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(job.Status, MediaJobStatuses.Canceled, StringComparison.OrdinalIgnoreCase),
                CanCancel = string.Equals(job.Status, MediaJobStatuses.Running, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(job.Status, MediaJobStatuses.Queued, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(job.Status, MediaJobStatuses.Retrying, StringComparison.OrdinalIgnoreCase),
                CanDelete = !string.Equals(job.Status, MediaJobStatuses.Running, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string BuildRecordingDetail(RecordingJob job)
        {
            return job.Status switch
            {
                MediaJobStatuses.Scheduled => $"Starts {job.StartTimeUtc.ToLocalTime():g} for {(job.EndTimeUtc - job.StartTimeUtc).TotalMinutes:0} min",
                MediaJobStatuses.Pending => $"Waiting to start at {job.StartTimeUtc.ToLocalTime():t}",
                MediaJobStatuses.Running => $"Recording until {job.EndTimeUtc.ToLocalTime():t}",
                MediaJobStatuses.Retrying => $"Retrying at {job.NextRetryAtUtc?.ToLocalTime():t} • {job.LastError}",
                MediaJobStatuses.Completed => $"Completed {job.CompletedAtUtc?.ToLocalTime():g} • {FormatBytes(job.FileSizeBytes)}",
                MediaJobStatuses.Failed => string.IsNullOrWhiteSpace(job.LastError) ? "Failed" : $"Failed • {job.LastError}",
                MediaJobStatuses.Canceled => "Canceled",
                _ => job.Status
            };
        }

        private static string BuildDownloadDetail(DownloadJob job)
        {
            return job.Status switch
            {
                MediaJobStatuses.Queued => $"Queued {job.RequestedAtUtc.ToLocalTime():g}",
                MediaJobStatuses.Running => "Downloading now",
                MediaJobStatuses.Retrying => $"Retrying at {job.NextRetryAtUtc?.ToLocalTime():t} • {job.LastError}",
                MediaJobStatuses.Completed => $"Completed {job.CompletedAtUtc?.ToLocalTime():g} • {FormatBytes(job.FileSizeBytes)}",
                MediaJobStatuses.Failed => string.IsNullOrWhiteSpace(job.LastError) ? "Failed" : $"Failed • {job.LastError}",
                MediaJobStatuses.Canceled => "Canceled",
                _ => job.Status
            };
        }

        private static DateTime NormalizeLocalTime(DateTime localTime)
        {
            return localTime.Kind switch
            {
                DateTimeKind.Utc => localTime,
                DateTimeKind.Local => localTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(localTime, DateTimeKind.Local).ToUniversalTime()
            };
        }

        private static string BuildOutputPath(string rootPath, string folderName, string title, DateTime timestampUtc, string extension)
        {
            var baseDirectory = Path.Combine(rootPath, folderName);
            Directory.CreateDirectory(baseDirectory);

            var safeTitle = SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "Untitled" : title);
            var suffix = timestampUtc.ToLocalTime().ToString("yyyyMMdd-HHmm");
            var fileName = $"{safeTitle}-{suffix}{extension}";
            var outputPath = Path.Combine(baseDirectory, fileName);
            var duplicateIndex = 1;
            while (File.Exists(outputPath) || File.Exists(outputPath + ".partial"))
            {
                outputPath = Path.Combine(baseDirectory, $"{safeTitle}-{suffix}-{duplicateIndex}{extension}");
                duplicateIndex++;
            }

            return outputPath;
        }

        private static string SanitizeFileName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var cleaned = new string(value
                .Trim()
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
        }

        private static string InferFileExtension(string streamUrl, string fallbackExtension)
        {
            if (Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri))
            {
                var extension = Path.GetExtension(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 8)
                {
                    return extension;
                }
            }

            var localExtension = Path.GetExtension(streamUrl);
            return !string.IsNullOrWhiteSpace(localExtension) && localExtension.Length <= 8
                ? localExtension
                : fallbackExtension;
        }

        private static void FinalizeOutput(string tempPath, string outputPath, out long fileSizeBytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            File.Move(tempPath, outputPath, overwrite: true);
            fileSizeBytes = new FileInfo(outputPath).Length;
        }

        private static long ExistingFileSize(string outputPath, long fallbackBytes)
        {
            return File.Exists(outputPath)
                ? new FileInfo(outputPath).Length
                : fallbackBytes;
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

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        private void RaiseJobsChanged()
        {
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _loopCancellationTokenSource?.Cancel();
            foreach (var tokenSource in _activeRecordingTokens.Values.ToList())
            {
                tokenSource.Cancel();
            }

            foreach (var tokenSource in _activeDownloadTokens.Values.ToList())
            {
                tokenSource.Cancel();
            }

            try
            {
                _processingLoopTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _loopCancellationTokenSource?.Dispose();
            _processGate.Dispose();
        }
    }
}
