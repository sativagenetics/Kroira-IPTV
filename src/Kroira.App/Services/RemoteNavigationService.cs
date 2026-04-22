#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public sealed class RemoteNavigationSettings
    {
        public bool IsRemoteModeEnabled { get; set; } = true;
    }

    public interface IRemoteNavigationService
    {
        bool IsRemoteModeEnabled { get; }
        event EventHandler? StateChanged;
        Task InitializeAsync();
        Task SetRemoteModeEnabledAsync(bool enabled);
    }

    public sealed class RemoteNavigationService : IRemoteNavigationService
    {
        private const string KeyPrefix = "RemoteNavigation.Profile.";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly IServiceProvider _serviceProvider;
        private bool _initialized;

        public RemoteNavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public bool IsRemoteModeEnabled { get; private set; } = true;

        public event EventHandler? StateChanged;

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var profileId = await profileService.GetActiveProfileIdAsync(db);
            var settings = await LoadAsync(db, profileId);
            IsRemoteModeEnabled = settings.IsRemoteModeEnabled;
            _initialized = true;
        }

        public async Task SetRemoteModeEnabledAsync(bool enabled)
        {
            await InitializeAsync();
            if (IsRemoteModeEnabled == enabled)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var profileId = await profileService.GetActiveProfileIdAsync(db);
            var key = BuildKey(profileId);
            var json = JsonSerializer.Serialize(
                new RemoteNavigationSettings
                {
                    IsRemoteModeEnabled = enabled
                },
                JsonOptions);

            var setting = await db.AppSettings.FirstOrDefaultAsync(item => item.Key == key);
            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = json
                });
            }
            else
            {
                setting.Value = json;
            }

            await db.SaveChangesAsync();
            IsRemoteModeEnabled = enabled;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static async Task<RemoteNavigationSettings> LoadAsync(AppDbContext db, int profileId)
        {
            var key = BuildKey(profileId);
            var json = await db.AppSettings
                .Where(item => item.Key == key)
                .Select(item => item.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                return new RemoteNavigationSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<RemoteNavigationSettings>(json, JsonOptions) ?? new RemoteNavigationSettings();
            }
            catch
            {
                return new RemoteNavigationSettings();
            }
        }

        private static string BuildKey(int profileId)
        {
            return KeyPrefix + Math.Max(0, profileId);
        }
    }
}
