#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kroira.App.Data
{
    public sealed class MonotonicMigrationsIdGenerator : IMigrationsIdGenerator
    {
        private const string TimestampFormat = "yyyyMMddHHmmss";

        private readonly object _gate = new();
        private readonly DateTime _maxExistingTimestamp = DiscoverMaxExistingTimestamp();
        private DateTime _lastIssuedTimestamp = DateTime.MinValue;

        public string GenerateId(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Migration name cannot be empty.", nameof(name));
            }

            lock (_gate)
            {
                var now = TrimToSecond(DateTime.Now);
                var floor = _lastIssuedTimestamp > _maxExistingTimestamp
                    ? _lastIssuedTimestamp
                    : _maxExistingTimestamp;
                var next = now > floor
                    ? now
                    : floor.AddSeconds(1);

                _lastIssuedTimestamp = next;
                return FormattableString.Invariant($"{next.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{name.Trim()}");
            }
        }

        public string GetName(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Migration id cannot be empty.", nameof(id));
            }

            var separatorIndex = id.IndexOf('_');
            return separatorIndex >= 0 && separatorIndex + 1 < id.Length
                ? id[(separatorIndex + 1)..]
                : id;
        }

        public bool IsValidId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= 15 || value[14] != '_')
            {
                return false;
            }

            for (var index = 0; index < 14; index++)
            {
                if (!char.IsDigit(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static DateTime DiscoverMaxExistingTimestamp()
        {
            var maxTimestamp = DateTime.MinValue;
            foreach (var type in GetLoadableTypes(typeof(AppDbContext).Assembly))
            {
                var attribute = type.GetCustomAttribute<MigrationAttribute>();
                if (attribute == null || !TryParseTimestamp(attribute.Id, out var timestamp))
                {
                    continue;
                }

                if (timestamp > maxTimestamp)
                {
                    maxTimestamp = timestamp;
                }
            }

            return maxTimestamp;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.OfType<Type>();
            }
        }

        private static DateTime TrimToSecond(DateTime value)
        {
            return new DateTime(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                value.Second,
                value.Kind);
        }

        private static bool TryParseTimestamp(string migrationId, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(migrationId) || migrationId.Length < 14)
            {
                return false;
            }

            return DateTime.TryParseExact(
                migrationId[..14],
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp);
        }
    }
}
