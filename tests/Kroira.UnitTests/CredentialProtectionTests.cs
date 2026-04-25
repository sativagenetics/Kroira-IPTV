using System.Text;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class CredentialProtectionTests
{
    [TestMethod]
    public void Redactor_MasksCredentialBearingUrlsAndLooseSecrets()
    {
        var redactor = new SensitiveDataRedactionService();

        var redacted = redactor.RedactLooseText(
            "open http://alice:secret@iptv.example/live/alice/secret/12.ts?username=alice&password=secret&token=tok123&api_key=key123 " +
            "mac=00:1A:79:AA:BB:CC device_id=dev123 Bearer abcdefghijk");

        Assert.IsFalse(redacted.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(redacted.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(redacted.Contains("tok123", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(redacted.Contains("key123", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(redacted.Contains("dev123", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(redacted.Contains("AA:BB", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(redacted.Contains("abcdefghijk", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(redacted, "iptv.example");
    }

    [TestMethod]
    public void Redactor_MasksContiguousMacLikeValues()
    {
        var redactor = new SensitiveDataRedactionService();

        Assert.AreEqual("00:1A:**:**:**:CC", redactor.RedactMacAddress("001A79AABBCC"));
    }

    [TestMethod]
    public async Task Store_ProtectRetrieveRoundtripWithFakeProtector()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedCredentialAsync(db);
        var store = CreateStore();
        var credential = await db.SourceCredentials.SingleAsync(item => item.SourceProfileId == sourceId);

        await store.ProtectCredentialAsync(db, credential);
        db.ChangeTracker.Clear();

        var loaded = await store.GetCredentialAsync(db, sourceId, asNoTracking: true);
        var protectedRows = await db.SourceProtectedCredentialSecrets
            .Where(secret => secret.SourceProfileId == sourceId)
            .ToListAsync();

        Assert.IsNotNull(loaded);
        Assert.AreEqual("https://iptv.example", loaded.Url);
        Assert.AreEqual("alice", loaded.Username);
        Assert.AreEqual("secret", loaded.Password);
        Assert.IsTrue(protectedRows.Count >= 3);
        Assert.IsFalse(protectedRows.Any(row => row.ProtectedValue.Contains("secret", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Store_MigrationCreatesProtectedCopyWithoutRemovingPlaintext()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedCredentialAsync(db);
        var store = CreateStore();

        var result = await store.ProtectExistingCredentialsAsync(db);
        db.ChangeTracker.Clear();

        var credential = await db.SourceCredentials.SingleAsync(item => item.SourceProfileId == sourceId);
        Assert.AreEqual(1, result.MigratedCount);
        Assert.AreEqual(0, result.FailedCount);
        Assert.AreEqual("secret", credential.Password);
        Assert.IsTrue(await db.SourceProtectedCredentialSecrets.AnyAsync(secret => secret.SourceProfileId == sourceId));
    }

    [TestMethod]
    public async Task Store_ProtectedValueTakesPrecedenceWithoutOverwritingPlaintextFallback()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedCredentialAsync(db, password: "plain-secret");
        var protector = new FakeSourceCredentialProtector();
        var store = new SourceCredentialStore(protector);
        db.SourceProtectedCredentialSecrets.Add(new SourceProtectedCredentialSecret
        {
            SourceProfileId = sourceId,
            Name = "Password",
            ProtectionScheme = protector.Scheme,
            ProtectedValue = protector.Protect("protected-secret"),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await store.GetCredentialAsync(db, sourceId);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("protected-secret", loaded.Password);

        loaded.DetectedEpgUrl = "https://guide.example/xmltv.php";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var raw = await db.SourceCredentials.SingleAsync(item => item.SourceProfileId == sourceId);
        Assert.AreEqual("plain-secret", raw.Password);
        Assert.AreEqual("https://guide.example/xmltv.php", raw.DetectedEpgUrl);
    }

    [TestMethod]
    public async Task Store_MigrationDoesNotOverwriteExistingProtectedValue()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedCredentialAsync(db, password: "plain-secret");
        var protector = new FakeSourceCredentialProtector();
        var store = new SourceCredentialStore(protector);
        db.SourceProtectedCredentialSecrets.Add(new SourceProtectedCredentialSecret
        {
            SourceProfileId = sourceId,
            Name = "Password",
            ProtectionScheme = protector.Scheme,
            ProtectedValue = protector.Protect("protected-secret"),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await store.ProtectExistingCredentialsAsync(db);
        db.ChangeTracker.Clear();

        var loaded = await store.GetCredentialAsync(db, sourceId, asNoTracking: true);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("protected-secret", loaded.Password);
    }

    [TestMethod]
    public async Task Store_PlaintextFallbackRemainsWhenProtectedCopyIsMissing()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedCredentialAsync(db, username: "fallback-user");
        var store = CreateStore();

        var loaded = await store.GetCredentialAsync(db, sourceId, asNoTracking: true);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("fallback-user", loaded.Username);
    }

    [TestMethod]
    public async Task Store_DeleteCleanupRemovesProtectedRecords()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedCredentialAsync(db);
        var store = CreateStore();
        var credential = await db.SourceCredentials.SingleAsync(item => item.SourceProfileId == sourceId);
        await store.ProtectCredentialAsync(db, credential);

        await store.DeleteProtectedCredentialsAsync(db, sourceId);

        Assert.AreEqual(0, await db.SourceProtectedCredentialSecrets.CountAsync(secret => secret.SourceProfileId == sourceId));
    }

    private static SourceCredentialStore CreateStore()
    {
        return new SourceCredentialStore(new FakeSourceCredentialProtector());
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AppDbContext> CreateDatabaseAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<int> SeedCredentialAsync(
        AppDbContext db,
        string username = "alice",
        string password = "secret")
    {
        var profile = new SourceProfile
        {
            Name = "Protected Source",
            Type = SourceType.Xtream
        };
        db.SourceProfiles.Add(profile);
        await db.SaveChangesAsync();

        db.SourceCredentials.Add(new SourceCredential
        {
            SourceProfileId = profile.Id,
            Url = "https://iptv.example",
            Username = username,
            Password = password,
            ManualEpgUrl = "https://guide.example/manual.xml?token=manual-token",
            ProxyUrl = "https://proxy.example/?key=proxy-key",
            CompanionUrl = "https://companion.example",
            StalkerMacAddress = "00:1A:79:AA:BB:CC"
        });
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private sealed class FakeSourceCredentialProtector : ISourceCredentialProtector
    {
        public string Scheme => "fake-v1";

        public string Protect(string plaintext)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"protected:{plaintext}"));
        }

        public string Unprotect(string protectedValue)
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
            return decoded.StartsWith("protected:", StringComparison.Ordinal)
                ? decoded["protected:".Length..]
                : decoded;
        }
    }
}
