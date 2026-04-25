#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Kroira.App.Services
{
    public static class LocalizedStrings
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, string> resolvedCache = new(StringComparer.Ordinal);
        private static readonly HashSet<string> missingCache = new(StringComparer.Ordinal);
        private static ResourceLoader? loader;
        private static bool resourceLoaderUnavailable;
        private static IReadOnlyDictionary<string, string>? fallbackResources;
        private static int version;

        public static int Version
        {
            get
            {
                lock (SyncRoot)
                {
                    return version;
                }
            }
        }

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return TryGet(key, out var value) ? value : key;
        }

        public static string GetOrDefault(string key, string fallback)
        {
            return TryGet(key, out var value) ? value : fallback;
        }

        public static bool TryGet(string key, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock (SyncRoot)
            {
                if (resolvedCache.TryGetValue(key, out var cached))
                {
                    value = cached;
                    return true;
                }

                if (missingCache.Contains(key))
                {
                    return false;
                }
            }

            var resolved = ResolveValue(key);
            lock (SyncRoot)
            {
                if (!string.IsNullOrEmpty(resolved))
                {
                    resolvedCache[key] = resolved;
                    value = resolved;
                    return true;
                }

                missingCache.Add(key);
                return false;
            }
        }

        public static void Reset()
        {
            lock (SyncRoot)
            {
                loader = null;
                resourceLoaderUnavailable = false;
                fallbackResources = null;
                resolvedCache.Clear();
                missingCache.Clear();
                version++;
            }
        }

        public static string Format(string key, params object?[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }

        private static string ResolveValue(string key)
        {
            foreach (var candidate in GetKeyCandidates(key))
            {
                var value = GetFromResourceLoader(candidate);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            var fallback = GetFallbackResources();
            foreach (var candidate in GetKeyCandidates(key))
            {
                if (fallback.TryGetValue(candidate, out var value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static IReadOnlyDictionary<string, string> GetFallbackResources()
        {
            lock (SyncRoot)
            {
                if (fallbackResources == null)
                {
                    fallbackResources = LoadFallbackResources();
                }

                return fallbackResources;
            }
        }

        private static string GetFromResourceLoader(string key)
        {
            ResourceLoader? currentLoader;
            lock (SyncRoot)
            {
                if (resourceLoaderUnavailable)
                {
                    return string.Empty;
                }

                currentLoader = loader;
            }

            if (currentLoader == null)
            {
                try
                {
                    currentLoader = new ResourceLoader();
                    lock (SyncRoot)
                    {
                        loader ??= currentLoader;
                        currentLoader = loader;
                    }
                }
                catch
                {
                    lock (SyncRoot)
                    {
                        resourceLoaderUnavailable = true;
                    }

                    return string.Empty;
                }
            }

            try
            {
                return currentLoader.GetString(key);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<string> GetKeyCandidates(string key)
        {
            yield return key;

            if (key.Contains('.', StringComparison.Ordinal))
            {
                var flatKey = key.Replace('.', '_');
                if (!string.Equals(flatKey, key, StringComparison.Ordinal))
                {
                    yield return flatKey;
                }
            }
        }

        private static IReadOnlyDictionary<string, string> LoadFallbackResources()
        {
            try
            {
                var path = FindFallbackResourceFile();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                return XDocument.Load(path)
                    .Descendants("data")
                    .Where(element => element.Attribute("name") != null)
                    .ToDictionary(
                        element => element.Attribute("name")!.Value,
                        element => element.Element("value")?.Value ?? string.Empty,
                        StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static string FindFallbackResourceFile()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; current != null && i < 10; i++, current = current.Parent)
            {
                var copiedCandidate = Path.Combine(current.FullName, "Strings", "en-US", "Resources.resw");
                if (File.Exists(copiedCandidate))
                {
                    return copiedCandidate;
                }

                var sourceCandidate = Path.Combine(current.FullName, "src", "Kroira.App", "Strings", "en-US", "Resources.resw");
                if (File.Exists(sourceCandidate))
                {
                    return sourceCandidate;
                }
            }

            return string.Empty;
        }
    }
}
