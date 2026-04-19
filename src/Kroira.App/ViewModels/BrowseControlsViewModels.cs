#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kroira.App.ViewModels
{
    public sealed class BrowseSortOptionViewModel
    {
        public BrowseSortOptionViewModel(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; }
    }

    public sealed class BrowseSourceFilterOptionViewModel
    {
        public BrowseSourceFilterOptionViewModel(int id, string name, int itemCount)
        {
            Id = id;
            Name = name;
            ItemCount = itemCount;
        }

        public int Id { get; }
        public string Name { get; }
        public int ItemCount { get; }
        public string DisplayName => ItemCount > 0 ? $"{Name} ({ItemCount:N0})" : Name;
    }

    public sealed partial class BrowseSourceVisibilityViewModel : ObservableObject
    {
        private readonly Action<BrowseSourceVisibilityViewModel, bool>? _onChanged;

        public BrowseSourceVisibilityViewModel(int id, string name, string detail, bool isVisible, Action<BrowseSourceVisibilityViewModel, bool>? onChanged)
        {
            Id = id;
            Name = name;
            Detail = detail;
            _isVisible = isVisible;
            _onChanged = onChanged;
        }

        public int Id { get; }
        public string Name { get; }
        public string Detail { get; }

        [ObservableProperty]
        private bool _isVisible;

        partial void OnIsVisibleChanged(bool value)
        {
            _onChanged?.Invoke(this, value);
        }
    }

    public sealed class BrowseCategoryManagerOptionViewModel
    {
        public BrowseCategoryManagerOptionViewModel(string key, string rawName, string effectiveName, int itemCount, bool isHidden)
        {
            Key = key;
            RawName = rawName;
            EffectiveName = effectiveName;
            ItemCount = itemCount;
            IsHidden = isHidden;
        }

        public string Key { get; }
        public string RawName { get; }
        public string EffectiveName { get; }
        public int ItemCount { get; }
        public bool IsHidden { get; }

        public string DisplayName
        {
            get
            {
                var detail = IsHidden ? "hidden" : EffectiveName;
                return ItemCount > 0
                    ? $"{RawName} -> {detail} ({ItemCount:N0})"
                    : $"{RawName} -> {detail}";
            }
        }
    }
}
