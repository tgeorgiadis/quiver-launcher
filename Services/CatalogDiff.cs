using QuiverLauncher.Models;
using AppCatalogSource = QuiverLauncher.AppCatalogSource;

namespace QuiverLauncher.Services
{
    public class CatalogDiff
    {
        public List<GameInfo> Added { get; set; } = [];
        public List<GameInfo> Removed { get; set; } = [];
        public List<GameInfo> Changed { get; set; } = [];

        public int AddedCount => Added.Count;
        public int RemovedCount => Removed.Count;
        public int ChangedCount => Changed.Count;

        public bool HasChanges => AddedCount > 0 || RemovedCount > 0 || ChangedCount > 0;
    }
}
