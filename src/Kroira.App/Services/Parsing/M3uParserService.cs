using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Parsing
{
    public interface IM3uParserService
    {
        Task ParseAndImportM3uAsync(AppDbContext db, int sourceProfileId);
    }

    public class M3uParserService : IM3uParserService
    {
        private static readonly Regex _groupRegex = new Regex(@"group-title=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex _logoRegex = new Regex(@"tvg-logo=""([^""]*)""", RegexOptions.Compiled);

        public async Task ParseAndImportM3uAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url))
                throw new Exception("Source URL or Path is empty.");

            string content;
            if (cred.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient();
                content = await client.GetStringAsync(cred.Url);
            }
            else
            {
                content = await System.IO.File.ReadAllTextAsync(cred.Url);
            }

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var parsedEntries = new List<M3uEntry>();

            string currentGroup = "Uncategorized";
            string currentLogo = string.Empty;
            string currentName = string.Empty;
            bool expectsUrl = false;
            int totalChannels = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("#EXTINF:"))
                {
                    var groupMatch = _groupRegex.Match(line);
                    currentGroup = groupMatch.Success ? groupMatch.Groups[1].Value.Trim() : "Uncategorized";
                    if (string.IsNullOrWhiteSpace(currentGroup)) currentGroup = "Uncategorized";

                    var logoMatch = _logoRegex.Match(line);
                    currentLogo = logoMatch.Success ? logoMatch.Groups[1].Value.Trim() : string.Empty;

                    var commaIndex = line.LastIndexOf(',');
                    currentName = (commaIndex != -1 && commaIndex < line.Length - 1)
                        ? line.Substring(commaIndex + 1).Trim()
                        : "Unknown Channel";

                    expectsUrl = true;
                }
                else if (expectsUrl && !line.StartsWith("#"))
                {
                    var url = line.Trim();
                    parsedEntries.Add(new M3uEntry
                    {
                        GroupName = currentGroup,
                        Name = currentName,
                        Url = url,
                        LogoUrl = currentLogo
                    });

                    expectsUrl = false;
                }
            }

            var categoryLabels = ContentClassifier.BuildCategoryLabelSet(parsedEntries.Select(entry => entry.GroupName));
            var categoriesDict = new Dictionary<string, ChannelCategory>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in parsedEntries)
            {
                if (ContentClassifier.IsGarbageCategoryName(entry.GroupName)) continue;
                if (!ContentClassifier.IsPlayableM3uLiveChannel(entry.Name, entry.Url, categoryLabels)) continue;

                if (!categoriesDict.TryGetValue(entry.GroupName, out var category))
                {
                    category = new ChannelCategory
                    {
                        SourceProfileId = sourceProfileId,
                        Name = entry.GroupName,
                        OrderIndex = categoriesDict.Count,
                        Channels = new List<Channel>()
                    };
                    categoriesDict[entry.GroupName] = category;
                }

                category.Channels.Add(new Channel
                {
                    Name = entry.Name,
                    StreamUrl = entry.Url,
                    LogoUrl = entry.LogoUrl
                });

                totalChannels++;
            }

            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // Destructive reload mapping for clean parity
                var existingCats = await db.ChannelCategories.Where(c => c.SourceProfileId == sourceProfileId).ToListAsync();
                var catIds = existingCats.Select(c => c.Id).ToList();
                var existingChannels = await db.Channels.Where(ch => catIds.Contains(ch.ChannelCategoryId)).ToListAsync();
                db.Channels.RemoveRange(existingChannels);
                db.ChannelCategories.RemoveRange(existingCats);
                await db.SaveChangesAsync();

                // Build relational graphs entirely generated off M3U extraction arrays
                db.ChannelCategories.AddRange(categoriesDict.Values);

                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 200;
                    syncState.ErrorLog = $"Parsed {totalChannels} channels matching {categoriesDict.Count} distinct categories.";
                }

                var profile = await db.SourceProfiles.FirstOrDefaultAsync(p => p.Id == sourceProfileId);
                if (profile != null)
                {
                    profile.LastSync = DateTime.UtcNow;
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private sealed class M3uEntry
        {
            public string GroupName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
        }
    }
}
