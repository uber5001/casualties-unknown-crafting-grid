using System.Text.Json;
using System.Text.RegularExpressions;
using CasualtiesUnknown.CraftingGrid;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
var html = File.ReadAllText(args.Length > 0 ? args[0] : "handoff/crafting-reference.html");
var data = JsonDocument.Parse(Regex.Match(html,@"const recipes = (\[.*?\]);").Groups[1].Value);
var entries = data.RootElement.EnumerateArray().Select((r,i) => new LayoutEntry {
    Key = i, Level = r.GetProperty("level").GetInt32(), Category = r.GetProperty("category").GetString(),
    SortName = r.GetProperty("label").GetString(), StableId = r.GetProperty("id").GetString()
}).ToList();
Check(entries.Count == 137,"Fixture recipe count changed");
Check(data.RootElement.EnumerateArray().Select(r => r.GetProperty("name").GetString()).Distinct().Count() == 135,"Fixture output count changed");
var board = GridLayout.Create(entries);
Check(board.Columns == 17 && board.Strips.Count == 1 && board.Strips[0].Height == 10,"Expected 17×10 desktop fixture");
Check(string.Join(",",board.Bands.Select(b => b.Width)) == "1,1,1,1,1,2,2,2,1,2,2,1","Expected reference band widths");
Check(board.Cells.Count == 137 && board.Cells.Values.Distinct().Count() == 137,"Recipe cells must be unique");
var reversed = GridLayout.Create(entries.AsEnumerable().Reverse());
Check(board.Cells.All(kv => reversed.Cells[kv.Key].Equals(kv.Value)),"Registry enumeration must not influence canonical positions");
foreach (var band in board.Bands)
{
    var food = band.Entries.Where(e => e.Category == "Food").ToList();
    if (food.Count > 0) Check(food.Min(e => board.Cells[e.Key].Y) == 0,"Food must start at band top");
}
foreach (int columns in new[] { 17,12,8,3 })
{
    var wrapped = GridLayout.Create(entries,columns);
    Check(wrapped.Cells.Count == 137,"Wrapping must retain all entries");
    foreach (var band in wrapped.Bands) Check(band.Entries.Select(e => wrapped.Cells[e.Key].Strip).Distinct().Count() == 1,"A band must not split across strips");
    var pocket = new IngredientSpace(wrapped,9);
    var large = pocket.Place(9); var small = pocket.Place(3);
    Check(large.Take(3).SequenceEqual(small),"Smaller ingredient groups must share their starting cells");
    foreach (int count in new[] { 1,3,9,20,50 })
    {
        var cells = pocket.Place(count);
        Check(cells.Count == count && cells.Distinct().Count() == count,"Ingredient capacity/uniqueness");
        Check(cells.All(c => !wrapped.Strips[c.Strip].Occupied.Contains(c)),"Ingredient group must not cover recipes");
        var reached = new HashSet<Cell> { cells[0] };
        bool changed;
        do { changed = false; foreach (var cell in cells) if (!reached.Contains(cell) && reached.Any(a => a.Strip == cell.Strip && Math.Abs(a.X-cell.X)+Math.Abs(a.Y-cell.Y) == 1)) changed |= reached.Add(cell); } while (changed);
        Check(reached.Count == count,"Ingredient group must be edge-connected");
    }
}
var modified = entries.Concat(new[] { new LayoutEntry { Key=200,Level=30,Category="Modded",SortName="New output",StableId="mod:variant" } }).ToList();
var modded = GridLayout.Create(modified);
Check(modded.Cells.ContainsKey(200) && modded.Cells.Count == 138,"New levels and categories must appear automatically");
var manyCategories = Enumerable.Range(0,14).Select(i => new LayoutEntry { Key=i,Level=5,Category="Category"+i,SortName="Test",StableId=i.ToString() });
Check(GridLayout.Create(manyCategories).Strips[0].Height == 14,"Height target must expand for more than ten categories");
Console.WriteLine($"Passed {checks} layout/pocket checks; fixture 137 recipes, 135 outputs, 17×10, canonical bands, wrapping, overflow, and modded categories.");
