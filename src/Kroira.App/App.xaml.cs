using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Kroira.App.Data;

namespace Kroira.App
{
    public partial class App : Application
    {
        private Window _window;
        public Window MainWindow => _window;

        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();
        }

        public IServiceProvider Services { get; }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
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

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddDbContext<AppDbContext>();

            // Core UI ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SourceOnboardingViewModel>();
            services.AddTransient<SourceListViewModel>();
            services.AddTransient<ChannelBrowserViewModel>();

            // Foundation Infrastructure
            services.AddSingleton<IEntitlementService, MockEntitlementService>();
            services.AddSingleton<IWindowManagerService, WindowManagerService>();
            services.AddSingleton<IInputInterceptorService, InputInterceptorService>();
            services.AddSingleton<Kroira.App.Services.Playback.IPlaybackEngine, Kroira.App.Services.Playback.LibVlcPlaybackEngine>();
            services.AddSingleton<Kroira.App.Services.Parsing.IM3uParserService, Kroira.App.Services.Parsing.M3uParserService>();

            return services.BuildServiceProvider();
        }
    }
}
