using System;

namespace CasualtiesUnknown.CraftingGrid
{
    public static class GridRules
    {
        public static bool MatchesFilter(bool known, string name, string category, string selectedCategory, string search)
        {
            // Search overrides category in the native menu. Unknown names must not leak through search.
            if (!string.IsNullOrEmpty(search)) return known && name.IndexOf(search,StringComparison.OrdinalIgnoreCase) >= 0;
            return selectedCategory == null || category == selectedCategory;
        }
        public static bool Visible(bool known, bool matchesFilter, bool ingredientRelated) => matchesFilter || known && ingredientRelated;
    }
    public sealed class HoverDelay<T> where T : class
    {
        readonly float delay;
        T pending;
        float due;
        public HoverDelay(float seconds) { delay = seconds; }
        public void Enter(T item, float now) { pending = item; due = now+delay; }
        public void Leave(T item) { if (ReferenceEquals(item,pending)) pending = null; }
        public void Clear() { pending = null; }
        public T TakeReady(float now)
        {
            if (pending == null || now < due) return null;
            T result = pending; pending = null; return result;
        }
    }
}
