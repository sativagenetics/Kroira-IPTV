#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Composition;
using Kroira.App.Data;
using Kroira.App.Services;
using Kroira.App.Services.Metadata;
using Kroira.App.Services.Playback;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App
{
    public partial class App : Application
    {
        private static readonly TimeSpan DeferredRuntimeMaintenanceDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan StartupMetadataDelay = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan StartupMetadataBudget = TimeSpan.FromSeconds(15);
        private readonly IServiceProvider _services;
        private Window? _window;
        private readonly CancellationTokenSource _startupMetadataCts = new();
        private readonly CancellationTokenSource _startupMaintenanceCts = new();
        public Window? MainWindow => _window;

        public IServiceProvider Services => _services;

        private static readonly string ErrorDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira");

        private static readonly string StartupLogPath =
            Path.Combine(ErrorDirectory, "startup-log.txt");

        private static readonly string StartupErrorPath =
            Path.Combine(ErrorDirectory, "startup-error.txt");

        public App()
        {
            Directory.CreateDirectory(ErrorDirectory);
            SafeAppendLog("APP 01: constructor entered");

            RegisterGlobalExceptionHandlers();

            try
            {
                SafeAppendLog("APP 02: before InitializeComponent");
                InitializeComponent();
                SafeAppendLog("APP 03: after InitializeComponent");

                SafeAppendLog("APP 04: before ConfigureServices");
                _services = ConfigureServices();
                SafeAppendLog("APP 05: after ConfigureServices");
            }
            catch (Exception ex)
            {
                SafeAppendLog("APP FATAL: exception in constructor");
                SafeLogException("APP CONSTRUCTOR EXCEPTION", ex);
                throw;
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            SafeAppendLog("APP 06: OnLaunched entered");

            try
            {
                RunFatalStartupStep("APP 07: database bootstrap", () =>
                {
                    using var scope = Services.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    DatabaseBootstrapper.Initialize(dbContext);
                });

                RunRecoverableStartupStep("APP 08: runtime startup repair", () =>
                {
                    Services.GetRequiredService<IRuntimeMaintenanceService>()
                        .RunStartupRepairAsync(_startupMaintenanceCts.Token)
                        .GetAwaiter()
                        .GetResult();
                });

                SafeAppendLog("APP 09: before MainWindow ctor");
                _window = new MainWindow();
                SafeAppendLog("APP 10: after MainWindow ctor");
                _window.Closed += (_, _) =>
                {
                    CancelStartupMaintenance("window closed");
                    CancelStartupMetadataBackfill("window closed");
                    try
                    {
                        Services.GetRequiredService<ISourceAutoRefreshService>().Stop();
                    }
                    catch (Exception ex)
                    {
                        SafeLogException("APP AUTORF STOP ERROR", ex);
                    }
                };

                RunRecoverableStartupStep("APP 11: appearance init", () =>
                {
                    Services.GetRequiredService<IAppAppearanceService>().InitializeAsync().GetAwaiter().GetResult();
                });
                RunRecoverableStartupStep("APP 12: media jobs start", () =>
                {
                    Services.GetRequiredService<IMediaJobService>().Start();
                });
                RunRecoverableStartupStep("APP 12A: remote navigation init", () =>
                {
                    Services.GetRequiredService<IRemoteNavigationService>().InitializeAsync().GetAwaiter().GetResult();
                });
                RunRecoverableStartupStep("APP 13: auto refresh start", () =>
                {
                    Services.GetRequiredService<ISourceAutoRefreshService>().Start();
                });
                RunRecoverableStartupStep("APP 14: window manager init", () =>
                {
                    var winManager = Services.GetRequiredService<IWindowManagerService>();
                    winManager.Initialize(_window);
                });
                RunRecoverableStartupStep("APP 15: input interceptor init", () =>
                {
                    var inputManager = Services.GetRequiredService<IInputInterceptorService>();
                    inputManager.Initialize(_window);
                });

                SafeAppendLog("APP 16: before window.Activate");
                _window.Activate();
                SafeAppendLog("APP 17: after window.Activate");

                if (_window is MainWindow mainWindow)
                {
                    SafeAppendLog("APP 18: before queue initial navigation");
                    mainWindow.QueueInitialNavigation();
                    SafeAppendLog("APP 19: after queue initial navigation");
                }

                ScheduleDeferredRuntimeMaintenance();
                ScheduleMetadataBackfillAfterShellVisible();
            }
            catch (Exception ex)
            {
                SafeAppendLog("APP FATAL: exception in OnLaunched");
                SafeLogException("APP ONLAUNCHED EXCEPTION", ex);
                ShowStartupErrorWindow(ex);
            }
        }

        private void ScheduleDeferredRuntimeMaintenance()
        {
            SafeAppendLog("APP RM 00: scheduling deferred runtime maintenance after shell is visible");
            _ = RunDeferredRuntimeMaintenanceAsync(_startupMaintenanceCts.Token);
        }

        private async Task RunDeferredRuntimeMaintenanceAsync(CancellationToken cancellationToken)
        {
            try
            {
                SafeAppendLog($"APP RM 01: delaying runtime maintenance by {DeferredRuntimeMaintenanceDelay.TotalSeconds:0}s");
                await Task.Delay(DeferredRuntimeMaintenanceDelay, cancellationToken);
                await Services.GetRequiredService<IRuntimeMaintenanceService>().RunDeferredRepairAsync(cancellationToken);
                SafeAppendLog("APP RM 02: deferred runtime maintenance completed");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SafeAppendLog("APP RM 03: deferred runtime maintenance cancelled");
            }
            catch (Exception ex)
            {
                SafeLogException("APP RUNTIME MAINTENANCE ERROR", ex);
            }
        }

        private void ScheduleMetadataBackfillAfterShellVisible()
        {
            SafeAppendLog("APP TMDB 00: scheduling metadata backfill after shell is visible");
            _ = RunMetadataBackfillAfterShellVisibleAsync(_startupMetadataCts.Token);
        }

        private async Task RunMetadataBackfillAfterShellVisibleAsync(CancellationToken cancellationToken)
        {
            try
            {
                SafeAppendLog($"APP TMDB 01: delaying metadata backfill by {StartupMetadataDelay.TotalSeconds:0}s");
                await Task.Delay(StartupMetadataDelay, cancellationToken);

                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tmdb = scope.ServiceProvider.GetRequiredService<ITmdbMetadataService>();

                if (!await tmdb.HasCredentialAsync(dbContext))
                {
                    SafeAppendLog("APP TMDB 02: metadata backfill skipped; no TMDb credential");
                    return;
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(StartupMetadataBudget);

                SafeAppendLog($"APP TMDB 03: metadata backfill starting with {StartupMetadataBudget.TotalSeconds:0}s budget");
                await tmdb.BackfillMissingMetadataAsync(dbContext, maxMovies: 24, maxSeries: 16, timeoutCts.Token);
                SafeAppendLog("APP TMDB 04: metadata backfill completed");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SafeAppendLog("APP TMDB 05: metadata backfill cancelled");
            }
            catch (OperationCanceledException)
            {
                SafeAppendLog("APP TMDB 06: metadata backfill stopped after startup budget");
            }
            catch (Exception ex)
            {
                SafeLogException("APP TMDB BACKFILL ERROR", ex);
            }
        }

        private void CancelStartupMetadataBackfill(string reason)
        {
            if (_startupMetadataCts.IsCancellationRequested)
            {
                return;
            }

            SafeAppendLog($"APP TMDB 07: cancelling metadata backfill ({reason})");
            _startupMetadataCts.Cancel();
        }

        private void CancelStartupMaintenance(string reason)
        {
            if (_startupMaintenanceCts.IsCancellationRequested)
            {
                return;
            }

            SafeAppendLog($"APP RM 04: cancelling runtime maintenance ({reason})");
            _startupMaintenanceCts.Cancel();
        }

        private void RegisterGlobalExceptionHandlers()
        {
            SafeAppendLog("APP G01: registering global exception handlers");

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                SafeAppendLog("APP G02: AppDomain.CurrentDomain.UnhandledException");
                if (e.ExceptionObject is Exception ex)
                {
                    SafeLogException("APPDOMAIN UNHANDLED", ex);
                }
                else
                {
                    SafeAppendLog($"APPDOMAIN UNHANDLED (non-Exception): {e.ExceptionObject}");
                }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                SafeAppendLog("APP G03: TaskScheduler.UnobservedTaskException");
                SafeLogException("TASK UNOBSERVED", e.Exception);
            };

            UnhandledException += (_, e) =>
            {
                SafeAppendLog("APP G04: Application.UnhandledException");
                SafeLogException("WINUI UNHANDLED", e.Exception);

                try
                {
                    ShowStartupErrorWindow(e.Exception);
                }
                catch (Exception windowEx)
                {
                    SafeLogException("ERROR SHOWING STARTUP ERROR WINDOW", windowEx);
                }

                // Tanı amaçlı: sessiz çöküş yerine hata penceresi görelim.
                e.Handled = true;
            };
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddDbContext<AppDbContext>();

            services.AddTransient<HomeViewModel>();
            services.AddTransient<ProfileViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<MediaLibraryViewModel>();
            services.AddTransient<SourceOnboardingViewModel>();
            services.AddTransient<SourceListViewModel>();
            services.AddTransient<EpgCenterViewModel>();
            services.AddTransient<ChannelBrowserViewModel>();
            services.AddTransient<FavoritesViewModel>();
            services.AddTransient<ContinueWatchingViewModel>();
            services.AddTransient<MoviesViewModel>();
            services.AddTransient<SeriesViewModel>();
            services.AddTransient<ChannelsPageViewModel>();

            services.AddSingleton<IEntitlementService, MockEntitlementService>();
            services.AddSingleton<ILibraryWatchStateService, LibraryWatchStateService>();
            services.AddSingleton<IProfileStateService, ProfileStateService>();
            services.AddSingleton<IAppAppearanceService, AppAppearanceService>();
            services.AddSingleton<IWindowManagerService, WindowManagerService>();
            services.AddSingleton<IPictureInPictureService, PictureInPictureService>();
            services.AddSingleton<IInputInterceptorService, InputInterceptorService>();
            services.AddSingleton<IPlayerPreferencesService, PlayerPreferencesService>();
            services.AddSingleton<ITmdbMetadataService, TmdbMetadataService>();
            services.AddSingleton<ICatalogDeduplicationService, CatalogDeduplicationService>();
            services.AddSingleton<ICatalogTaxonomyService, CatalogTaxonomyService>();
            services.AddSingleton<ICatalogSurfaceCountService, CatalogSurfaceCountService>();
            services.AddSingleton<IHomeRecommendationService, HomeRecommendationService>();
            services.AddSingleton<ILiveGuideService, LiveGuideService>();
            services.AddSingleton<IEpgGuideTimelineService, EpgGuideTimelineService>();
            services.AddSingleton<IEpgManualMatchService, EpgManualMatchService>();
            services.AddSingleton<IEpgCoverageReportService, EpgCoverageReportService>();
            services.AddSingleton<ISurfaceStateService, SurfaceStateService>();
            services.AddSingleton<IBackupPackageService, BackupPackageService>();
            services.AddSingleton<IMediaJobService, MediaJobService>();
            services.AddKroiraPipelineServices();

            return services.BuildServiceProvider();
        }

        internal void LogStartupCheckpoint(string message)
        {
            SafeAppendLog(message);
        }

        internal void LogStartupException(string title, Exception ex)
        {
            SafeLogException(title, ex);
        }

        private void RunFatalStartupStep(string checkpoint, Action action)
        {
            SafeAppendLog($"{checkpoint} start");
            action();
            SafeAppendLog($"{checkpoint} end");
        }

        private void RunRecoverableStartupStep(string checkpoint, Action action)
        {
            SafeAppendLog($"{checkpoint} start");
            try
            {
                action();
                SafeAppendLog($"{checkpoint} end");
            }
            catch (Exception ex)
            {
                SafeLogException($"{checkpoint} failed", ex);
            }
        }

        private void ShowStartupErrorWindow(Exception ex)
        {
            try
            {
                SafeAppendLog("APP E01: showing startup error window");

                var text =
                    "Kroira startup error\n\n" +
                    ex + "\n\n" +
                    $"Startup log: {StartupLogPath}\n" +
                    $"Startup error: {StartupErrorPath}";

                var errorWindow = new Window
                {
                    Title = "Kroira startup error",
                    Content = new Grid
                    {
                        Padding = new Thickness(16),
                        Children =
                        {
                            new ScrollViewer
                            {
                                Content = new TextBox
                                {
                                    Text = text,
                                    IsReadOnly = true,
                                    AcceptsReturn = true,
                                    TextWrapping = TextWrapping.Wrap
                                }
                            }
                        }
                    }
                };

                errorWindow.Activate();
                _window = errorWindow;
            }
            catch (Exception ex2)
            {
                SafeLogException("APP E02: failed to show startup error window", ex2);
            }
        }

        private static void SafeLogException(string title, Exception ex)
        {
            try
            {
                var text =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}{Environment.NewLine}" +
                    ex + Environment.NewLine +
                    new string('-', 80) + Environment.NewLine;

                Debug.WriteLine(text);
                File.AppendAllText(StartupErrorPath, text);
                File.AppendAllText(StartupLogPath, text);
            }
            catch
            {
                // Tanı kodu, burada tekrar patlamasın.
            }
        }

        private static void SafeAppendLog(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                Debug.WriteLine(line);
                File.AppendAllText(StartupLogPath, line + Environment.NewLine);
            }
            catch
            {
                // Tanı kodu, burada tekrar patlamasın.
            }
        }
    }
}
