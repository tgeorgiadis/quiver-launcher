using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuiverLauncher.Services
{
    public class TagDisplayFilterListItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private TagDisplayFilter _filter = null!;
        private string _name = string.Empty;

        public TagDisplayFilter Filter
        {
            get => _filter;
            init { _filter = value; _name = value.Name; }
        }
        public string Id => Filter.Id;
        public string Name => _name;

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
        /// Updates filter data and selection in place when the filter ids and order match.
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

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item._filter = filters[i];
                // The editor mutates the existing filter, so retain the last
                // displayed name to detect renames and notify the menu binding.
                if (item._name != filters[i].Name)
                {
                    item._name = filters[i].Name;
                    item.OnPropertyChanged(nameof(Name));
                }
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
