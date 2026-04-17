using System;
using Kroira.App.Data;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.IO;

namespace Kroira.App
{
    public partial class App : Application
    {
        private Window _window;
        public Window MainWindow => _window;

        public App()
        {
            try
            {
                this.InitializeComponent();
                Services = ConfigureServices();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                WriteStartupError(ex);
                throw;
            }
        }

        public IServiceProvider Services { get; }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                using (var scope = Services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    DatabaseBootstrapper.Initialize(dbContext);
                }

                _window = new MainWindow();

                var winManager = Services.GetRequiredService<IWindowManagerService>();
                winManager.Initialize(_window);

                var inputManager = Services.GetRequiredService<IInputInterceptorService>();
                inputManager.Initialize(_window);

                _window.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                WriteStartupError(ex);
                ShowStartupError(ex);
            }
        }

        // Temporary startup diagnostics. Remove after the startup failure is identified.
        private static string WriteStartupError(Exception ex)
        {
            var errorDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Kroira");
            Directory.CreateDirectory(errorDirectory);

            var errorPath = Path.Combine(errorDirectory, "startup-error.txt");
            File.WriteAllText(errorPath, ex.ToString());
            return errorPath;
        }

        private void ShowStartupError(Exception ex)
        {
            try
            {
                _window = new Window
                {
                    Title = "Kroira startup error",
                    Content = new TextBox
                    {
                        Text = ex.ToString(),
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap
                    }
                };

                _window.Activate();
            }
            catch
            {
                // File logging above is the reliable fallback.
            }
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddDbContext<AppDbContext>();

            // Core UI ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<HomeViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SourceOnboardingViewModel>();
            services.AddTransient<SourceListViewModel>();
            services.AddTransient<ChannelBrowserViewModel>();
            services.AddTransient<FavoritesViewModel>();
            services.AddTransient<ContinueWatchingViewModel>();
            services.AddTransient<MoviesViewModel>();
            services.AddTransient<SeriesViewModel>();
            services.AddTransient<ChannelsPageViewModel>();

            // Foundation Infrastructure
            services.AddSingleton<IEntitlementService, MockEntitlementService>();
            services.AddSingleton<IWindowManagerService, WindowManagerService>();
            services.AddSingleton<IInputInterceptorService, InputInterceptorService>();
            services.AddTransient<Kroira.App.Services.Playback.IPlaybackEngine, Kroira.App.Services.Playback.MpvPlaybackEngine>();
            services.AddSingleton<Kroira.App.Services.Parsing.IM3uParserService, Kroira.App.Services.Parsing.M3uParserService>();
            services.AddSingleton<Kroira.App.Services.Parsing.IXmltvParserService, Kroira.App.Services.Parsing.XmltvParserService>();
            services.AddSingleton<Kroira.App.Services.Parsing.IXtreamParserService, Kroira.App.Services.Parsing.XtreamParserService>();

            return services.BuildServiceProvider();
        }
    }
}
