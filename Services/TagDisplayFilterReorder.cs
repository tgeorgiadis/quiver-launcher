using QuiverLauncher;

namespace QuiverLauncher.Services
{
    public static class TagDisplayFilterReorder
    {
        public static void Move(List<TagDisplayFilter> filters, int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || toIndex < 0 || fromIndex >= filters.Count || toIndex >= filters.Count)
                return;

            if (fromIndex == toIndex)
                return;

            var item = filters[fromIndex];
            filters.RemoveAt(fromIndex);
            filters.Insert(toIndex, item);
        }
    }
}
