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

    public sealed class BrowseSourceFilterOptionViewModel : ObservableObject
    {
        private string _name;
        private int _itemCount;

        public BrowseSourceFilterOptionViewModel(int id, string name, int itemCount)
        {
            Id = id;
            _name = name;
            _itemCount = itemCount;
        }

        public int Id { get; }
        public string Name => _name;
        public int ItemCount => _itemCount;
        public string DisplayName => ItemCount > 0 ? $"{Name} ({ItemCount:N0})" : Name;

        public void UpdateFrom(BrowseSourceFilterOptionViewModel incoming)
        {
            var changed = false;
            if (!string.Equals(_name, incoming.Name, StringComparison.Ordinal))
            {
                _name = incoming.Name;
                OnPropertyChanged(nameof(Name));
                changed = true;
            }

            if (_itemCount != incoming.ItemCount)
            {
                _itemCount = incoming.ItemCount;
                OnPropertyChanged(nameof(ItemCount));
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public sealed class BrowseFacetOptionViewModel
    {
        public BrowseFacetOptionViewModel(string key, string label, int itemCount)
        {
            Key = key;
            Label = label;
            ItemCount = itemCount;
        }

        public string Key { get; }
        public string Label { get; }
        public int ItemCount { get; }
        public string DisplayName => ItemCount > 0 ? $"{Label} ({ItemCount:N0})" : Label;
    }

    public sealed partial class BrowseSourceVisibilityViewModel : ObservableObject
    {
        private readonly Action<BrowseSourceVisibilityViewModel, bool>? _onChanged;
        private string _name;
        private string _detail;

        public BrowseSourceVisibilityViewModel(int id, string name, string detail, bool isVisible, Action<BrowseSourceVisibilityViewModel, bool>? onChanged)
        {
            Id = id;
            _name = name;
            _detail = detail;
            _isVisible = isVisible;
            _onChanged = onChanged;
        }

        public int Id { get; }
        public string Name => _name;
        public string Detail => _detail;

        [ObservableProperty]
        private bool _isVisible;

        partial void OnIsVisibleChanged(bool value)
        {
            _onChanged?.Invoke(this, value);
        }

        public void UpdateFrom(BrowseSourceVisibilityViewModel incoming)
        {
            if (!string.Equals(_name, incoming.Name, StringComparison.Ordinal))
            {
                _name = incoming.Name;
                OnPropertyChanged(nameof(Name));
            }

            if (!string.Equals(_detail, incoming.Detail, StringComparison.Ordinal))
            {
                _detail = incoming.Detail;
                OnPropertyChanged(nameof(Detail));
            }

            if (IsVisible != incoming.IsVisible)
            {
                IsVisible = incoming.IsVisible;
            }
        }
    }

    public sealed class BrowseCategoryManagerOptionViewModel
    {
        public BrowseCategoryManagerOptionViewModel(
            string key,
            string rawName,
            string effectiveName,
            string autoDisplayName,
            int itemCount,
            bool isHidden,
            bool hasManualAlias)
        {
            Key = key;
            RawName = rawName;
            EffectiveName = effectiveName;
            AutoDisplayName = autoDisplayName;
            ItemCount = itemCount;
            IsHidden = isHidden;
            HasManualAlias = hasManualAlias;
        }

        public string Key { get; }
        public string RawName { get; }
        public string EffectiveName { get; }
        public string AutoDisplayName { get; }
        public int ItemCount { get; }
        public bool IsHidden { get; }
        public bool HasManualAlias { get; }

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
