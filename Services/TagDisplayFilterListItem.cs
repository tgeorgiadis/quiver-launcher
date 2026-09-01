using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuiverLauncher.Services
{
    public class TagDisplayFilterListItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public TagDisplayFilter Filter { get; init; } = null!;
        public string Id => Filter.Id;
        public string Name => Filter.Name;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static TagDisplayFilterListItem FromFilter(TagDisplayFilter filter, bool isSelected) =>
            new()
            {
                Filter = filter,
                IsSelected = isSelected,
            };

        /// <summary>
        /// Updates <see cref="IsSelected"/> in place when the filter ids and order match.
        /// Returns false when the list must be rebuilt (add, delete, or reorder).
        /// </summary>
        public static bool TryUpdateSelection(
            IList<TagDisplayFilterListItem> items,
            IReadOnlyList<TagDisplayFilter> filters,
            string? activeFilterId)
        {
            if (items.Count != filters.Count)
                return false;

            for (var i = 0; i < items.Count; i++)
            {
                if (!string.Equals(items[i].Id, filters[i].Id, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            foreach (var item in items)
            {
                item.IsSelected = string.Equals(
                    item.Id,
                    activeFilterId,
                    StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
