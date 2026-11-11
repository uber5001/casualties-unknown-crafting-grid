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
    internal sealed class NativeCatalog : IDisposable
    {
        public readonly List<NativeEntry> Entries = new List<NativeEntry>();
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
                var entry = new NativeEntry { Recipe = r, Layout = new LayoutEntry {
                    Key = i, Category = r.category.ToString(), OutputName = CanonicalName(english,r.result.id,r.result.isLiquid), Repair = r.isRepair,
                    Ingredients = r.items.Where(s => s.specific).Select(s => CanonicalName(english,s.specificId,s.isLiquid)).ToArray(),
                    StableId = Identity(r)
                }};
                try { (entry.Sprite, entry.Color) = r.resultSprite; }
                catch (Exception ex) { Plugin.Log.LogWarning($"Missing native art for {r.result.id}: {ex.Message}"); entry.Color = Color.white; }
                if (entry.Sprite) entry.Gray = Grayscale(entry.Sprite);
                Entries.Add(entry);

            }

        }
        static string CanonicalName(Language english, string id, bool liquid)
        {
            if (liquid && Liquids.Registry.TryGetValue(id,out var type))
            {
                id = type.localeName ?? id;
                liquid = !type.localeFromItem;
            }
            var names = liquid ? english?.other : english?.main;
            return names != null && names.TryGetValue(id,out var name) ? name : id;
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
        public static bool Known(Recipe r) => r.visible;
        public static bool Ready(Recipe r) => Known(r) && r.GetItemsForRecipe() != null;
        public static string RequirementOutputId(RecipeItem slot, Item allocated)
        {
            if (slot.specific) return slot.specificId;
            if (!allocated) return null;
            if (!slot.isLiquid) return allocated.id;
            if (allocated.TryGetComponent<WaterContainerItem>(out var water))
                foreach (var stack in water.stack)
                    if (Liquids.Registry.TryGetValue(stack.liquidId,out var liquid) && Item.GetQualityThatMeetsCriteria(slot.quality,liquid.GetScaledQualities(stack.amount)) != null) return stack.liquidId;
            return null;
        }
        public static bool IsRequirementOutput(Recipe recipe, RecipeItem slot, string allocatedId, bool available)
        {
            var output = recipe.result;
            if (output.isLiquid != slot.isLiquid || !slot.isLiquid && output.id == slot.ignoredId) return false;
            if (allocatedId != null) return output.id == allocatedId;
            if (available) return false;
            // A missing alternative slot marks eligible producer choices; a fulfilled slot marks its actual provider only.
            if (slot.isLiquid) return Liquids.Registry.TryGetValue(output.id,out var liquid) && Item.HasCommonQuality(slot.quality,liquid.qualities) != null;
            return Item.GlobalItems.TryGetValue(output.id,out var item) && Item.GetQualityThatMeetsCriteria(slot.quality,item.qualities) != null;
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
