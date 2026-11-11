using System;

namespace CasualtiesUnknown.CraftingGrid
{
    public static class GridRules
    {
        public static float CellSide(float viewportWidth, int columns)
        {
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            return Math.Max(37,(viewportWidth-8-2*(columns-1))/columns);
        }
        public static bool MatchesSearch(bool known, string name, string search) => string.IsNullOrEmpty(search) || known && name.IndexOf(search,StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool HighlightCategory(bool known, string category, string selectedCategory) => known && selectedCategory != null && category == selectedCategory;
    }
}
