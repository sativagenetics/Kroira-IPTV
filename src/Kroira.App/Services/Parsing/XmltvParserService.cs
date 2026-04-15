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
                    
                    var targetCh = channels.FirstOrDefault(c => c.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase) || c.Name.Equals(chIdNode, StringComparison.OrdinalIgnoreCase));
                    if (targetCh == null) continue;

                    var startString = p.Attribute("start")?.Value;
                    var stopString = p.Attribute("stop")?.Value;
                    if (startString == null || stopString == null) continue;

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
                    var existingEpg = await db.EpgPrograms.Where(e => chIds.Contains(e.ChannelId)).ToListAsync();
                    db.EpgPrograms.RemoveRange(existingEpg);

                    db.EpgPrograms.AddRange(epgItems);

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                    if (syncState != null)
                    {
                        syncState.LastAttempt = DateTime.UtcNow;
                        syncState.HttpStatusCode = 200;
                        syncState.ErrorLog = $"EPG Sync: {epgItems.Count} programs imported.";
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
                if (dateStr.Length >= 14)
                {
                    string substr = dateStr.Substring(0, 14);
                    return DateTime.ParseExact(substr, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
                }
            }
            catch { }
            return DateTime.UtcNow;
        }
    }
}
