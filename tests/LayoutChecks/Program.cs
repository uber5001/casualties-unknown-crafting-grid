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
var vertical = GridLayout.CreateVertical(entries);
Check(vertical.Columns == 10 && vertical.Strips.Single().Height == 17,"Inline grid must transpose to 10×17");
Check(vertical.Cells.All(kv => kv.Value.X == board.Cells[kv.Key].Y && kv.Value.Y == board.Cells[kv.Key].X),"Transposition must preserve canonical recipe neighborhoods");
Check(vertical.Cells.Values.Distinct().Count() == 137,"Transposed recipes must have unique reserved slots");
var verticalPocket = new IngredientSpace(vertical,9);
foreach (int count in new[] { 2,9,30 })
{
    var cells = verticalPocket.Place(count);
    Check(cells.Count == count && cells.All(c => !vertical.Strips[c.Strip].Occupied.Contains(c)),"Inline ingredient groups must not overlap reserved recipes");
}
Check(GridLayout.CreateVertical(modified).Cells.Count == 138,"Transposed layout must include added recipes");
Check(!GridRules.MatchesFilter(true,"Rope","Materials","Food",""),"Category filter must hide non-category recipes");
Check(GridRules.Visible(true,false,true),"Known ingredient producers must override a category filter");
Check(!GridRules.Visible(false,false,true),"Ingredient highlighting must not reveal unknown recipes");
Check(GridRules.MatchesFilter(true,"Rope","Materials","Food","ro"),"Native search must override category and ignore case");
Check(!GridRules.MatchesFilter(false,"Rope","Materials",null,"Rope"),"Search must not reveal an unknown identity");
Check(GridRules.MatchesFilter(false,"","Materials",null,""),"Anonymous unknown slots remain in the all-category view");
var delay = new HoverDelay<object>(.1f); var first = new object(); var second = new object();
delay.Enter(first,0);
Check(delay.TakeReady(.099f) == null,"Native details must not change before 100 ms");
Check(ReferenceEquals(delay.TakeReady(.1f),first),"Native details should update after the hover delay");
Check(delay.TakeReady(1) == null,"A stationary hover must not repeatedly refresh native details");
delay.Enter(first,1); delay.Leave(first);
Check(delay.TakeReady(2) == null,"Leaving a recipe must cancel pending native details");
delay.Enter(first,3); delay.Enter(second,3.05f); delay.Leave(first);
Check(delay.TakeReady(3.14f) == null,"A new recipe needs its own full delay");
Check(ReferenceEquals(delay.TakeReady(3.16f),second),"A previous exit must not cancel the new recipe's delay");
delay.Enter(first,4); delay.Clear();
Check(delay.TakeReady(5) == null,"Closing or changing modes must cancel hover work");
Console.WriteLine($"Passed {checks} checks: reference/modded geometry, transposed 10×17 layout, pockets, filter exceptions, and cancellable 100 ms hover.");
