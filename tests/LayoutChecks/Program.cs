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
Check(GridLayout.CreateVertical(modified).Cells.Count == 138,"Transposed layout must include added recipes");
Check(GridRules.HighlightCategory(true,"Food","Food"),"Selected category must receive its background");
Check(!GridRules.HighlightCategory(true,"Materials","Food"),"Other categories must retain plain backgrounds");
Check(!GridRules.HighlightCategory(false,"Food","Food"),"Category tint must not reveal unknown recipes");
Check(!GridRules.HighlightCategory(true,"Food",null),"All-categories button must clear category backgrounds");
Check(GridRules.MatchesSearch(true,"Rope","RO"),"Search remains case-insensitive");
Check(!GridRules.MatchesSearch(false,"Rope","Rope"),"Search must not reveal unknown identity");
Check(GridRules.MatchesSearch(false,"",""),"Unknown slots remain visible without a search query");
foreach (float viewport in new[] { 500f,600f,900f })
{
    float side = GridRules.CellSide(viewport,10);
    Check(Math.Abs(8+10*side+9*2-viewport) < .001f,"Grid must fill the available width without a height or maximum-size cap");
}
Check(GridRules.CellSide(900,10) > 56,"A wide viewport must not retain the old 56-unit cap");
Check(GridRules.CellSide(200,10) == 37,"Narrow/modded catalogs retain a readable minimum with overflow");
Console.WriteLine($"Passed {checks} checks: dynamic/transposed geometry, reserved slots, category tint, search privacy, and full-width sizing.");
