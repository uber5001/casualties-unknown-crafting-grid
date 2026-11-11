using System.IO;
using Newtonsoft.Json;

namespace CasualtiesUnknown.CraftingGrid
{
    internal static class LayoutResource
    {
        public static readonly RecipeLayoutSpec Spec = Read();
        static RecipeLayoutSpec Read()
        {
            using (var stream = typeof(LayoutResource).Assembly.GetManifestResourceStream("CasualtiesUnknown.CraftingGrid.recipe-layout.json"))
            using (var reader = new StreamReader(stream ?? throw new FileNotFoundException("Embedded recipe-layout.json is missing.")))
                return JsonConvert.DeserializeObject<RecipeLayoutSpec>(reader.ReadToEnd());
        }
    }
}
