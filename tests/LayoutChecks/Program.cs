using System.Text.Json;
using System.Text.RegularExpressions;
using CasualtiesUnknown.CraftingGrid;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
var spec = JsonSerializer.Deserialize<RecipeLayoutSpec>(File.ReadAllText("recipe-layout.json"),new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
var html = File.ReadAllText("handoff/crafting-reference.html");
var data = JsonDocument.Parse(Regex.Match(html,@"const recipes = (\[.*?\]);").Groups[1].Value);
var entries = data.RootElement.EnumerateArray().Select((r,i) => new LayoutEntry {
    Key = i, Category = r.GetProperty("category").GetString(), OutputName = r.GetProperty("name").GetString(),
    StableId = r.GetProperty("id").GetString(), Repair = r.GetProperty("repair").GetBoolean(),
    Ingredients = r.GetProperty("ingredients").EnumerateArray().Where(s => !s.GetProperty("quality").GetBoolean()).Select(s => s.GetProperty("name").GetString()).ToArray()
}).ToList();
// Optional locally inspected catalog, so canonical names/flags can be checked against this installed game without shipping its data.
if (args.Length > 0) entries = JsonSerializer.Deserialize<List<LayoutEntry>>(File.ReadAllText(args[0]),new JsonSerializerOptions { IncludeFields = true });
int Key(string referenceId) => entries.Single(e => e.StableId == referenceId).Key;
Cell At(BoardLayout b, string id) => b.Cells[Key(id)];
void Position(BoardLayout b, string id, int x, int y) => Check(At(b,id).Equals(new Cell(x,y)), $"{id} expected at {x},{y}, got {At(b,id).X},{At(b,id).Y}");
var board = GridLayout.Create(entries,spec);
Check(entries.Count == 137,"Expected full base catalog fixture");
Check(board.Columns == 12 && board.Rows == 12,"Authored layout should be 12 columns by 12 rows");
Check(board.MappedCount == 137,"Every reference recipe must resolve, including repair and ingredient variants");
Check(board.Cells.Count == entries.Count && board.Cells.Values.Distinct().Count() == entries.Count,"Live recipes must occur exactly once without overlapping");
Position(board,"clean-water-1",0,0); Position(board,"bio-chem-fluid-1",6,0); Position(board,"bio-chem-fluid-2",7,0);
Position(board,"soap-1",8,0); Position(board,"lrd-serum-1",10,1);
Position(board,"ripped-dressing-1",0,2); Position(board,"sterilized-dressing-1",1,2); Position(board,"dressing-1",2,2); Position(board,"plastic-dressing-1",4,2);
Position(board,"tourniquet-1",0,3); Position(board,"syringe-1",5,2); Position(board,"peg-limb-1",8,3);
Position(board,"rye-flour-1",0,4); Position(board,"stick-1",0,6);
Position(board,"makeshift-helmet-1",0,10); Position(board,"foliage-bag-1",4,4);
Position(board,"flimsy-knife-1",0,8); Position(board,"campfire-1",9,6);
Position(board,"torch-1",8,4); Position(board,"torch-2",9,4); Position(board,"processed-copper-1",7,8); Position(board,"circuit-board-1",9,8);
Position(board,"scrap-cube-1",5,6); Position(board,"casing-1",6,10); Position(board,"dynamite-1",9,11);
var reversed = GridLayout.Create(entries.AsEnumerable().Reverse(),spec);
Check(board.Cells.All(kv => reversed.Cells[kv.Key].Equals(kv.Value)),"Registry enumeration must not influence positions");
foreach (int columns in Enumerable.Range(1,12))
{
    var wrapped = GridLayout.Create(entries,spec,columns);
    Check(wrapped.Columns == columns && wrapped.Cells.Count == entries.Count,"Narrow layouts retain every recipe");
    Check(wrapped.Cells.Values.Distinct().Count() == entries.Count,"Narrow layouts cannot overlap recipes");
    Check(wrapped.Cells.Values.All(c => c.X >= 0 && c.X < columns && c.Y >= 0 && c.Y < wrapped.Rows),"Recipes stay inside grid bounds");
    int previousGroupStart = -1;
    foreach (var group in spec.Bands.SelectMany(b => b))
    {
        var cells = group.Recipes.Select(r => At(wrapped,r.ReferenceId)).ToList();
        var order = cells.Select(c => c.Y*columns+c.X).ToList();
        Check(order.SequenceEqual(order.OrderBy(n => n)),"Recipe order must remain left-to-right then downward within each group");
        Check(order[0] > previousGroupStart,"Group starts must retain authored order"); previousGroupStart = order[0];
    }
}
var eight = GridLayout.Create(entries,spec,8);
Position(eight,"ripped-dressing-1",0,3); Position(eight,"tourniquet-1",0,5); Position(eight,"syringe-1",0,4); Position(eight,"peg-limb-1",0,6);
var six = GridLayout.Create(entries,spec,6);
Position(six,"bio-chem-fluid-1",0,1); Position(six,"ripped-dressing-1",0,4); Position(six,"tourniquet-1",0,7); Position(six,"syringe-1",0,5); Position(six,"peg-limb-1",0,9);
Check(GridLayout.Create(entries,spec,1).Rows == 137,"One-column layout preserves the complete authored sequence");
var extra = new LayoutEntry { Key=200,Category="Modded",OutputName="New output",StableId="mod:variant" };
var variant = new LayoutEntry { Key=201,OutputName="Bio-chem fluid",StableId="mod:new-biochem",Ingredients=new[] { "New ingredient" } };
var modded = GridLayout.Create(entries.Concat(new[] { extra,variant }),spec);
Check(modded.MappedCount == 137 && modded.Cells.Count == 139,"New outputs and unmatched variants must both be retained at the end");
Check(board.Cells.All(kv => modded.Cells[kv.Key].Equals(kv.Value)),"Appended recipes must leave authored positions intact");
Check(modded.Cells[200].Y == 12 && modded.Cells[201].Y == 12,"Unmapped recipes must start after every authored band");
var missing = GridLayout.Create(entries.Where(e => e.Key != Key("merc-scarf-1")),spec);
Check(missing.Cells.Count == 136 && missing.MappedCount == 136 && board.Cells.Where(kv => kv.Key != Key("merc-scarf-1")).All(kv => missing.Cells[kv.Key].Equals(kv.Value)),"Absent/secret recipes reserve their authored space without shifting other recipes");
var shuffledFacts = entries.Select(e => new LayoutEntry { Key=e.Key,OutputName=e.OutputName.ToUpperInvariant(),Repair=e.Repair,StableId=e.StableId,Ingredients=e.Ingredients }).ToList();
Check(GridLayout.Create(shuffledFacts,spec).MappedCount == 137,"Canonical matching should ignore case");
Check(GridRules.HighlightCategory(true,"Food","Food"),"Selected category must receive its background");
Check(!GridRules.HighlightCategory(true,"Materials","Food"),"Other categories must retain plain backgrounds");
Check(!GridRules.HighlightCategory(false,"Food","Food"),"Category tint must not reveal unknown recipes");
Check(!GridRules.HighlightCategory(true,"Food",null),"All-categories button must clear category backgrounds");
Check(GridRules.MatchesSearch(true,"Rope","RO"),"Search remains case-insensitive");
Check(!GridRules.MatchesSearch(false,"Rope","Rope"),"Search must not reveal unknown identity");
Check(GridRules.MatchesSearch(false,"",""),"Unknown slots remain visible without a search query");
foreach (float viewport in new[] { 100f,200f,395f,396f,473f,474f,500f,600f,900f })
{
    int columns = GridRules.ColumnCount(viewport,spec.ReferenceColumns);
    float side = GridRules.CellSide(viewport,columns);
    Check(columns >= 1 && columns <= spec.ReferenceColumns && side >= 37,"Column wrapping keeps cells readable");
    Check(Math.Abs(8+columns*side+(columns-1)*2-viewport) < .001f,"Grid must fill its usable width without overflowing or a height cap");
}
Check(GridRules.ColumnCount(473,12) == 11 && GridRules.ColumnCount(474,12) == 12,"Columns wrap at the minimum readable cell width");
Check(GridRules.CellSide(900,12) > 56,"A wide viewport must not retain the old 56-unit cap");
Check(GridRules.ScrollbarInsets(600,580,600) == (0f,22f),"Overlapping right scrollbar needs its width plus clearance");
Check(GridRules.ScrollbarInsets(580,580,600) == (0f,0f),"Already-excluded scrollbar must not consume width twice");
Check(GridRules.ScrollbarInsets(600,-5,15) == (17f,0f),"Left scrollbar needs left-side clearance");
Check(GridRules.ScrollbarInsets(600,590,610) == (0f,12f),"Partially overlapping scrollbar reserves only the overlap and gap");
Check(GridRules.ScrollbarInsets(600,-30,-10) == (0f,0f),"Non-overlapping scrollbar leaves usable space unchanged");
Console.WriteLine($"Passed {checks} checks: authored placement, variants, narrow wrapping, unmapped/missing recipes, search, tint, sizing, and scrollbar clearance.");
