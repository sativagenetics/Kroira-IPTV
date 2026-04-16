using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using System;

namespace Kroira.App.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<AppSetting> AppSettings { get; set; } = null!;
        public DbSet<FeatureEntitlement> FeatureEntitlements { get; set; } = null!;
        public DbSet<ParentalControlSetting> ParentalControlSettings { get; set; } = null!;
        public DbSet<SchemaVersion> SchemaVersions { get; set; } = null!;
        public DbSet<SourceProfile> SourceProfiles { get; set; } = null!;
        public DbSet<SourceCredential> SourceCredentials { get; set; } = null!;
        public DbSet<SourceSyncState> SourceSyncStates { get; set; } = null!;
        public DbSet<ChannelCategory> ChannelCategories { get; set; } = null!;
        public DbSet<Channel> Channels { get; set; } = null!;
        public DbSet<EpgProgram> EpgPrograms { get; set; } = null!;
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
                string dbPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Kroira", "kroira.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
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

            modelBuilder.Entity<SourceSyncState>()
                .HasOne<SourceProfile>()
                .WithOne()
                .HasForeignKey<SourceSyncState>(e => e.SourceProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Optimize query access
            modelBuilder.Entity<Favorite>()
                .HasIndex(f => new { f.ContentType, f.ContentId });

            modelBuilder.Entity<PlaybackProgress>()
                .HasIndex(p => new { p.ContentType, p.ContentId });

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
