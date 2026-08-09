using QuiverLauncher;

namespace QuiverLauncher.Services
{
    public class TagDisplayFilterListItem
    {
        public TagDisplayFilter Filter { get; init; } = null!;
        public string Id => Filter.Id;
        public string Name => Filter.Name;
        public bool IsSelected { get; init; }

        public static TagDisplayFilterListItem FromFilter(TagDisplayFilter filter, bool isSelected) =>
            new()
            {
                Filter = filter,
                IsSelected = isSelected,
            };
    }
}
