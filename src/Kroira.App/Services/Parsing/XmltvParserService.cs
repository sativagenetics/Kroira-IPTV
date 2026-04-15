using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Kroira.App.Services.Parsing
{
    public interface IXmltvParserService
    {
        Task ParseAndImportEpgAsync(AppDbContext db, int sourceProfileId);
    }

    public class XmltvParserService : IXmltvParserService
    {
        public async Task ParseAndImportEpgAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.EpgUrl))
                throw new Exception("EPG URL is not configured for this source.");

            string xmlContent;
            using var client = new HttpClient();
            xmlContent = await client.GetStringAsync(cred.EpgUrl);

            try
            {
                var doc = XDocument.Parse(xmlContent);
                var programmes = doc.Descendants("programme").ToList();
                var channelMapping = doc.Descendants("channel").ToDictionary(
                    c => c.Attribute("id")?.Value ?? string.Empty,
                    c => c.Element("display-name")?.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase
                );

                var channels = await db.Channels
                    .Join(db.ChannelCategories.Where(cat => cat.SourceProfileId == sourceProfileId),
                          ch => ch.ChannelCategoryId,
                          cat => cat.Id,
                          (ch, cat) => ch)
                    .ToListAsync();
                
                var epgItems = new List<EpgProgram>();

                foreach (var p in programmes)
                {
                    var chIdNode = p.Attribute("channel")?.Value;
                    if (string.IsNullOrWhiteSpace(chIdNode)) continue;

                    string displayName = channelMapping.TryGetValue(chIdNode, out var dn) ? dn : chIdNode;
                    string cleanName = displayName.Trim();
                    string cleanId = chIdNode.Trim();
                    var targetCh = channels.FirstOrDefault(c => string.Equals(c.Name.Trim(), cleanName, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name.Trim(), cleanId, StringComparison.OrdinalIgnoreCase));
                    if (targetCh == null) continue;

                    var startString = p.Attribute("start")?.Value;
                    var stopString = p.Attribute("stop")?.Value;
                    if (string.IsNullOrWhiteSpace(startString) || string.IsNullOrWhiteSpace(stopString)) continue;

                    var start = ParseXmltvDate(startString);
                    var end = ParseXmltvDate(stopString);

                    var titleNode = p.Element("title");
                    var descNode = p.Element("desc");

                    epgItems.Add(new EpgProgram
                    {
                        ChannelId = targetCh.Id,
                        StartTimeUtc = start,
                        EndTimeUtc = end,
                        Title = titleNode?.Value ?? "Unknown Program",
                        Description = descNode?.Value ?? string.Empty
                    });
                }

                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    var chIds = channels.Select(c => c.Id).ToList();
                    
                    // Chunked deletion preserving memory across massive EPG scopes natively
                    for (int i = 0; i < chIds.Count; i += 50)
                    {
                        var chunk = chIds.Skip(i).Take(50).ToList();
                        var oldEpg = await db.EpgPrograms.Where(e => chunk.Contains(e.ChannelId)).ToListAsync();
                        if (oldEpg.Any())
                        {
                            db.EpgPrograms.RemoveRange(oldEpg);
                        }
                    }
                    await db.SaveChangesAsync();

                    // Chunked insertion
                    for (int i = 0; i < epgItems.Count; i += 1000)
                    {
                        var chunk = epgItems.Skip(i).Take(1000).ToList();
                        db.EpgPrograms.AddRange(chunk);
                        await db.SaveChangesAsync();
                    }

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                    if (syncState != null)
                    {
                        syncState.LastAttempt = DateTime.UtcNow;
                        syncState.HttpStatusCode = 200;
                        syncState.ErrorLog = $"EPG Sync: Imported {epgItems.Count} programs successfully.";
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
            catch (Exception ex)
            {
                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 500;
                    syncState.ErrorLog = $"EPG parsing failed: {ex.Message}";
                    await db.SaveChangesAsync();
                }
                throw new Exception($"Failed to parse XMLTV EPG: {ex.Message}");
            }
        }

        private DateTime ParseXmltvDate(string dateStr)
        {
            try
            {
                dateStr = dateStr.Trim();
                if (dateStr.Length >= 14)
                {
                    string formatNode = dateStr.Substring(0, 14);
                    string offsetNode = dateStr.Length >= 19 ? dateStr.Substring(15, 5).Replace(" ", "+") : "+0000";
                    
                    if (DateTimeOffset.TryParseExact($"{formatNode} {offsetNode}", "yyyyMMddHHmmss zzz", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dto))
                    {
                        return dto.UtcDateTime;
                    }

                    return DateTime.ParseExact(formatNode, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
                }
            }
            catch { }
            return DateTime.UtcNow;
        }
    }
}
