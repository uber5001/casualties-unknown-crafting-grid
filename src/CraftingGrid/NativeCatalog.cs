using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace CasualtiesUnknown.CraftingGrid
{
    internal sealed class NativeEntry
    {
        public Recipe Recipe;
        public LayoutEntry Layout;
        public Sprite Sprite, Gray;
        public Color Color;
    }
    internal sealed class Requirement
    {
        public string Name, Qualifier, OutputId, Badge, Category;
        public bool Liquid, Short;
        public Sprite Sprite;
        public Color Color = Color.white;
    }
    internal sealed class NativeCatalog : IDisposable
    {
        public readonly List<NativeEntry> Entries = new List<NativeEntry>();
        public readonly Dictionary<Recipe, List<Recipe>> Related = new Dictionary<Recipe, List<Recipe>>();
        readonly Dictionary<string, List<NativeEntry>> producers = new Dictionary<string, List<NativeEntry>>(StringComparer.Ordinal);
        readonly Dictionary<Texture, Texture2D> grayTextures = new Dictionary<Texture, Texture2D>();
        readonly List<Sprite> graySprites = new List<Sprite>();
        readonly List<Recipe> concealed = new List<Recipe>();
        readonly List<Recipe> registry;
        readonly int registryCount;
        readonly int structuralStamp;

        public NativeCatalog()
        {
            registry = Recipes.recipes; registryCount = registry.Count; structuralStamp = RegistryStamp(registry);
            Language english = null;
            string path = Path.Combine(Application.dataPath, "Lang", "EN.json");
            if (File.Exists(path)) english = JsonConvert.DeserializeObject<Language>(File.ReadAllText(path));
            for (int i = 0; i < registry.Count; i++)
            {
                var r = registry[i];
                if (r?.result == null || r.items == null) continue;
                // Respect native genuinely-secret item metadata; normal level-locked entries reserve a square.
                if (!r.result.isLiquid && Item.GlobalItems.TryGetValue(r.result.id, out var info) && info.secret && !r.specialKnown && !r.hasMadeBefore) { concealed.Add(r); continue; }
                string name = r.result.id;
                var names = r.result.isLiquid ? english?.other : english?.main;
                if (names != null && names.TryGetValue(r.result.id, out var canonical)) name = canonical;
                var entry = new NativeEntry { Recipe = r, Layout = new LayoutEntry {
                    Key = i, Level = r.INT, Category = r.category.ToString(), SortName = name + (r.isRepair ? " (repair)" : ""),
                    StableId = Identity(r)
                }};
                try { (entry.Sprite, entry.Color) = r.resultSprite; }
                catch (Exception ex) { Plugin.Log.LogWarning($"Missing native art for {r.result.id}: {ex.Message}"); entry.Color = Color.white; }
                if (entry.Sprite) entry.Gray = Grayscale(entry.Sprite);
                Entries.Add(entry);
                string key = OutputKey(r.result.id, r.result.isLiquid);
                if (!producers.TryGetValue(key, out var list)) producers.Add(key, list = new List<NativeEntry>());
                list.Add(entry);
            }
            // Structural producer matching is cached and uses native quality definitions, never translated names.
            foreach (var entry in Entries)
                Related[entry.Recipe] = Entries.Where(p => p.Recipe != entry.Recipe && !p.Recipe.isRepair && entry.Recipe.items.Any(slot => ProducesFor(p.Recipe, slot))).Select(p => p.Recipe).ToList();
        }
        public bool IsCurrent => ReferenceEquals(registry, Recipes.recipes) && registryCount == Recipes.recipes.Count && structuralStamp == RegistryStamp(Recipes.recipes) && !concealed.Any(r => r.specialKnown || r.hasMadeBefore);
        static int RegistryStamp(List<Recipe> recipes)
        {
            unchecked
            {
                int hash = recipes.Count;
                foreach (var r in recipes)
                {
                    if (r == null) continue;
                    hash = hash*31+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r);
                    hash = hash*31+r.INT; hash = hash*31+(int)r.category; hash = hash*31+(r.isRepair ? 1 : 0);
                    if (r.result != null)
                    {
                        hash = hash*31+(r.result.id?.GetHashCode() ?? 0); hash = hash*31+r.result.amount;
                        hash = hash*31+r.result.resultCondition.GetHashCode(); hash = hash*31+(r.result.isLiquid ? 1 : 0);
                    }
                    if (r.items == null) continue;
                    foreach (var slot in r.items)
                    {
                        hash = hash*31+(slot.specificId?.GetHashCode() ?? 0); hash = hash*31+(slot.quality?.id?.GetHashCode() ?? 0);
                        hash = hash*31+(slot.quality?.amount.GetHashCode() ?? 0); hash = hash*31+slot.minimumCondition.GetHashCode();
                        hash = hash*31+(slot.destroyItem ? 1 : 0)+(slot.isLiquid ? 2 : 0)+(slot.specific ? 4 : 0)+(slot.noMaxCondition ? 8 : 0);
                        hash = hash*31+(slot.ignoredId?.GetHashCode() ?? 0);
                    }
                }
                return hash;
            }
        }
        static string OutputKey(string id, bool liquid) => (liquid ? "liquid:" : "item:") + id;
        static string Identity(Recipe r) => OutputKey(r.result.id, r.result.isLiquid) + ":" + r.isRepair + ":" + r.result.resultCondition.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
            string.Join(";", r.items.Select(s => (s.specific ? s.specificId : "quality:"+s.quality?.id+":"+s.quality?.amount) + ":"+s.isLiquid+":"+s.minimumCondition+":"+s.destroyItem+":"+s.noMaxCondition+":"+s.ignoredId)) + ":" + r.result.amount;
        static bool ProducesFor(Recipe r, RecipeItem slot)
        {
            if (r.result.isLiquid != slot.isLiquid || r.result.id == slot.ignoredId && !slot.isLiquid) return false;
            if (slot.specific && slot.specificId != r.result.id) return false;
            if (slot.isLiquid)
            {
                if (slot.specific) return r.result.resultCondition >= slot.minimumCondition;
                return Liquids.Registry.TryGetValue(r.result.id, out var l) && Item.GetQualityThatMeetsCriteria(slot.quality, l.GetScaledQualities(r.result.resultCondition)) != null;
            }
            if (r.result.resultCondition < slot.minimumCondition || slot.noMaxCondition && r.result.resultCondition >= .991f) return false;
            return slot.specific || Item.GlobalItems.TryGetValue(r.result.id, out var info) && Item.GetQualityThatMeetsCriteria(slot.quality, info.qualities) != null;
        }
        public string CategoryFor(string id, bool liquid)
        {
            if (producers.TryGetValue(OutputKey(id, liquid), out var list)) return list[0].Layout.Category;
            if (!liquid && Item.GlobalItems.TryGetValue(id, out var info))
            {
                string category = info.category?.ToLowerInvariant() ?? "";
                if (category.Contains("food")) return "Food";
                if (category.Contains("medic")) return "Medicine";
                if (category.Contains("tool")) return "Tools";
            }
            return "Materials";
        }
        public static bool Known(Recipe r) => r.visible;
        public static bool Ready(Recipe r) => Known(r) && r.GetItemsForRecipe() != null;
        public static string Name(Recipe r) => (r.isRepair ? Locale.GetOther("craftrepair") : "") + (r.result.isLiquid ? Locale.GetOther(r.result.id) : Locale.GetItem(r.result.id));
        public static string CategoryName(Recipe r)
        {
            // Look up the same native category keys used by the native crafting controls.
            string key = "craftingcategory" + r.category.ToString().ToLowerInvariant();
            string localized = Locale.GetOther(key);
            return localized == key ? r.category.ToString() : localized;
        }
        public static List<Item> Scope(PlayerCamera pc)
        {
            var scope = pc.body.GetAllItemsThorough();
            foreach (var hit in Physics2D.OverlapCircleAll(pc.body.transform.position, 10f, LayerMask.GetMask("Item")))
                if (hit.TryGetComponent<Item>(out var item) && pc.body.DoPickupCheck(item, noAlerts: true)) scope.Add(item);
            return scope;
        }
        public static int Fingerprint(PlayerCamera pc)
        {
            unchecked
            {
                int hash = pc.body.skills.INT;
                foreach (var item in Scope(pc))
                {
                    if (!item) continue;
                    hash = hash*31+item.GetInstanceID(); hash = hash*31+item.condition.GetHashCode(); hash = hash*31+(item.favourited ? 1 : 0);
                    if (item.TryGetComponent<Container>(out var container)) hash = hash*31+container.itemCount;
                    if (item.TryGetComponent<WaterContainerItem>(out var water)) foreach (var stack in water.stack) { hash = hash*31+stack.liquidId.GetHashCode(); hash = hash*31+stack.amount.GetHashCode(); }
                }
                foreach (var r in Recipes.recipes) hash = hash*31+(r.visible ? 1 : 0)+(r.hasMadeBefore ? 2 : 0)+(r.specialKnown ? 4 : 0);
                return hash;
            }
        }
        static string F(float value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        public List<Requirement> Bill(Recipe recipe)
        {
            var allocation = recipe.GetItemsForRecipeThorough();
            var result = new List<Requirement>();
            var groups = new Dictionary<string, (Requirement requirement, int owned, int needed)>();
            var scope = Scope(PlayerCamera.main);
            var exclusions = new HashSet<Item>();
            for (int i = 0; i < recipe.items.Count; i++)
            {
                var slot = recipe.items[i]; var item = allocation[i];
                string id = slot.specific ? slot.specificId : item ? item.id : null;
                string name = slot.specific ? slot.isLiquid ? Locale.GetOther(id) : Locale.GetItem(id) : item && !slot.isLiquid ? Locale.GetItem(id) : slot.quality.LocaleName;
                float needed = 1, owned = item ? 1 : 0;
                string unit = "";
                if (slot.isLiquid)
                {
                    id = slot.specific ? slot.specificId : null;
                    needed = slot.specific ? slot.minimumCondition : slot.quality.amount;
                    // One native slot requires one qualifying vessel; never sum different containers.
                    owned = 0;
                    foreach (var candidate in scope.Where(c => c && !c.favourited && !exclusions.Contains(c)))
                    {
                        if (!candidate.TryGetComponent<WaterContainerItem>(out var water)) continue;
                        if (slot.specific) owned = Math.Max(owned, water.AmountOf(slot.specificId));
                        else foreach (var stack in water.stack)
                            if (Liquids.Registry.TryGetValue(stack.liquidId, out var liquid))
                                foreach (var quality in liquid.GetScaledQualities(stack.amount).Where(q => q.id == slot.quality.id)) owned = Math.Max(owned, quality.amount);
                    }
                    unit = slot.specific ? " mL" : " q";
                    if (item && !slot.specific && item.TryGetComponent<WaterContainerItem>(out var provider))
                    {
                        foreach (var stack in provider.stack)
                        {
                            if (!Liquids.Registry.TryGetValue(stack.liquidId, out var liquid)) continue;
                            var quality = Item.GetQualityThatMeetsCriteria(slot.quality, liquid.GetScaledQualities(stack.amount));
                            if (quality == null) continue;
                            id = stack.liquidId; name = Locale.GetOther(id); needed = stack.amount*slot.quality.amount/quality.amount; owned = needed; unit = " mL"; break;
                        }
                    }
                    if (item) owned = needed;
                }
                var req = new Requirement { Name = name, OutputId = id, Liquid = slot.isLiquid, Short = !item, Category = CategoryFor(id ?? "", slot.isLiquid) };
                var qualifiers = new List<string>();
                if (!slot.destroyItem && !slot.isLiquid) qualifiers.Add("retained tool");
                if (!slot.isLiquid && slot.minimumCondition > 0) qualifiers.Add("≥"+F(slot.minimumCondition*100)+"% condition");
                if (slot.noMaxCondition) qualifiers.Add("<99.1% condition");
                if (!slot.specific) qualifiers.Add(slot.quality.LocaleName+" "+F(slot.quality.amount));
                if (slot.isLiquid) qualifiers.Add("one vessel");
                req.Qualifier = string.Join(" · ", qualifiers);
                if (slot.isLiquid)
                {
                    req.Sprite = Resources.Load<Sprite>("Sprites/droplet");
                    if (id != null && Liquids.Registry.TryGetValue(id, out var liquid)) req.Color = liquid.color;
                }
                else
                {
                    if (id == null) id = Recipes.QualityExamples.FirstOrDefault(p => p.Key.id == slot.quality.id && p.Key.amount == slot.quality.amount).Value;
                    if (id != null)
                    {
                        var prefab = Resources.Load<GameObject>(id);
                        if (prefab && prefab.TryGetComponent<SpriteRenderer>(out var renderer)) { req.Sprite = renderer.sprite; req.Color = renderer.color; }
                    }
                    if (item && item.TryGetComponent<SpriteRenderer>(out var actual)) { req.Sprite = actual.sprite; req.Color = actual.color; }
                }
                req.Badge = (req.Short ? F(Math.Min(owned, needed))+"/" : "")+F(needed)+unit;
                // Fold equivalent item slots; liquids stay discrete so vessel requirements remain visible.
                string groupKey = (slot.specific ? "specific:"+slot.specificId : "quality:"+slot.quality.id+":"+F(slot.quality.amount)) + ":"+id+":"+req.Qualifier;
                if (!slot.isLiquid && groups.TryGetValue(groupKey, out var group))
                {
                    group.owned += item ? 1 : 0; group.needed++; group.requirement.Short |= req.Short;
                    group.requirement.Badge = group.requirement.Short ? group.owned+"/"+group.needed : group.needed.ToString();
                    groups[groupKey] = group;
                }
                else { result.Add(req); if (!slot.isLiquid) groups.Add(groupKey, (req,item ? 1 : 0,1)); }
                if (item && slot.destroyItem && !slot.isLiquid) exclusions.Add(item);
            }
            return result;
        }
        Sprite Grayscale(Sprite source)
        {
            try
            {
                if (!grayTextures.TryGetValue(source.texture, out var texture))
                {
                    var render = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0);
                    var previous = RenderTexture.active;
                    try
                    {
                        Graphics.Blit(source.texture, render); RenderTexture.active = render;
                        texture = new Texture2D(render.width, render.height, TextureFormat.RGBA32, false);
                        texture.ReadPixels(new Rect(0,0,render.width,render.height),0,0);
                        var colors = texture.GetPixels32();
                        for (int i = 0; i < colors.Length; i++) { var c = colors[i]; byte gray = (byte)((c.r*299+c.g*587+c.b*114)/1000); colors[i] = new Color32(gray,gray,gray,c.a); }
                        texture.SetPixels32(colors); texture.filterMode = FilterMode.Point; texture.Apply();
                        grayTextures.Add(source.texture, texture);
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(render); }
                }
                // Native textureRect samples only the sprite's atlas region, rather than the entire texture.
                var sprite = Sprite.Create(texture, source.textureRect, new Vector2(.5f,.5f), source.pixelsPerUnit);
                graySprites.Add(sprite); return sprite;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Could not prepare grayscale icon: "+ex.Message); return source; }
        }
        public void Dispose()
        {
            foreach (var sprite in graySprites) UnityEngine.Object.Destroy(sprite);
            foreach (var texture in grayTextures.Values) UnityEngine.Object.Destroy(texture);
        }
    }
}
