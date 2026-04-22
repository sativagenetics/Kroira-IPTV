using System;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<AppSetting> AppSettings { get; set; } = null!;
        public DbSet<AppProfile> AppProfiles { get; set; } = null!;
        public DbSet<FeatureEntitlement> FeatureEntitlements { get; set; } = null!;
        public DbSet<ParentalControlSetting> ParentalControlSettings { get; set; } = null!;
        public DbSet<SchemaVersion> SchemaVersions { get; set; } = null!;
        public DbSet<SourceProfile> SourceProfiles { get; set; } = null!;
        public DbSet<SourceCredential> SourceCredentials { get; set; } = null!;
        public DbSet<SourceSyncState> SourceSyncStates { get; set; } = null!;
        public DbSet<SourceHealthReport> SourceHealthReports { get; set; } = null!;
        public DbSet<SourceHealthComponent> SourceHealthComponents { get; set; } = null!;
        public DbSet<SourceHealthProbe> SourceHealthProbes { get; set; } = null!;
        public DbSet<SourceHealthIssue> SourceHealthIssues { get; set; } = null!;
        public DbSet<SourceChannelEnrichmentRecord> SourceChannelEnrichmentRecords { get; set; } = null!;
        public DbSet<LogicalOperationalState> LogicalOperationalStates { get; set; } = null!;
        public DbSet<LogicalOperationalCandidate> LogicalOperationalCandidates { get; set; } = null!;
        public DbSet<ChannelCategory> ChannelCategories { get; set; } = null!;
        public DbSet<Channel> Channels { get; set; } = null!;
        public DbSet<EpgProgram> EpgPrograms { get; set; } = null!;
        public DbSet<EpgSyncLog> EpgSyncLogs { get; set; } = null!;
        public DbSet<Movie> Movies { get; set; } = null!;
        public DbSet<Series> Series { get; set; } = null!;
        public DbSet<Season> Seasons { get; set; } = null!;
        public DbSet<Episode> Episodes { get; set; } = null!;
        public DbSet<Favorite> Favorites { get; set; } = null!;
        public DbSet<PlaybackProgress> PlaybackProgresses { get; set; } = null!;
        public DbSet<RecordingJob> RecordingJobs { get; set; } = null!;
        public DbSet<DownloadJob> DownloadJobs { get; set; } = null!;
        public DbSet<ContinueWatching> ContinueWatchings { get; set; } = null!; // Keyless View model

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite($"Data Source={DatabaseBootstrapper.RuntimeDatabasePath}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Query Model Configuration (Explicitly keyless)
            modelBuilder.Entity<ContinueWatching>().HasNoKey();

            // Strict Source configuration
            modelBuilder.Entity<SourceProfile>()
                .Property(e => e.Name).IsRequired().HasMaxLength(150);

            // Strict Credentials separation to prevent array bleed, with hard cascading delete for security.
            modelBuilder.Entity<SourceCredential>()
                .HasIndex(e => e.SourceProfileId)
                .IsUnique();

            modelBuilder.Entity<SourceCredential>()
                .HasOne<SourceProfile>()
                .WithOne()
                .HasForeignKey<SourceCredential>(e => e.SourceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SourceCredential>()
                .Property(e => e.ProxyUrl)
                .IsRequired()
                .HasMaxLength(600);

            modelBuilder.Entity<SourceSyncState>()
                .HasOne<SourceProfile>()
                .WithOne()
                .HasForeignKey<SourceSyncState>(e => e.SourceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SourceHealthReport>()
                .HasIndex(e => e.SourceProfileId)
                .IsUnique();

            modelBuilder.Entity<SourceHealthReport>()
                .HasOne<SourceProfile>()
                .WithOne()
                .HasForeignKey<SourceHealthReport>(e => e.SourceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SourceHealthReport>()
                .Property(e => e.StatusSummary)
                .IsRequired()
                .HasMaxLength(280);

            modelBuilder.Entity<SourceHealthReport>()
                .Property(e => e.ImportResultSummary)
                .IsRequired()
                .HasMaxLength(280);

            modelBuilder.Entity<SourceHealthReport>()
                .Property(e => e.ValidationSummary)
                .IsRequired()
                .HasMaxLength(280);

            modelBuilder.Entity<SourceHealthReport>()
                .Property(e => e.TopIssueSummary)
                .IsRequired()
                .HasMaxLength(360);

            modelBuilder.Entity<SourceHealthComponent>()
                .HasIndex(e => new { e.SourceHealthReportId, e.ComponentType })
                .IsUnique();

            modelBuilder.Entity<SourceHealthComponent>()
                .Property(e => e.Summary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceHealthComponent>()
                .HasOne(component => component.Report)
                .WithMany(report => report.Components)
                .HasForeignKey(component => component.SourceHealthReportId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SourceHealthProbe>()
                .HasIndex(e => new { e.SourceHealthReportId, e.ProbeType })
                .IsUnique();

            modelBuilder.Entity<SourceHealthProbe>()
                .Property(e => e.Summary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceHealthProbe>()
                .HasOne(probe => probe.Report)
                .WithMany(report => report.Probes)
                .HasForeignKey(probe => probe.SourceHealthReportId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SourceHealthIssue>()
                .Property(e => e.Code)
                .IsRequired()
                .HasMaxLength(64);

            modelBuilder.Entity<SourceHealthIssue>()
                .Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(120);

            modelBuilder.Entity<SourceHealthIssue>()
                .Property(e => e.Message)
                .IsRequired()
                .HasMaxLength(280);

            modelBuilder.Entity<SourceHealthIssue>()
                .Property(e => e.SampleItems)
                .IsRequired()
                .HasMaxLength(280);

            modelBuilder.Entity<SourceHealthIssue>()
                .HasOne(issue => issue.Report)
                .WithMany(report => report.Issues)
                .HasForeignKey(issue => issue.SourceHealthReportId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .HasIndex(e => new { e.SourceProfileId, e.IdentityKey })
                .IsUnique();

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.IdentityKey)
                .IsRequired()
                .HasMaxLength(180);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.NormalizedName)
                .IsRequired()
                .HasMaxLength(180);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.AliasKeys)
                .IsRequired()
                .HasMaxLength(2000);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.ProviderName)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.ProviderEpgChannelId)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.ProviderLogoUrl)
                .IsRequired()
                .HasMaxLength(600);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.ResolvedLogoUrl)
                .IsRequired()
                .HasMaxLength(600);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.MatchedXmltvChannelId)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.MatchedXmltvDisplayName)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.MatchedXmltvIconUrl)
                .IsRequired()
                .HasMaxLength(600);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.EpgMatchSummary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .Property(e => e.LogoSummary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<SourceChannelEnrichmentRecord>()
                .HasOne<SourceProfile>()
                .WithMany()
                .HasForeignKey(e => e.SourceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LogicalOperationalState>()
                .HasIndex(e => new { e.ContentType, e.LogicalContentKey })
                .IsUnique();

            modelBuilder.Entity<LogicalOperationalState>()
                .Property(e => e.LogicalContentKey)
                .IsRequired()
                .HasMaxLength(260);

            modelBuilder.Entity<LogicalOperationalState>()
                .Property(e => e.SelectionSummary)
                .IsRequired()
                .HasMaxLength(240);

            modelBuilder.Entity<LogicalOperationalState>()
                .Property(e => e.RecoverySummary)
                .IsRequired()
                .HasMaxLength(240);

            modelBuilder.Entity<LogicalOperationalCandidate>()
                .HasIndex(e => new { e.LogicalOperationalStateId, e.ContentId, e.SourceProfileId })
                .IsUnique();

            modelBuilder.Entity<LogicalOperationalCandidate>()
                .HasIndex(e => new { e.SourceProfileId, e.IsSelected });

            modelBuilder.Entity<LogicalOperationalCandidate>()
                .Property(e => e.SourceName)
                .IsRequired()
                .HasMaxLength(150);

            modelBuilder.Entity<LogicalOperationalCandidate>()
                .Property(e => e.StreamUrl)
                .IsRequired()
                .HasMaxLength(1200);

            modelBuilder.Entity<LogicalOperationalCandidate>()
                .Property(e => e.Summary)
                .IsRequired()
                .HasMaxLength(240);

            modelBuilder.Entity<LogicalOperationalCandidate>()
                .HasOne(candidate => candidate.State)
                .WithMany(state => state.Candidates)
                .HasForeignKey(candidate => candidate.LogicalOperationalStateId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EpgSyncLog>()
                .HasIndex(e => e.SourceProfileId)
                .IsUnique();

            modelBuilder.Entity<EpgSyncLog>()
                .HasOne<SourceProfile>()
                .WithOne()
                .HasForeignKey<EpgSyncLog>(e => e.SourceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EpgProgram>()
                .HasIndex(e => new { e.ChannelId, e.StartTimeUtc })
                .HasDatabaseName("IX_EpgPrograms_ChannelId_StartTimeUtc");

            modelBuilder.Entity<Channel>()
                .HasIndex(e => e.EpgChannelId)
                .HasDatabaseName("IX_Channels_EpgChannelId");

            modelBuilder.Entity<Channel>()
                .HasIndex(e => e.NormalizedIdentityKey)
                .HasDatabaseName("IX_Channels_NormalizedIdentityKey");

            modelBuilder.Entity<Channel>()
                .Property(e => e.ProviderLogoUrl)
                .IsRequired()
                .HasMaxLength(600);

            modelBuilder.Entity<Channel>()
                .Property(e => e.ProviderEpgChannelId)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<Channel>()
                .Property(e => e.NormalizedIdentityKey)
                .IsRequired()
                .HasMaxLength(180);

            modelBuilder.Entity<Channel>()
                .Property(e => e.NormalizedName)
                .IsRequired()
                .HasMaxLength(180);

            modelBuilder.Entity<Channel>()
                .Property(e => e.AliasKeys)
                .IsRequired()
                .HasMaxLength(2000);

            modelBuilder.Entity<Channel>()
                .Property(e => e.EpgMatchSummary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<Channel>()
                .Property(e => e.LogoSummary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<Channel>()
                .Property(e => e.ProviderCatchupMode)
                .IsRequired()
                .HasMaxLength(64);

            modelBuilder.Entity<Channel>()
                .Property(e => e.ProviderCatchupSource)
                .IsRequired()
                .HasMaxLength(600);

            modelBuilder.Entity<Channel>()
                .Property(e => e.CatchupSummary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<ParentalControlSetting>()
                .HasIndex(setting => setting.ProfileId)
                .IsUnique();

            modelBuilder.Entity<AppProfile>()
                .HasIndex(profile => profile.Name);

            // Optimize query access
            modelBuilder.Entity<Favorite>()
                .HasIndex(f => new { f.ProfileId, f.ContentType, f.ContentId });

            modelBuilder.Entity<Favorite>()
                .HasIndex(f => new { f.ContentType, f.ContentId });

            modelBuilder.Entity<Favorite>()
                .HasIndex(f => new { f.ProfileId, f.ContentType, f.LogicalContentKey });

            modelBuilder.Entity<Favorite>()
                .Property(f => f.LogicalContentKey)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<PlaybackProgress>()
                .HasIndex(p => new { p.ProfileId, p.ContentType, p.ContentId });

            modelBuilder.Entity<PlaybackProgress>()
                .HasIndex(p => new { p.ContentType, p.ContentId });

            modelBuilder.Entity<PlaybackProgress>()
                .HasIndex(p => new { p.ProfileId, p.ContentType, p.LogicalContentKey });

            modelBuilder.Entity<PlaybackProgress>()
                .Property(p => p.LogicalContentKey)
                .IsRequired()
                .HasMaxLength(260);

            modelBuilder.Entity<SourceSyncState>()
                .Property(state => state.AutoRefreshSummary)
                .IsRequired()
                .HasMaxLength(220);

            modelBuilder.Entity<RecordingJob>()
                .HasIndex(job => new { job.ProfileId, job.Status, job.StartTimeUtc });

            modelBuilder.Entity<DownloadJob>()
                .HasIndex(job => new { job.ProfileId, job.Status, job.RequestedAtUtc });

            modelBuilder.Entity<Movie>()
                .HasIndex(m => m.TmdbId);

            modelBuilder.Entity<Movie>()
                .HasIndex(m => m.MetadataUpdatedAt);

            modelBuilder.Entity<Movie>()
                .HasIndex(m => m.CanonicalTitleKey);

            modelBuilder.Entity<Movie>()
                .HasIndex(m => m.DedupFingerprint);

            modelBuilder.Entity<Series>()
                .HasIndex(s => s.TmdbId);

            modelBuilder.Entity<Series>()
                .HasIndex(s => s.MetadataUpdatedAt);

            modelBuilder.Entity<Series>()
                .HasIndex(s => s.CanonicalTitleKey);

            modelBuilder.Entity<Series>()
                .HasIndex(s => s.DedupFingerprint);

            modelBuilder.Entity<SchemaVersion>()
                .HasData(new SchemaVersion
                {
                    Id = 1,
                    VersionNumber = 1,
                    AppliedAt = new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
                    IsValidated = true
                });
        }
    }
}
