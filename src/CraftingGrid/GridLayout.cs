using System;
using System.Collections.Generic;
using System.Linq;

namespace CasualtiesUnknown.CraftingGrid
{
    // Pure geometry: eligibility, discovery, localization and hover never enter the layout.
    public sealed class LayoutEntry
    {
        public int Key, Level;
        public string Category, SortName, StableId;
    }
    public sealed class Band
    {
        public int Low, High, Width, Height;
        public readonly List<LayoutEntry> Entries = new List<LayoutEntry>();
    }
    public struct Cell : IEquatable<Cell>
    {
        public int Strip, X, Y;
        public Cell(int strip, int x, int y) { Strip = strip; X = x; Y = y; }
        public bool Equals(Cell c) => Strip == c.Strip && X == c.X && Y == c.Y;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => (Strip * 397 ^ X) * 397 ^ Y;
    }
    public sealed class Strip
    {
        public int Width, Height;
        public readonly HashSet<Cell> Occupied = new HashSet<Cell>();
    }
    public sealed class BoardLayout
    {
        public readonly List<Band> Bands = new List<Band>();
        public readonly List<Strip> Strips = new List<Strip>();
        public readonly Dictionary<int, Cell> Cells = new Dictionary<int, Cell>();
        public int Columns;
    }
    public static class GridLayout
    {
        public static BoardLayout CreateVertical(IEnumerable<LayoutEntry> entries)
        {
            // Transpose the same canonical catalog layout: levels now progress downward.
            var original = Create(entries);
            var board = new BoardLayout();
            if (original.Strips.Count == 0) return board;
            board.Bands.AddRange(original.Bands);
            board.Columns = original.Strips[0].Height;
            var strip = new Strip { Width = board.Columns, Height = original.Columns };
            board.Strips.Add(strip);
            foreach (var pair in original.Cells)
            {
                var cell = new Cell(0,pair.Value.Y,pair.Value.X);
                board.Cells.Add(pair.Key,cell); strip.Occupied.Add(cell);
            }
            return board;
        }
        static readonly string[] Categories = { "Food", "Materials", "Medicine", "Tools", "Utilities" };
        static string[] OrderedCategories(IEnumerable<LayoutEntry> entries) => entries.Select(e => e.Category).Distinct()
            .OrderBy(c => { int i = Array.IndexOf(Categories, c); return i < 0 ? int.MaxValue : i; })
            .ThenBy(c => c, StringComparer.Ordinal).ToArray();
        static int Height(IEnumerable<LayoutEntry> entries, int width) => entries.GroupBy(e => e.Category).Sum(g => (g.Count() + width - 1) / width);
        static void Finish(BoardLayout board, List<LayoutEntry> entries)
        {
            if (entries.Count == 0) return;
            int target = Math.Max(10, entries.Select(e => e.Category).Distinct().Count()), width = 1;
            while (Height(entries, width) > target) width++;
            var band = new Band { Low = entries.Min(e => e.Level), High = entries.Max(e => e.Level), Width = width, Height = Height(entries, width) };
            foreach (string category in OrderedCategories(entries))
                band.Entries.AddRange(entries.Where(e => e.Category == category).OrderBy(e => e.Level)
                    .ThenBy(e => e.SortName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("en"), false))
                    .ThenBy(e => e.StableId, StringComparer.Ordinal).ThenBy(e => e.Key));
            board.Bands.Add(band);
            entries.Clear();
        }
        public static BoardLayout Create(IEnumerable<LayoutEntry> entries, int maxColumns = int.MaxValue)
        {
            var board = new BoardLayout();
            var pending = new List<LayoutEntry>();
            int previous = int.MinValue;
            foreach (var level in entries.GroupBy(e => e.Level).OrderBy(g => g.Key))
            {
                var group = level.ToList();
                if (previous != int.MinValue && level.Key != previous + 1) Finish(board, pending);
                if (group.Count >= 7) { Finish(board, pending); Finish(board, group); }
                else
                {
                    if (pending.Count > 0 && Height(pending.Concat(group), 2) > Math.Max(10, pending.Concat(group).Select(e => e.Category).Distinct().Count()))
                        Finish(board, pending);
                    pending.AddRange(group);
                }
                previous = level.Key;
            }
            Finish(board, pending);
            if (board.Bands.Count == 0) return board;
            board.Columns = Math.Min(board.Bands.Sum(b => b.Width), Math.Max(board.Bands.Max(b => b.Width), maxColumns));
            int offset = 0, stripIndex = -1;
            Strip strip = null;
            foreach (var band in board.Bands)
            {
                if (strip == null || offset + band.Width > board.Columns)
                {
                    strip = new Strip { Width = board.Columns };
                    board.Strips.Add(strip); stripIndex++; offset = 0;
                }
                int row = 0;
                foreach (var category in band.Entries.GroupBy(e => e.Category))
                {
                    int n = 0;
                    foreach (var entry in category)
                    {
                        var cell = new Cell(stripIndex, offset + n % band.Width, row + n / band.Width);
                        board.Cells.Add(entry.Key, cell); strip.Occupied.Add(cell); n++;
                    }
                    row += (n + band.Width - 1) / band.Width;
                }
                strip.Height = Math.Max(strip.Height, row); offset += band.Width;
            }
            return board;
        }
    }

}
