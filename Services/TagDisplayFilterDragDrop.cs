namespace QuiverLauncher.Services
{
    public static class TagDisplayFilterDragDrop
    {
        public static int ResolveInsertIndex(
            int itemCount,
            int dragIndex,
            double pointerY,
            double listTop,
            double rowStride)
        {
            if (itemCount <= 0 || dragIndex < 0 || rowStride <= 0)
                return dragIndex;

            var raw = (int)Math.Floor((pointerY - listTop) / rowStride);
            return Math.Clamp(raw, 0, itemCount - 1);
        }
    }
}
