#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public sealed record SmartOriginalProviderGroup(string Key, string Name, int Count);

    public sealed class SmartCategoryIndex<TItem>
        where TItem : notnull
    {
        public static SmartCategoryIndex<TItem> Empty { get; } = new(
            Array.Empty<TItem>(),
            new Dictionary<string, IReadOnlyList<TItem>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<SmartOriginalProviderGroup>(),
            0);

        internal SmartCategoryIndex(
            IReadOnlyList<TItem> allItems,
            IReadOnlyDictionary<string, IReadOnlyList<TItem>> itemsByCategoryKey,
            IReadOnlyDictionary<string, int> countsByCategoryKey,
            IReadOnlyList<SmartOriginalProviderGroup> originalProviderGroups,
            int contextBuildCount)
        {
            AllItems = allItems;
            ItemsByCategoryKey = itemsByCategoryKey;
            CountsByCategoryKey = countsByCategoryKey;
            OriginalProviderGroups = originalProviderGroups;
            ContextBuildCount = contextBuildCount;
        }

        public IReadOnlyList<TItem> AllItems { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<TItem>> ItemsByCategoryKey { get; }
        public IReadOnlyDictionary<string, int> CountsByCategoryKey { get; }
        public IReadOnlyList<SmartOriginalProviderGroup> OriginalProviderGroups { get; }
        public int ContextBuildCount { get; }

        public IReadOnlyList<TItem> GetItems(string? categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryKey))
            {
                return AllItems;
            }

            return ItemsByCategoryKey.TryGetValue(categoryKey, out var items)
                ? items
                : Array.Empty<TItem>();
        }

        public int GetCount(string? categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryKey))
            {
                return AllItems.Count;
            }

            return CountsByCategoryKey.TryGetValue(categoryKey, out var count) ? count : 0;
        }
    }

    public static class SmartCategoryIndexBuilder
    {
        public static SmartCategoryIndex<TItem> Build<TItem>(
            IEnumerable<TItem> items,
            IEnumerable<SmartCategoryDefinition> definitions,
            Func<TItem, SmartCategoryItemContext> contextFactory,
            Func<TItem, IEnumerable<string>> providerGroupSelector,
            ISmartCategoryService smartCategoryService)
            where TItem : notnull
        {
            var allItems = items.ToList();
            var definitionList = definitions.ToList();
            var byKey = new Dictionary<string, List<TItem>>(StringComparer.OrdinalIgnoreCase);
            var providerNamesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var contextBuildCount = 0;

            foreach (var item in allItems)
            {
                var context = contextFactory(item);
                contextBuildCount++;

                foreach (var definition in definitionList)
                {
                    if (definition.IsAllCategory)
                    {
                        continue;
                    }

                    if (definition.Predicate(context))
                    {
                        AddItem(byKey, definition.Id, item);
                    }
                }

                var providerKeysForItem = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var providerGroup in providerGroupSelector(item))
                {
                    if (string.IsNullOrWhiteSpace(providerGroup))
                    {
                        continue;
                    }

                    var providerKey = smartCategoryService.BuildOriginalProviderGroupKey(providerGroup);
                    if (string.IsNullOrWhiteSpace(providerKey) || !providerKeysForItem.Add(providerKey))
                    {
                        continue;
                    }

                    AddItem(byKey, providerKey, item);
                    providerNamesByKey.TryAdd(providerKey, providerGroup.Trim());
                }
            }

            var readOnlyItemsByKey = byKey.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TItem>)pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var countsByKey = byKey.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Count,
                StringComparer.OrdinalIgnoreCase);
            countsByKey[string.Empty] = allItems.Count;

            var originalProviderGroups = providerNamesByKey
                .Select(pair => new SmartOriginalProviderGroup(
                    pair.Key,
                    pair.Value,
                    countsByKey.TryGetValue(pair.Key, out var count) ? count : 0))
                .Where(group => group.Count > 0)
                .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new SmartCategoryIndex<TItem>(
                allItems,
                readOnlyItemsByKey,
                countsByKey,
                originalProviderGroups,
                contextBuildCount);
        }

        private static void AddItem<TItem>(
            IDictionary<string, List<TItem>> itemsByKey,
            string key,
            TItem item)
            where TItem : notnull
        {
            if (!itemsByKey.TryGetValue(key, out var items))
            {
                items = new List<TItem>();
                itemsByKey[key] = items;
            }

            items.Add(item);
        }
    }
}
