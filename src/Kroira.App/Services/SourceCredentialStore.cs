#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ISourceCredentialProtector
    {
        string Scheme { get; }
        string Protect(string plaintext);
        string Unprotect(string protectedValue);
    }

    public interface ISourceCredentialStore
    {
        Task<SourceCredential?> GetCredentialAsync(
            AppDbContext db,
            int sourceProfileId,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default);

        Task ApplyProtectedValuesAsync(
            AppDbContext db,
            SourceCredential? credential,
            CancellationToken cancellationToken = default);

        Task ProtectCredentialAsync(
            AppDbContext db,
            SourceCredential credential,
            CancellationToken cancellationToken = default);

        Task<SourceCredentialProtectionMigrationResult> ProtectExistingCredentialsAsync(
            AppDbContext db,
            CancellationToken cancellationToken = default);

        Task DeleteProtectedCredentialsAsync(
            AppDbContext db,
            int sourceProfileId,
            CancellationToken cancellationToken = default);
    }

    public sealed class SourceCredentialProtectionMigrationResult
    {
        public int MigratedCount { get; init; }
        public int RemovedStaleSecretCount { get; init; }
        public int FailedCount { get; init; }
    }

    public sealed class DpapiSourceCredentialProtector : ISourceCredentialProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kroira.SourceCredentialStore.v1");

        public string Scheme => "dpapi-current-user-v1";

        public string Protect(string plaintext)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("DPAPI source credential protection requires Windows.");
            }

            var bytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Unprotect(string protectedValue)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("DPAPI source credential protection requires Windows.");
            }

            var protectedBytes = Convert.FromBase64String(protectedValue ?? string.Empty);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public sealed class SourceCredentialStore : ISourceCredentialStore
    {
        private static readonly IReadOnlyList<CredentialField> Fields = new[]
        {
            new CredentialField("Url", nameof(SourceCredential.Url), credential => credential.Url, (credential, value) => credential.Url = value),
            new CredentialField("Username", nameof(SourceCredential.Username), credential => credential.Username, (credential, value) => credential.Username = value),
            new CredentialField("Password", nameof(SourceCredential.Password), credential => credential.Password, (credential, value) => credential.Password = value),
            new CredentialField("ManualEpgUrl", nameof(SourceCredential.EpgUrl), credential => credential.ManualEpgUrl, (credential, value) => credential.ManualEpgUrl = value),
            new CredentialField("DetectedEpgUrl", nameof(SourceCredential.DetectedEpgUrl), credential => credential.DetectedEpgUrl, (credential, value) => credential.DetectedEpgUrl = value),
            new CredentialField("FallbackEpgUrls", nameof(SourceCredential.FallbackEpgUrls), credential => credential.FallbackEpgUrls, (credential, value) => credential.FallbackEpgUrls = value),
            new CredentialField("ProxyUrl", nameof(SourceCredential.ProxyUrl), credential => credential.ProxyUrl, (credential, value) => credential.ProxyUrl = value),
            new CredentialField("CompanionUrl", nameof(SourceCredential.CompanionUrl), credential => credential.CompanionUrl, (credential, value) => credential.CompanionUrl = value),
            new CredentialField("StalkerMacAddress", nameof(SourceCredential.StalkerMacAddress), credential => credential.StalkerMacAddress, (credential, value) => credential.StalkerMacAddress = value),
            new CredentialField("StalkerDeviceId", nameof(SourceCredential.StalkerDeviceId), credential => credential.StalkerDeviceId, (credential, value) => credential.StalkerDeviceId = value),
            new CredentialField("StalkerSerialNumber", nameof(SourceCredential.StalkerSerialNumber), credential => credential.StalkerSerialNumber, (credential, value) => credential.StalkerSerialNumber = value),
            new CredentialField("StalkerApiUrl", nameof(SourceCredential.StalkerApiUrl), credential => credential.StalkerApiUrl, (credential, value) => credential.StalkerApiUrl = value)
        };

        private readonly ISourceCredentialProtector _protector;

        public SourceCredentialStore(ISourceCredentialProtector protector)
        {
            _protector = protector;
        }

        public static SourceCredentialStore CreateDefault()
        {
            return new SourceCredentialStore(new DpapiSourceCredentialProtector());
        }

        public async Task<SourceCredential?> GetCredentialAsync(
            AppDbContext db,
            int sourceProfileId,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default)
        {
            var query = asNoTracking
                ? db.SourceCredentials.AsNoTracking()
                : db.SourceCredentials.AsQueryable();
            var credential = await query.FirstOrDefaultAsync(
                item => item.SourceProfileId == sourceProfileId,
                cancellationToken);
            await ApplyProtectedValuesAsync(db, credential, cancellationToken);
            return credential;
        }

        public async Task ApplyProtectedValuesAsync(
            AppDbContext db,
            SourceCredential? credential,
            CancellationToken cancellationToken = default)
        {
            if (credential == null)
            {
                return;
            }

            var secrets = await db.SourceProtectedCredentialSecrets
                .AsNoTracking()
                .Where(secret => secret.SourceProfileId == credential.SourceProfileId)
                .ToDictionaryAsync(secret => secret.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
            if (secrets.Count == 0)
            {
                return;
            }

            foreach (var field in Fields)
            {
                if (!secrets.TryGetValue(field.Name, out var secret) ||
                    string.IsNullOrWhiteSpace(secret.ProtectedValue))
                {
                    continue;
                }

                try
                {
                    var value = _protector.Unprotect(secret.ProtectedValue);
                    field.Set(credential, value);
                    PreservePlaintextColumn(db, credential, field.EntityPropertyName, value);
                }
                catch (Exception ex)
                {
                    RuntimeEventLogger.Log(
                        "CREDENTIAL-STORE",
                        ex,
                        $"source_id={credential.SourceProfileId} protected credential field={field.Name} could not be read; plaintext fallback retained");
                }
            }
        }

        public async Task ProtectCredentialAsync(
            AppDbContext db,
            SourceCredential credential,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(credential);
            await UpsertProtectedValuesAsync(
                db,
                credential,
                overwriteExistingProtectedValues: true,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task<SourceCredentialProtectionMigrationResult> ProtectExistingCredentialsAsync(
            AppDbContext db,
            CancellationToken cancellationToken = default)
        {
            var credentials = await db.SourceCredentials
                .OrderBy(credential => credential.SourceProfileId)
                .ToListAsync(cancellationToken);

            var migrated = 0;
            var staleRemoved = 0;
            var failed = 0;
            foreach (var credential in credentials)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var change = await UpsertProtectedValuesAsync(
                        db,
                        credential,
                        overwriteExistingProtectedValues: false,
                        cancellationToken);
                    if (change.SecretUpsertedCount > 0)
                    {
                        migrated++;
                    }

                    staleRemoved += change.SecretRemovedCount;
                }
                catch (Exception ex)
                {
                    failed++;
                    RuntimeEventLogger.Log(
                        "CREDENTIAL-STORE",
                        ex,
                        $"source_id={credential.SourceProfileId} credential protection migration skipped");
                }
            }

            if (migrated > 0 || staleRemoved > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return new SourceCredentialProtectionMigrationResult
            {
                MigratedCount = migrated,
                RemovedStaleSecretCount = staleRemoved,
                FailedCount = failed
            };
        }

        public async Task DeleteProtectedCredentialsAsync(
            AppDbContext db,
            int sourceProfileId,
            CancellationToken cancellationToken = default)
        {
            var secrets = await db.SourceProtectedCredentialSecrets
                .Where(secret => secret.SourceProfileId == sourceProfileId)
                .ToListAsync(cancellationToken);
            if (secrets.Count == 0)
            {
                return;
            }

            db.SourceProtectedCredentialSecrets.RemoveRange(secrets);
            await db.SaveChangesAsync(cancellationToken);
        }

        private async Task<CredentialStoreChange> UpsertProtectedValuesAsync(
            AppDbContext db,
            SourceCredential credential,
            bool overwriteExistingProtectedValues,
            CancellationToken cancellationToken)
        {
            var existing = await db.SourceProtectedCredentialSecrets
                .Where(secret => secret.SourceProfileId == credential.SourceProfileId)
                .ToDictionaryAsync(secret => secret.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var upserted = 0;
            var removed = 0;
            var now = DateTime.UtcNow;
            foreach (var field in Fields)
            {
                var value = field.Get(credential) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (existing.TryGetValue(field.Name, out var staleSecret))
                    {
                        db.SourceProtectedCredentialSecrets.Remove(staleSecret);
                        removed++;
                    }

                    continue;
                }

                if (!existing.TryGetValue(field.Name, out var secret))
                {
                    secret = new SourceProtectedCredentialSecret
                    {
                        SourceProfileId = credential.SourceProfileId,
                        Name = field.Name
                    };
                    db.SourceProtectedCredentialSecrets.Add(secret);
                }
                else if (!overwriteExistingProtectedValues &&
                         !string.IsNullOrWhiteSpace(secret.ProtectedValue))
                {
                    continue;
                }

                var protectedValue = _protector.Protect(value);
                secret.ProtectedValue = protectedValue;
                secret.ProtectionScheme = _protector.Scheme;
                secret.UpdatedAtUtc = now;
                upserted++;
            }

            return new CredentialStoreChange(upserted, removed);
        }

        private static void PreservePlaintextColumn(
            AppDbContext db,
            SourceCredential credential,
            string entityPropertyName,
            string value)
        {
            var entry = db.Entry(credential);
            if (entry.State == EntityState.Detached)
            {
                return;
            }

            var property = entry.Property(entityPropertyName);
            property.OriginalValue = value;
            property.IsModified = false;
        }

        private sealed record CredentialField(
            string Name,
            string EntityPropertyName,
            Func<SourceCredential, string?> Get,
            Action<SourceCredential, string> Set);

        private sealed record CredentialStoreChange(int SecretUpsertedCount, int SecretRemovedCount);
    }
}
