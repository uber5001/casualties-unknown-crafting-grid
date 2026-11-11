using System;
using System.Collections.Generic;
using System.Linq;

namespace CasualtiesUnknown.CraftingGrid
{
    public sealed class LayoutEntry
    {
        public int Key;
        public string Category, OutputName, StableId;
        public bool Repair;
        public string[] Ingredients = Array.Empty<string>();
    }
    public sealed class RecipeLayoutSpec
    {
        public int ReferenceColumns { get; set; }
        public List<List<RecipeGroup>> Bands { get; set; }
    }
    public sealed class RecipeGroup
    {
        public string Group { get; set; }
        public int Columns { get; set; }
        public List<RecipeSelector> Recipes { get; set; }
    }
    public sealed class RecipeSelector
    {
        public string ReferenceId { get; set; }
        public string Label { get; set; }
        public string Output { get; set; }
        public bool Repair { get; set; }
        public string VariantIngredient { get; set; }
    }
    public struct Cell : IEquatable<Cell>
    {
        public int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell c) => X == c.X && Y == c.Y;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => X * 397 ^ Y;
    }
    public sealed class BoardLayout
    {
        public readonly Dictionary<int, Cell> Cells = new Dictionary<int, Cell>();
        public int Columns, Rows, MappedCount;
    }
    public static class GridLayout
    {
        // Selectors use canonical English output names, never localized UI labels or registry indices.
        static string Normalize(string name) => new string((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        static bool Matches(LayoutEntry entry, RecipeSelector selector) =>
            Normalize(entry.OutputName) == Normalize(selector.Output ?? selector.Label) && entry.Repair == selector.Repair &&
            (string.IsNullOrEmpty(selector.VariantIngredient) || entry.Ingredients.Any(i => Normalize(i) == Normalize(selector.VariantIngredient)));

        public static BoardLayout Create(IEnumerable<LayoutEntry> entries, RecipeLayoutSpec spec, int maxColumns = int.MaxValue)
        {
            if (spec == null || spec.ReferenceColumns < 1 || spec.Bands == null || spec.Bands.Any(b => b == null || b.Any(g =>
                g == null || g.Columns < 1 || g.Recipes == null || g.Recipes.Any(r => r == null || string.IsNullOrWhiteSpace(r.Output ?? r.Label)))))
                throw new ArgumentException("recipe-layout.json must define a reference width, positive group widths, and recipe selectors.", nameof(spec));
            if (maxColumns < 1) throw new ArgumentOutOfRangeException(nameof(maxColumns));
            var board = new BoardLayout { Columns = Math.Min(spec.ReferenceColumns,maxColumns) };
            var remaining = entries.OrderBy(e => e.OutputName,StringComparer.Ordinal).ThenBy(e => e.Repair)
                .ThenBy(e => e.StableId,StringComparer.Ordinal).ThenBy(e => e.Key).ToList();
            int y = 0;
            foreach (var band in spec.Bands)
            {
                int x = 0, rowHeight = 0;
                foreach (var group in band)
                {
                    int width = Math.Min(group.Columns,board.Columns);
                    if (x + width > board.Columns) { y += rowHeight; x = 0; rowHeight = 0; }
                    for (int i = 0; i < group.Recipes.Count; i++)
                    {
                        var entry = remaining.FirstOrDefault(e => Matches(e,group.Recipes[i]));
                        // Absent/secret recipes leave their authored position empty; discoveries don't shift neighbors.
                        if (entry == null) continue;
                        board.Cells.Add(entry.Key,new Cell(x+i%width,y+i/width));
                        remaining.Remove(entry); board.MappedCount++;
                    }
                    rowHeight = Math.Max(rowHeight,(group.Recipes.Count+width-1)/width);
                    x += width;
                }
                y += rowHeight;
            }
            // New or modded live recipes are never dropped or inserted into the hand-picked groups.
            for (int i = 0; i < remaining.Count; i++) board.Cells.Add(remaining[i].Key,new Cell(i%board.Columns,y+i/board.Columns));
            board.Rows = y+(remaining.Count+board.Columns-1)/board.Columns;
            return board;
        }
    }
}
