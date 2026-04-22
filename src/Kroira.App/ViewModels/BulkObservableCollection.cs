#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Kroira.App.ViewModels
{
    public readonly record struct CollectionPatchResult(
        int ReusedCount,
        int InsertedCount,
        int RemovedCount,
        int MovedCount,
        int UpdatedCount);

    public sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void AppendRange(IEnumerable<T> items)
        {
            CheckReentrancy();

            var appendedItems = items.ToList();
            if (appendedItems.Count == 0)
            {
                return;
            }

            var startIndex = Items.Count;
            foreach (var item in appendedItems)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                appendedItems,
                startIndex));
        }

        public CollectionPatchResult PatchToMatch(
            IEnumerable<T> items,
            Func<T, T, bool> matches,
            Action<T, T>? updateExisting = null)
        {
            CheckReentrancy();

            var desiredItems = items.ToList();
            var reused = 0;
            var inserted = 0;
            var removed = 0;
            var moved = 0;
            var updated = 0;

            for (var index = 0; index < desiredItems.Count; index++)
            {
                var desiredItem = desiredItems[index];
                if (index < Items.Count)
                {
                    var existingItem = Items[index];
                    if (matches(existingItem, desiredItem))
                    {
                        reused++;
                        if (updateExisting != null)
                        {
                            updateExisting(existingItem, desiredItem);
                            updated++;
                        }

                        continue;
                    }

                    var existingIndex = FindMatchIndex(index + 1, desiredItem, matches);
                    if (existingIndex >= 0)
                    {
                        Move(existingIndex, index);
                        reused++;
                        moved++;
                        if (updateExisting != null)
                        {
                            updateExisting(Items[index], desiredItem);
                            updated++;
                        }

                        continue;
                    }

                    Insert(index, desiredItem);
                    inserted++;
                    continue;
                }

                Add(desiredItem);
                inserted++;
            }

            while (Items.Count > desiredItems.Count)
            {
                RemoveAt(Items.Count - 1);
                removed++;
            }

            return new CollectionPatchResult(reused, inserted, removed, moved, updated);
        }

        private int FindMatchIndex(int startIndex, T desiredItem, Func<T, T, bool> matches)
        {
            for (var index = startIndex; index < Items.Count; index++)
            {
                if (matches(Items[index], desiredItem))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
