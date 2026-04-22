using System.Text.Json;
using System.Text.RegularExpressions;
using Kroira.App.Composition;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.Regressions;

internal sealed class RegressionRunner
{
    private static readonly Regex AbsoluteSyncTimestampPattern = new(
        @"(?<=\b(?:Last successful sync was|Using data from|The last successful source sync was|Last sync attempt was) )\d{1,2}[./-]\d{1,2}[./-]\d{4} \d{1,2}:\d{2}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<RegressionRunResult> RunAsync(RegressionRunnerOptions options)
    {
        var repoRoot = ResolveRepoRoot();
        var projectRoot = Path.Combine(repoRoot, "tests", "Kroira.Regressions");
        var corpusRoot = Path.Combine(projectRoot, "Corpus");
        var artifactRoot = Path.Combine(projectRoot, "artifacts");
        Directory.CreateDirectory(artifactRoot);

        var caseDirectories = Directory.Exists(corpusRoot)
            ? Directory.GetFiles(corpusRoot, "case.json", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        var discovered = caseDirectories
            .Select(Path.GetFileName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

        if (options.ListOnly)
        {
            return new RegressionRunResult
            {
                DiscoveredCaseIds = discovered
            };
        }

        var filteredDirectories = string.IsNullOrWhiteSpace(options.CaseId)
            ? caseDirectories
            : caseDirectories.Where(path => string.Equals(Path.GetFileName(path), options.CaseId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(options.CaseId) && filteredDirectories.Count == 0)
        {
            throw new InvalidOperationException($"Regression case '{options.CaseId}' was not found.");
        }

        var result = new RegressionRunResult
        {
            DiscoveredCaseIds = discovered
        };

        foreach (var caseDirectory in filteredDirectories)
        {
            var caseDefinition = await LoadCaseDefinitionAsync(caseDirectory);
            Console.WriteLine($"[{caseDefinition.Id}] running");

            var actualPath = Path.Combine(artifactRoot, $"{caseDefinition.Id}.actual.json");
            var expectedPath = Path.Combine(caseDirectory, "expected.json");

            await using var server = await FixtureHttpServer.StartAsync(caseDirectory);
            var dbPath = Path.Combine(artifactRoot, $"{caseDefinition.Id}.db");
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");

            var provider = BuildServices(dbPath);
            try
            {
                var snapshot = await ExecuteCaseAsync(provider, caseDefinition, server.BaseUrl);
                var actualJson = JsonSerializer.Serialize(snapshot, RegressionJson.Options);
                await File.WriteAllTextAsync(actualPath, actualJson);

                if (options.UpdateBaselines || !File.Exists(expectedPath))
                {
                    await File.WriteAllTextAsync(expectedPath, actualJson);
                    result.UpdatedBaselineCount++;
                    Console.WriteLine($"[{caseDefinition.Id}] baseline updated");
                    continue;
                }

                var expectedJson = await File.ReadAllTextAsync(expectedPath);
                if (string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
                {
                    result.PassedCount++;
                    Console.WriteLine($"[{caseDefinition.Id}] passed");
                    continue;
                }

                result.FailedCount++;
                var diff = JsonDiffWriter.CreateDiff(expectedJson, actualJson);
                Console.WriteLine($"[{caseDefinition.Id}] failed");
                Console.WriteLine(diff);
                Console.WriteLine($"Expected: {expectedPath}");
                Console.WriteLine($"Actual:   {actualPath}");
            }
            finally
            {
                if (provider is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        return result;
    }

    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddKroiraPipelineServices();
        return services.BuildServiceProvider();
    }

    private static async Task<RegressionCaseDefinition> LoadCaseDefinitionAsync(string caseDirectory)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(caseDirectory, "case.json"));
        var definition = JsonSerializer.Deserialize<RegressionCaseDefinition>(text, RegressionJson.Options)
                         ?? throw new InvalidOperationException($"Failed to parse case.json in {caseDirectory}.");
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = Path.GetFileName(caseDirectory);
        }

        return definition;
    }

    private static async Task<RegressionSnapshot> ExecuteCaseAsync(ServiceProvider provider, RegressionCaseDefinition definition, string serverBaseUrl)
    {
        using var bootstrapScope = provider.CreateScope();
        var db = bootstrapScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        DatabaseBootstrapper.EnsureRuntimeSchema(db);
        await SeedDeterministicSettingsAsync(db);

        var runtimeSources = new List<RuntimeSource>();
        foreach (var source in definition.Sources)
        {
            var profile = new SourceProfile
            {
                Name = source.Name,
                Type = source.Type
            };
            db.SourceProfiles.Add(profile);
            await db.SaveChangesAsync();

            db.SourceCredentials.Add(new SourceCredential
            {
                SourceProfileId = profile.Id,
                Url = ResolveTokens(source.Url, serverBaseUrl),
                Username = ResolveTokens(source.Username, serverBaseUrl),
                Password = ResolveTokens(source.Password, serverBaseUrl),
                ManualEpgUrl = ResolveTokens(source.ManualEpgUrl, serverBaseUrl),
                EpgMode = source.EpgMode,
                M3uImportMode = source.M3uImportMode,
                ProxyScope = source.ProxyScope,
                ProxyUrl = ResolveTokens(source.ProxyUrl, serverBaseUrl)
            });
            await db.SaveChangesAsync();

            runtimeSources.Add(new RuntimeSource
            {
                Definition = source,
                SourceProfileId = profile.Id
            });
        }

        var refreshService = bootstrapScope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
        foreach (var runtime in runtimeSources)
        {
            runtime.Result = await refreshService.RefreshSourceAsync(
                runtime.SourceProfileId,
                runtime.Definition.RefreshTrigger,
                runtime.Definition.RefreshScope);
        }

        await bootstrapScope.ServiceProvider.GetRequiredService<IContentOperationalService>().RefreshOperationalStateAsync(db);
        return await CaptureSnapshotAsync(db, runtimeSources, serverBaseUrl);
    }

    private static async Task SeedDeterministicSettingsAsync(AppDbContext db)
    {
        await UpsertSettingAsync(db, "Sources.AutoRefresh.Enabled", bool.FalseString);
        await UpsertSettingAsync(db, "Sources.AutoRefresh.IntervalHours", "6");
        await UpsertSettingAsync(db, "Sources.AutoRefresh.RunAfterLaunch", bool.FalseString);
    }

    private static async Task UpsertSettingAsync(AppDbContext db, string key, string value)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(item => item.Key == key);
        if (setting == null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value
            });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<RegressionSnapshot> CaptureSnapshotAsync(AppDbContext db, IReadOnlyList<RuntimeSource> runtimeSources, string serverBaseUrl)
    {
        var sourceIds = runtimeSources.Select(item => item.SourceProfileId).ToHashSet();

        var sourceProfiles = await db.SourceProfiles
            .AsNoTracking()
            .Where(item => sourceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id);

        var credentials = await db.SourceCredentials
            .AsNoTracking()
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .ToDictionaryAsync(item => item.SourceProfileId);

        var syncStates = await db.SourceSyncStates
            .AsNoTracking()
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .ToDictionaryAsync(item => item.SourceProfileId);

        var healthReports = await db.SourceHealthReports
            .AsNoTracking()
            .Include(item => item.Components)
            .Include(item => item.Probes)
            .Include(item => item.Issues)
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .ToDictionaryAsync(item => item.SourceProfileId);

        var epgLogs = await db.EpgSyncLogs
            .AsNoTracking()
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .ToDictionaryAsync(item => item.SourceProfileId);

        var channelsBySource = (await db.Channels
            .AsNoTracking()
            .Join(
                db.ChannelCategories.AsNoTracking(),
                channel => channel.ChannelCategoryId,
                category => category.Id,
                (channel, category) => new { category.SourceProfileId, CategoryName = category.Name, Channel = channel })
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .ToListAsync())
            .Select(item => new ChannelFixtureRow(item.SourceProfileId, item.CategoryName, item.Channel))
            .ToList();

        var moviesBySource = await db.Movies
            .AsNoTracking()
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .ToListAsync();

        var seriesBySource = await db.Series
            .AsNoTracking()
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .Include(item => item.Seasons!)
            .ThenInclude(item => item.Episodes!)
            .ToListAsync();

        var enrichmentBySource = await db.SourceChannelEnrichmentRecords
            .AsNoTracking()
            .Where(item => sourceIds.Contains(item.SourceProfileId))
            .ToListAsync();

        var channelIds = channelsBySource.Select(item => item.Channel.Id).ToHashSet();
        var epgPrograms = await db.EpgPrograms
            .AsNoTracking()
            .Where(item => channelIds.Contains(item.ChannelId))
            .ToListAsync();

        var contentNames = BuildContentNameLookup(channelsBySource.Select(item => item.Channel).ToList(), moviesBySource);

        var operationalStates = await db.LogicalOperationalStates
            .AsNoTracking()
            .Include(item => item.Candidates)
            .ToListAsync();

        var snapshot = new RegressionSnapshot();
        foreach (var runtime in runtimeSources.OrderBy(item => item.Definition.Key, StringComparer.OrdinalIgnoreCase))
        {
            var sourceId = runtime.SourceProfileId;
            sourceProfiles.TryGetValue(sourceId, out var profile);
            credentials.TryGetValue(sourceId, out var credential);
            syncStates.TryGetValue(sourceId, out var syncState);
            healthReports.TryGetValue(sourceId, out var health);
            epgLogs.TryGetValue(sourceId, out var epg);

            var sourceChannels = channelsBySource
                .Where(item => item.SourceProfileId == sourceId)
                .OrderBy(item => item.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Channel.NormalizedIdentityKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Channel.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Channel.StreamUrl, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var sourceMovies = moviesBySource
                .Where(item => item.SourceProfileId == sourceId)
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ExternalId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var sourceSeries = seriesBySource
                .Where(item => item.SourceProfileId == sourceId)
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ExternalId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var sourcePrograms = epgPrograms.Where(item => sourceChannels.Select(channel => channel.Channel.Id).Contains(item.ChannelId)).ToList();

            snapshot.Sources.Add(new SourceSnapshot
            {
                Key = runtime.Definition.Key,
                Name = profile?.Name ?? runtime.Definition.Name,
                Type = (profile?.Type ?? runtime.Definition.Type).ToString(),
                Refresh = BuildRefreshSnapshot(runtime, serverBaseUrl),
                Credential = BuildCredentialSnapshot(credential, serverBaseUrl),
                SyncState = BuildSyncStateSnapshot(syncState, serverBaseUrl),
                Counts = new SourceCountSnapshot
                {
                    Channels = sourceChannels.Count,
                    Movies = sourceMovies.Count,
                    Series = sourceSeries.Count,
                    Seasons = sourceSeries.SelectMany(item => item.Seasons ?? []).Count(),
                    Episodes = sourceSeries.SelectMany(item => item.Seasons ?? []).SelectMany(item => item.Episodes ?? []).Count(),
                    EpgPrograms = sourcePrograms.Count
                },
                Health = BuildHealthSnapshot(health, serverBaseUrl),
                Epg = BuildEpgSnapshot(epg, serverBaseUrl),
                Channels = sourceChannels.Select(item => BuildChannelSnapshot(item, serverBaseUrl)).ToList(),
                Movies = sourceMovies.Select(item => BuildMovieSnapshot(item, serverBaseUrl)).ToList(),
                Series = sourceSeries.Select(item => BuildSeriesSnapshot(item, serverBaseUrl)).ToList(),
                Enrichment = enrichmentBySource
                    .Where(item => item.SourceProfileId == sourceId)
                    .OrderBy(item => item.IdentityKey, StringComparer.OrdinalIgnoreCase)
                    .Select(item => BuildEnrichmentSnapshot(item, serverBaseUrl))
                    .ToList()
            });
        }

        foreach (var state in operationalStates
                     .Where(item => item.Candidates.Any(candidate => sourceIds.Contains(candidate.SourceProfileId)))
                     .OrderBy(item => item.ContentType.ToString(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.LogicalContentKey, StringComparer.OrdinalIgnoreCase))
        {
            var selected = state.Candidates.FirstOrDefault(item => item.IsSelected)
                           ?? state.Candidates.OrderBy(item => item.Rank).FirstOrDefault();
            snapshot.OperationalStates.Add(new OperationalStateSnapshot
            {
                ContentType = state.ContentType.ToString(),
                LogicalContentKey = NormalizeText(state.LogicalContentKey, serverBaseUrl),
                CandidateCount = state.CandidateCount,
                PreferredSourceName = selected?.SourceName ?? string.Empty,
                PreferredTitle = selected == null ? string.Empty : ResolveContentTitle(contentNames, state.ContentType, selected.ContentId, serverBaseUrl),
                PreferredScore = state.PreferredScore,
                SelectionSummary = NormalizeText(state.SelectionSummary, serverBaseUrl),
                RecoveryAction = state.RecoveryAction.ToString(),
                RecoverySummary = NormalizeText(state.RecoverySummary, serverBaseUrl),
                Candidates = state.Candidates
                    .Where(item => sourceIds.Contains(item.SourceProfileId))
                    .OrderBy(item => item.Rank)
                    .ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new OperationalCandidateSnapshot
                    {
                        Rank = item.Rank,
                        SourceName = NormalizeText(item.SourceName, serverBaseUrl),
                        Title = ResolveContentTitle(contentNames, state.ContentType, item.ContentId, serverBaseUrl),
                        Score = item.Score,
                        IsSelected = item.IsSelected,
                        IsLastKnownGood = item.IsLastKnownGood,
                        SupportsProxy = item.SupportsProxy,
                        Summary = NormalizeText(item.Summary, serverBaseUrl)
                    }).ToList()
            });
        }

        return snapshot;
    }

    private static SourceRefreshSnapshot BuildRefreshSnapshot(RuntimeSource runtime, string serverBaseUrl)
    {
        return new SourceRefreshSnapshot
        {
            Success = runtime.Result?.Success ?? false,
            Trigger = (runtime.Result?.Trigger ?? runtime.Definition.RefreshTrigger).ToString(),
            Scope = (runtime.Result?.Scope ?? runtime.Definition.RefreshScope).ToString(),
            Message = NormalizeText(runtime.Result?.Message, serverBaseUrl),
            CatalogSummary = NormalizeText(runtime.Result?.CatalogSummary, serverBaseUrl),
            GuideSummary = NormalizeText(runtime.Result?.GuideSummary, serverBaseUrl),
            GuideAttempted = runtime.Result?.GuideAttempted ?? false,
            GuideSucceeded = runtime.Result?.GuideSucceeded ?? false
        };
    }

    private static SourceCredentialSnapshot BuildCredentialSnapshot(SourceCredential? credential, string serverBaseUrl)
    {
        return new SourceCredentialSnapshot
        {
            Url = NormalizeText(credential?.Url, serverBaseUrl),
            DetectedEpgUrl = NormalizeText(credential?.DetectedEpgUrl, serverBaseUrl),
            ManualEpgUrl = NormalizeText(credential?.ManualEpgUrl, serverBaseUrl),
            EpgMode = credential?.EpgMode.ToString() ?? string.Empty,
            M3uImportMode = credential?.M3uImportMode.ToString() ?? string.Empty,
            ProxyScope = credential?.ProxyScope.ToString() ?? string.Empty,
            ProxyUrl = NormalizeText(credential?.ProxyUrl, serverBaseUrl)
        };
    }

    private static SourceSyncStateSnapshot BuildSyncStateSnapshot(SourceSyncState? syncState, string serverBaseUrl)
    {
        return new SourceSyncStateSnapshot
        {
            HttpStatusCode = syncState?.HttpStatusCode ?? 0,
            ErrorLog = NormalizeText(syncState?.ErrorLog, serverBaseUrl),
            AutoRefreshState = syncState?.AutoRefreshState.ToString() ?? string.Empty,
            AutoRefreshSummary = NormalizeText(syncState?.AutoRefreshSummary, serverBaseUrl),
            AutoRefreshFailureCount = syncState?.AutoRefreshFailureCount ?? 0
        };
    }

    private static ChannelSnapshot BuildChannelSnapshot(ChannelFixtureRow item, string serverBaseUrl)
    {
        var channel = item.Channel;
        var categoryName = item.CategoryName;
        return new ChannelSnapshot
        {
            Category = categoryName,
            Name = NormalizeText(channel.Name, serverBaseUrl),
            StreamUrl = NormalizeText(channel.StreamUrl, serverBaseUrl),
            ProviderEpgChannelId = NormalizeText(channel.ProviderEpgChannelId, serverBaseUrl),
            EpgChannelId = NormalizeText(channel.EpgChannelId, serverBaseUrl),
            ProviderLogoUrl = NormalizeText(channel.ProviderLogoUrl, serverBaseUrl),
            LogoUrl = NormalizeText(channel.LogoUrl, serverBaseUrl),
            NormalizedIdentityKey = NormalizeText(channel.NormalizedIdentityKey, serverBaseUrl),
            AliasKeys = NormalizeText(channel.AliasKeys, serverBaseUrl),
            EpgMatchSource = channel.EpgMatchSource.ToString(),
            EpgMatchConfidence = channel.EpgMatchConfidence,
            EpgMatchSummary = NormalizeText(channel.EpgMatchSummary, serverBaseUrl),
            LogoSource = channel.LogoSource.ToString(),
            LogoConfidence = channel.LogoConfidence,
            LogoSummary = NormalizeText(channel.LogoSummary, serverBaseUrl),
            SupportsCatchup = channel.SupportsCatchup,
            CatchupWindowHours = channel.CatchupWindowHours,
            CatchupSource = channel.CatchupSource.ToString(),
            CatchupConfidence = channel.CatchupConfidence,
            CatchupSummary = NormalizeText(channel.CatchupSummary, serverBaseUrl)
        };
    }

    private static MovieSnapshot BuildMovieSnapshot(Movie item, string serverBaseUrl)
    {
        return new MovieSnapshot
        {
            Title = NormalizeText(item.Title, serverBaseUrl),
            RawTitle = NormalizeText(item.RawSourceTitle, serverBaseUrl),
            CategoryName = NormalizeText(item.CategoryName, serverBaseUrl),
            ContentKind = NormalizeText(item.ContentKind, serverBaseUrl),
            CanonicalTitleKey = NormalizeText(item.CanonicalTitleKey, serverBaseUrl),
            DedupFingerprint = NormalizeText(item.DedupFingerprint, serverBaseUrl),
            StreamUrl = NormalizeText(item.StreamUrl, serverBaseUrl)
        };
    }

    private static SeriesSnapshot BuildSeriesSnapshot(Series item, string serverBaseUrl)
    {
        return new SeriesSnapshot
        {
            Title = NormalizeText(item.Title, serverBaseUrl),
            RawTitle = NormalizeText(item.RawSourceTitle, serverBaseUrl),
            CategoryName = NormalizeText(item.CategoryName, serverBaseUrl),
            ContentKind = NormalizeText(item.ContentKind, serverBaseUrl),
            CanonicalTitleKey = NormalizeText(item.CanonicalTitleKey, serverBaseUrl),
            DedupFingerprint = NormalizeText(item.DedupFingerprint, serverBaseUrl),
            Seasons = (item.Seasons ?? [])
                .OrderBy(season => season.SeasonNumber)
                .Select(season => new SeasonSnapshot
                {
                    SeasonNumber = season.SeasonNumber,
                    Episodes = (season.Episodes ?? [])
                        .OrderBy(episode => episode.EpisodeNumber)
                        .ThenBy(episode => episode.Title, StringComparer.OrdinalIgnoreCase)
                        .Select(episode => new EpisodeSnapshot
                        {
                            ExternalId = NormalizeText(episode.ExternalId, serverBaseUrl),
                            Title = NormalizeText(episode.Title, serverBaseUrl),
                            EpisodeNumber = episode.EpisodeNumber,
                            StreamUrl = NormalizeText(episode.StreamUrl, serverBaseUrl)
                        }).ToList()
                }).ToList()
        };
    }

    private static EnrichmentRecordSnapshot BuildEnrichmentSnapshot(SourceChannelEnrichmentRecord item, string serverBaseUrl)
    {
        return new EnrichmentRecordSnapshot
        {
            IdentityKey = NormalizeText(item.IdentityKey, serverBaseUrl),
            NormalizedName = NormalizeText(item.NormalizedName, serverBaseUrl),
            AliasKeys = NormalizeText(item.AliasKeys, serverBaseUrl),
            MatchedXmltvChannelId = NormalizeText(item.MatchedXmltvChannelId, serverBaseUrl),
            MatchedXmltvDisplayName = NormalizeText(item.MatchedXmltvDisplayName, serverBaseUrl),
            ResolvedLogoUrl = NormalizeText(item.ResolvedLogoUrl, serverBaseUrl),
            EpgMatchSource = item.EpgMatchSource.ToString(),
            EpgMatchConfidence = item.EpgMatchConfidence,
            LogoSource = item.LogoSource.ToString(),
            LogoConfidence = item.LogoConfidence
        };
    }

    private static Dictionary<(OperationalContentType, int), string> BuildContentNameLookup(IReadOnlyCollection<Channel> channels, IReadOnlyCollection<Movie> movies)
    {
        var lookup = new Dictionary<(OperationalContentType, int), string>();
        foreach (var channel in channels)
        {
            lookup[(OperationalContentType.Channel, channel.Id)] = channel.Name;
        }

        foreach (var movie in movies)
        {
            lookup[(OperationalContentType.Movie, movie.Id)] = movie.Title;
        }

        return lookup;
    }

    private static string ResolveContentTitle(
        IReadOnlyDictionary<(OperationalContentType, int), string> contentNames,
        OperationalContentType contentType,
        int contentId,
        string serverBaseUrl)
    {
        return contentNames.TryGetValue((contentType, contentId), out var title)
            ? NormalizeText(title, serverBaseUrl)
            : string.Empty;
    }

    private static SourceHealthSnapshot BuildHealthSnapshot(SourceHealthReport? report, string serverBaseUrl)
    {
        if (report == null)
        {
            return new SourceHealthSnapshot();
        }

        return new SourceHealthSnapshot
        {
            State = report.HealthState.ToString(),
            Score = report.HealthScore,
            StatusSummary = NormalizeText(report.StatusSummary, serverBaseUrl),
            ImportResultSummary = NormalizeText(report.ImportResultSummary, serverBaseUrl),
            ValidationSummary = NormalizeText(report.ValidationSummary, serverBaseUrl),
            TopIssueSummary = NormalizeText(report.TopIssueSummary, serverBaseUrl),
            DuplicateCount = report.DuplicateCount,
            InvalidStreamCount = report.InvalidStreamCount,
            ChannelsWithEpgMatchCount = report.ChannelsWithEpgMatchCount,
            ChannelsWithLogoCount = report.ChannelsWithLogoCount,
            SuspiciousEntryCount = report.SuspiciousEntryCount,
            WarningCount = report.WarningCount,
            ErrorCount = report.ErrorCount,
            Components = report.Components
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.ComponentType.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(item => new SourceHealthComponentSnapshot
                {
                    Type = item.ComponentType.ToString(),
                    State = item.State.ToString(),
                    Score = item.Score,
                    Summary = NormalizeText(item.Summary, serverBaseUrl),
                    RelevantCount = item.RelevantCount,
                    HealthyCount = item.HealthyCount,
                    IssueCount = item.IssueCount
                }).ToList(),
            Probes = report.Probes
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.ProbeType.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(item => new SourceHealthProbeSnapshot
                {
                    Type = item.ProbeType.ToString(),
                    Status = item.Status.ToString(),
                    CandidateCount = item.CandidateCount,
                    SampleSize = item.SampleSize,
                    SuccessCount = item.SuccessCount,
                    FailureCount = item.FailureCount,
                    TimeoutCount = item.TimeoutCount,
                    HttpErrorCount = item.HttpErrorCount,
                    TransportErrorCount = item.TransportErrorCount,
                    Summary = NormalizeText(item.Summary, serverBaseUrl)
                }).ToList(),
            Issues = report.Issues
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .Select(item => new SourceHealthIssueSnapshot
                {
                    Severity = item.Severity.ToString(),
                    Code = item.Code,
                    Title = NormalizeText(item.Title, serverBaseUrl),
                    AffectedCount = item.AffectedCount,
                    SampleItems = NormalizeText(item.SampleItems, serverBaseUrl)
                }).ToList()
        };
    }

    private static SourceEpgSnapshot BuildEpgSnapshot(EpgSyncLog? log, string serverBaseUrl)
    {
        if (log == null)
        {
            return new SourceEpgSnapshot();
        }

        return new SourceEpgSnapshot
        {
            IsPresent = true,
            IsSuccess = log.IsSuccess,
            Status = log.Status.ToString(),
            ResultCode = log.ResultCode.ToString(),
            FailureStage = log.FailureStage.ToString(),
            ActiveMode = log.ActiveMode.ToString(),
            ActiveXmltvUrl = NormalizeText(log.ActiveXmltvUrl, serverBaseUrl),
            MatchedChannelCount = log.MatchedChannelCount,
            UnmatchedChannelCount = log.UnmatchedChannelCount,
            CurrentCoverageCount = log.CurrentCoverageCount,
            NextCoverageCount = log.NextCoverageCount,
            TotalLiveChannelCount = log.TotalLiveChannelCount,
            ProgrammeCount = log.ProgrammeCount,
            MatchBreakdown = NormalizeText(log.MatchBreakdown, serverBaseUrl),
            FailureReason = NormalizeText(log.FailureReason, serverBaseUrl)
        };
    }

    private static string ResolveTokens(string value, string serverBaseUrl)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("{{server}}", serverBaseUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? value, string serverBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Replace(serverBaseUrl, "[fixture-server]", StringComparison.OrdinalIgnoreCase);
        return AbsoluteSyncTimestampPattern.Replace(normalized, "[timestamp]");
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Kroira.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from the regression runner output path.");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class RuntimeSource
    {
        public RegressionSourceDefinition Definition { get; init; } = new();
        public int SourceProfileId { get; init; }
        public SourceRefreshResult? Result { get; set; }
    }

    private sealed record ChannelFixtureRow(int SourceProfileId, string CategoryName, Channel Channel);
}

internal sealed class RegressionRunResult
{
    public List<string> DiscoveredCaseIds { get; init; } = [];
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int UpdatedBaselineCount { get; set; }
}
