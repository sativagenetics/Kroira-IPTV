using System;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kroira.App.Migrations
{
    internal static class MigrationDesignerBackfillSupport
    {
        public static void BuildAfterFavoritesIndexAndSeedSchemaVersion(ModelBuilder modelBuilder)
        {
            var migration = new AddFavoritesIndexAndSeedSchemaVersion();
            var buildTargetModel = typeof(AddFavoritesIndexAndSeedSchemaVersion).GetMethod(
                "BuildTargetModel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildTargetModel == null)
            {
                throw new InvalidOperationException("Could not locate BuildTargetModel on AddFavoritesIndexAndSeedSchemaVersion.");
            }

            buildTargetModel.Invoke(migration, new object[] { modelBuilder });
        }

        public static void BuildCurrent(ModelBuilder modelBuilder)
        {
            new CurrentSnapshotProxy().Apply(modelBuilder);
        }

        public static void ApplyTmdbMetadataFields(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity("Kroira.App.Models.Movie", b =>
            {
                b.Property<string>("BackdropUrl")
                    .HasColumnType("TEXT");

                b.Property<string>("Genres")
                    .HasColumnType("TEXT");

                b.Property<string>("ImdbId")
                    .HasColumnType("TEXT");

                b.Property<DateTime?>("MetadataUpdatedAt")
                    .HasColumnType("TEXT");

                b.Property<string>("OriginalLanguage")
                    .HasColumnType("TEXT");

                b.Property<string>("Overview")
                    .HasColumnType("TEXT");

                b.Property<double>("Popularity")
                    .HasColumnType("REAL");

                b.Property<DateTime?>("ReleaseDate")
                    .HasColumnType("TEXT");

                b.Property<string>("TmdbBackdropPath")
                    .HasColumnType("TEXT");

                b.Property<string>("TmdbPosterPath")
                    .HasColumnType("TEXT");

                b.Property<double>("VoteAverage")
                    .HasColumnType("REAL");

                b.HasIndex("MetadataUpdatedAt")
                    .HasDatabaseName("IX_Movies_MetadataUpdatedAt");

                b.HasIndex("TmdbId")
                    .HasDatabaseName("IX_Movies_TmdbId");
            });

            modelBuilder.Entity("Kroira.App.Models.Series", b =>
            {
                b.Property<string>("BackdropUrl")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<DateTime?>("FirstAirDate")
                    .HasColumnType("TEXT");

                b.Property<string>("Genres")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<string>("ImdbId")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<DateTime?>("MetadataUpdatedAt")
                    .HasColumnType("TEXT");

                b.Property<string>("OriginalLanguage")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<string>("Overview")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<double>("Popularity")
                    .HasColumnType("REAL");

                b.Property<string>("TmdbBackdropPath")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<string>("TmdbPosterPath")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<double>("VoteAverage")
                    .HasColumnType("REAL");

                b.HasIndex("MetadataUpdatedAt")
                    .HasDatabaseName("IX_Series_MetadataUpdatedAt");

                b.HasIndex("TmdbId")
                    .HasDatabaseName("IX_Series_TmdbId");
            });
        }

        public static void ApplyEpgEnhancements(ModelBuilder modelBuilder)
        {
            RemoveIndex(modelBuilder, "Kroira.App.Models.EpgProgram", "ChannelId");

            modelBuilder.Entity("Kroira.App.Models.EpgProgram", b =>
            {
                b.Property<string>("Category")
                    .HasColumnType("TEXT");

                b.Property<string>("Subtitle")
                    .HasColumnType("TEXT");

                b.HasIndex("ChannelId", "StartTimeUtc")
                    .HasDatabaseName("IX_EpgPrograms_ChannelId_StartTimeUtc");
            });

            modelBuilder.Entity("Kroira.App.Models.EpgSyncLog", b =>
            {
                b.Property<int>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("INTEGER");

                b.Property<string>("FailureReason")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<bool>("IsSuccess")
                    .HasColumnType("INTEGER");

                b.Property<int>("MatchedChannelCount")
                    .HasColumnType("INTEGER");

                b.Property<int>("ProgrammeCount")
                    .HasColumnType("INTEGER");

                b.Property<int>("SourceProfileId")
                    .HasColumnType("INTEGER");

                b.Property<DateTime>("SyncedAtUtc")
                    .HasColumnType("TEXT");

                b.HasKey("Id");

                b.HasIndex("SourceProfileId")
                    .IsUnique();

                b.ToTable("EpgSyncLogs");
            });

            modelBuilder.Entity("Kroira.App.Models.EpgSyncLog", b =>
            {
                b.HasOne("Kroira.App.Models.SourceProfile", null)
                    .WithOne()
                    .HasForeignKey("Kroira.App.Models.EpgSyncLog", "SourceProfileId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
            });
        }

        public static void ApplyM3uImportMode(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity("Kroira.App.Models.SourceCredential", b =>
            {
                b.Property<int>("M3uImportMode")
                    .HasColumnType("INTEGER");
            });
        }

        public static void RemoveSourceHealthComponents(ModelBuilder modelBuilder)
        {
            RemoveEntity(modelBuilder, "Kroira.App.Models.SourceHealthComponent");
        }

        public static void RemoveSourceHealthProbes(ModelBuilder modelBuilder)
        {
            RemoveEntity(modelBuilder, "Kroira.App.Models.SourceHealthProbe");
        }

        public static void RemoveSourceEnrichment(ModelBuilder modelBuilder)
        {
            RemoveEntity(modelBuilder, "Kroira.App.Models.SourceChannelEnrichmentRecord");
            RemoveIndex(modelBuilder, "Kroira.App.Models.Channel", "NormalizedIdentityKey");
            RemoveProperties(
                modelBuilder,
                "Kroira.App.Models.Channel",
                "ProviderLogoUrl",
                "ProviderEpgChannelId",
                "NormalizedIdentityKey",
                "NormalizedName",
                "AliasKeys",
                "EpgMatchSource",
                "EpgMatchConfidence",
                "EpgMatchSummary",
                "LogoSource",
                "LogoConfidence",
                "LogoSummary",
                "EnrichedAtUtc");
        }

        public static void RemoveGlobalFavoritesAutoRefreshCatchup(ModelBuilder modelBuilder)
        {
            RemoveIndex(modelBuilder, "Kroira.App.Models.Favorite", "ProfileId", "ContentType", "LogicalContentKey");
            RemoveProperties(
                modelBuilder,
                "Kroira.App.Models.Favorite",
                "LogicalContentKey",
                "PreferredSourceProfileId",
                "ResolvedAtUtc");

            RemoveIndex(modelBuilder, "Kroira.App.Models.PlaybackProgress", "ProfileId", "ContentType", "LogicalContentKey");
            RemoveProperties(
                modelBuilder,
                "Kroira.App.Models.PlaybackProgress",
                "LogicalContentKey",
                "PreferredSourceProfileId",
                "ResolvedAtUtc");

            RemoveIndex(modelBuilder, "Kroira.App.Models.SourceSyncState", "NextAutoRefreshDueAtUtc");
            RemoveProperties(
                modelBuilder,
                "Kroira.App.Models.SourceSyncState",
                "LastAutoRefreshAttemptAtUtc",
                "LastAutoRefreshSuccessAtUtc",
                "NextAutoRefreshDueAtUtc",
                "AutoRefreshState",
                "AutoRefreshSummary",
                "AutoRefreshFailureCount");

            RemoveProperties(
                modelBuilder,
                "Kroira.App.Models.Channel",
                "ProviderCatchupMode",
                "ProviderCatchupSource",
                "SupportsCatchup",
                "CatchupWindowHours",
                "CatchupSource",
                "CatchupConfidence",
                "CatchupSummary",
                "CatchupDetectedAtUtc");
        }

        public static void RemoveMirrorProxyRollbackOps(ModelBuilder modelBuilder)
        {
            RemoveEntity(modelBuilder, "Kroira.App.Models.LogicalOperationalCandidate");
            RemoveEntity(modelBuilder, "Kroira.App.Models.LogicalOperationalState");
            RemoveProperties(modelBuilder, "Kroira.App.Models.SourceCredential", "ProxyScope", "ProxyUrl");
        }

        public static void RemoveCatchupPlaybackAttempts(ModelBuilder modelBuilder)
        {
            RemoveEntity(modelBuilder, "Kroira.App.Models.CatchupPlaybackAttempt");
        }

        public static void RemoveStalkerPortalSupport(ModelBuilder modelBuilder)
        {
            RemoveEntity(modelBuilder, "Kroira.App.Models.StalkerPortalSnapshot");
            RemoveProperties(
                modelBuilder,
                "Kroira.App.Models.SourceCredential",
                "StalkerApiUrl",
                "StalkerDeviceId",
                "StalkerLocale",
                "StalkerMacAddress",
                "StalkerSerialNumber",
                "StalkerTimezone");
        }

        public static void RemoveSourceCompanionRelay(ModelBuilder modelBuilder)
        {
            RemoveProperties(modelBuilder, "Kroira.App.Models.SourceCredential", "CompanionMode", "CompanionScope", "CompanionUrl");
        }

        private static void RemoveEntity(ModelBuilder modelBuilder, string entityName)
        {
            var entityType = modelBuilder.Model.FindEntityType(entityName);
            if (entityType != null)
            {
                modelBuilder.Model.RemoveEntityType(entityType);
            }
        }

        private static void RemoveProperties(ModelBuilder modelBuilder, string entityName, params string[] propertyNames)
        {
            var entityType = modelBuilder.Model.FindEntityType(entityName);
            if (entityType == null)
            {
                return;
            }

            foreach (var propertyName in propertyNames)
            {
                var property = entityType.FindProperty(propertyName);
                if (property != null)
                {
                    entityType.RemoveProperty(property);
                }
            }
        }

        private static void RemoveIndex(ModelBuilder modelBuilder, string entityName, params string[] propertyNames)
        {
            var entityType = modelBuilder.Model.FindEntityType(entityName);
            if (entityType == null)
            {
                return;
            }

            var index = entityType.GetIndexes()
                .FirstOrDefault(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
            if (index != null)
            {
                entityType.RemoveIndex(index);
            }
        }

        private sealed class CurrentSnapshotProxy : AppDbContextModelSnapshot
        {
            public void Apply(ModelBuilder modelBuilder)
            {
                BuildModel(modelBuilder);
            }
        }
    }
}
