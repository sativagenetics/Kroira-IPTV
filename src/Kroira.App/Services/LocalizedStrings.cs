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
        private static ResourceLoader? loader;
        private static bool resourceLoaderUnavailable;
        private static IReadOnlyDictionary<string, string>? fallbackResources;

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var value = GetFromResourceLoader(key);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (fallbackResources == null)
            {
                fallbackResources = LoadFallbackResources();
            }

            if (fallbackResources.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            return string.IsNullOrEmpty(value) ? key : value;
        }

        public static string Format(string key, params object?[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }

        private static string GetFromResourceLoader(string key)
        {
            if (resourceLoaderUnavailable)
            {
                return string.Empty;
            }

            try
            {
                loader ??= new ResourceLoader();
                return loader.GetString(key);
            }
            catch
            {
                resourceLoaderUnavailable = true;
                return string.Empty;
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
