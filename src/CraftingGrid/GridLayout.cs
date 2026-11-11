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

    // One connected pocket, seeded for the largest bill; smaller bills share its prefix.
    public sealed class IngredientSpace
    {
        readonly BoardLayout board;
        readonly Dictionary<Cell, double> clearance = new Dictionary<Cell, double>();
        readonly List<List<Cell>> pockets = new List<List<Cell>>();
        readonly Dictionary<int, List<Cell>> cached = new Dictionary<int, List<Cell>>();
        List<Cell> home, homePocket;
        static IEnumerable<Cell> Adjacent(Cell c)
        {
            yield return new Cell(c.Strip, c.X, c.Y - 1); yield return new Cell(c.Strip, c.X - 1, c.Y);
            yield return new Cell(c.Strip, c.X + 1, c.Y); yield return new Cell(c.Strip, c.X, c.Y + 1);
        }
        public IngredientSpace(BoardLayout board, int maxCount)
        {
            this.board = board;
            for (int s = 0; s < board.Strips.Count; s++)
            {
                var strip = board.Strips[s];
                for (int y = 0; y < strip.Height; y++) for (int x = 0; x < strip.Width; x++)
                {
                    var c = new Cell(s, x, y);
                    if (!strip.Occupied.Contains(c)) Add(c);
                }
            }
            var unseen = new HashSet<Cell>(clearance.Keys);
            foreach (Cell cell in clearance.Keys.ToArray())
            {
                if (!unseen.Remove(cell)) continue;
                var pocket = new List<Cell> { cell };
                for (int i = 0; i < pocket.Count; i++) foreach (var next in Adjacent(pocket[i])) if (unseen.Remove(next)) pocket.Add(next);
                pockets.Add(pocket);
            }
            int capacity = pockets.Count == 0 ? 0 : pockets.Max(p => p.Count);
            if (capacity > 0) home = Choose(pockets, Math.Min(capacity, Math.Max(1, maxCount)), out homePocket);
        }
        void Add(Cell c)
        {
            double nearest = double.PositiveInfinity;
            // Strip-local distance is enough to rank pockets without crossing strip boundaries.
            foreach (var a in board.Strips[c.Strip].Occupied) nearest = Math.Min(nearest, (a.X-c.X)*(a.X-c.X)+(a.Y-c.Y)*(a.Y-c.Y));
            clearance[c] = double.IsInfinity(nearest) ? 0 : Math.Sqrt(nearest);
        }
        List<Cell> Grow(List<Cell> pocket, IEnumerable<Cell> initial, int count)
        {
            var allowed = new HashSet<Cell>(pocket); var result = initial.ToList(); var used = new HashSet<Cell>(result);
            while (result.Count < count)
            {
                int left = result.Min(c => c.X), right = result.Max(c => c.X), top = result.Min(c => c.Y), bottom = result.Max(c => c.Y);
                var edge = result.SelectMany(Adjacent).Where(c => allowed.Contains(c) && !used.Contains(c)).Distinct().ToList();
                if (edge.Count == 0) break;
                var best = edge.OrderByDescending(c => {
                    int w = Math.Max(right,c.X)-Math.Min(left,c.X)+1, h = Math.Max(bottom,c.Y)-Math.Min(top,c.Y)+1;
                    return 2 * Adjacent(c).Count(used.Contains) - w*h - .75*Math.Abs(w-h) + .32*clearance[c];
                }).ThenBy(c => c.Y).ThenBy(c => c.X).First();
                used.Add(best); result.Add(best);
            }
            return result;
        }
        List<Cell> Choose(IEnumerable<List<Cell>> groups, int count, out List<Cell> chosenPocket)
        {
            double best = double.NegativeInfinity; List<Cell> winner = null; chosenPocket = null;
            foreach (var pocket in groups.Where(p => p.Count >= count)) foreach (var seed in pocket)
            {
                var cells = Grow(pocket, new[] { seed }, count);
                int w = cells.Max(c => c.X)-cells.Min(c => c.X)+1, h = cells.Max(c => c.Y)-cells.Min(c => c.Y)+1;
                double score = cells.Min(c => clearance[c])+cells.Average(c => clearance[c])-.85*((double)w*h/count-1)-.14*Math.Abs(w-h)+.025*clearance[seed];
                if (score > best) { best = score; winner = cells; chosenPocket = pocket; }
            }
            return winner;
        }
        public List<Cell> Place(int count)
        {
            if (count <= 0 || board.Strips.Count == 0) return new List<Cell>();
            if (cached.TryGetValue(count, out var saved)) return saved;
            List<Cell> cells;
            if (homePocket != null && homePocket.Count >= count)
            {
                if (home.Count < count) home = Grow(homePocket, home, count);
                cells = home.Take(count).ToList();
            }
            else
            {
                cells = Choose(pockets, count, out _);
                if (cells == null)
                {
                    int s = board.Strips.Count-1; var tail = board.Strips[s];
                    var area = pockets.Where(p => p.Any(c => c.Strip == s && c.Y == tail.Height-1)).SelectMany(p => p).ToList();
                    int row = tail.Height;
                    do { for (int x = 0; x < tail.Width; x++) { var c = new Cell(s,x,row); if (!clearance.ContainsKey(c)) Add(c); area.Add(c); } row++; } while (area.Count < count);
                    var seed = home != null && area.Contains(home[0]) ? home[0] : new Cell(s,0,tail.Height);
                    cells = Grow(area, new[] { seed }, count);
                }
            }
            cached[count] = cells; return cells;
        }
    }
}
