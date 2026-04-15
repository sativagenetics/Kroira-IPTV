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
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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
                    string cleanId  = chIdNode.Trim();

                    // --- Channel matching: exact first, then normalized fallback ---
                    var targetCh = channels.FirstOrDefault(c =>
                        string.Equals(c.Name.Trim(), cleanName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Name.Trim(), cleanId,   StringComparison.OrdinalIgnoreCase));

                    if (targetCh == null)
                    {
                        // Normalized fallback: strip trailing HD/SD/FHD/4K qualifiers and retry
                        string normIncoming = NormalizeChannelName(cleanName);
                        targetCh = channels.FirstOrDefault(c =>
                            string.Equals(NormalizeChannelName(c.Name), normIncoming, StringComparison.OrdinalIgnoreCase));
                    }

                    if (targetCh == null) continue;

                    var startString = p.Attribute("start")?.Value;
                    var stopString  = p.Attribute("stop")?.Value;
                    if (string.IsNullOrWhiteSpace(startString) || string.IsNullOrWhiteSpace(stopString)) continue;

                    var start = ParseXmltvDate(startString);
                    var end   = ParseXmltvDate(stopString);

                    // Skip rows with unparseable timestamps or zero/negative duration
                    if (start == null || end == null || end <= start) continue;

                    var titleNode = p.Element("title");
                    var descNode  = p.Element("desc");

                    epgItems.Add(new EpgProgram
                    {
                        ChannelId    = targetCh.Id,
                        StartTimeUtc = start.Value,
                        EndTimeUtc   = end.Value,
                        Title        = string.IsNullOrWhiteSpace(titleNode?.Value) ? "Unknown Program" : titleNode!.Value.Trim(),
                        Description  = descNode?.Value?.Trim() ?? string.Empty
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
                        var matchedChannels = epgItems.Select(e => e.ChannelId).Distinct().Count();
                        syncState.ErrorLog = $"EPG Sync: {epgItems.Count} programs across {matchedChannels} channels imported. ({programmes.Count} raw programme entries processed)";
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

        private static DateTime? ParseXmltvDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return null;
            try
            {
                dateStr = dateStr.Trim();
                if (dateStr.Length >= 14)
                {
                    string formatNode = dateStr.Substring(0, 14);

                    // Offset: may be separated by a space ("20240101120000 +0200")
                    // or attached directly ("20240101120000+0200").
                    // Positions 14+ after trimming the datetime digits.
                    string offsetNode = "+0000";
                    if (dateStr.Length >= 15)
                    {
                        string remainder = dateStr.Substring(14).Trim();
                        if (remainder.Length >= 5)
                            offsetNode = remainder.Substring(0, 5); // e.g. "+0200" or "-0500"
                    }

                    // Normalise: DateTimeOffset expects "zzz" = +HH:mm; convert +HHMM → +HH:mm
                    if (offsetNode.Length == 5 && !offsetNode.Contains(':'))
                        offsetNode = offsetNode.Insert(3, ":");

                    if (DateTimeOffset.TryParseExact(
                            $"{formatNode} {offsetNode}",
                            "yyyyMMddHHmmss zzz",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out var dto))
                    {
                        return dto.UtcDateTime;
                    }

                    // Last-resort: treat as UTC
                    if (DateTime.TryParseExact(
                            formatNode,
                            "yyyyMMddHHmmss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var dt))
                    {
                        return dt;
                    }
                }
            }
            catch { }
            return null; // Caller will skip this programme row
        }

        /// <summary>
        /// Strips common quality/region suffixes so "CNN HD" matches "CNN" and vice-versa.
        /// </summary>
        private static string NormalizeChannelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            // Remove trailing qualifiers (case-insensitive)
            var suffixes = new[] { " HD", " SD", " FHD", " 4K", " UHD", " (HD)", " (SD)" };
            string result = name.Trim();
            foreach (var s in suffixes)
                if (result.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    result = result.Substring(0, result.Length - s.Length).TrimEnd();
            return result;
        }
    }
}
