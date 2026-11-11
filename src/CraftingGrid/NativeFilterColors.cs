using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CasualtiesUnknown.CraftingGrid
{
    internal static class NativeFilterColors
    {
        public static Color Read(Image background)
        {
            Color best = Color.white;
            float score = float.NegativeInfinity;
            var button = background.GetComponentInParent<Button>();
            var root = button ? button.transform : background.transform;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true).Where(g => g != background))
            {
                if (graphic is Image image && image.sprite)
                    Consider(Sample(image.sprite.texture,image.sprite.textureRect,image.color));
                else if (graphic is TMP_Text text)
                {
                    text.ForceMeshUpdate();
                    for (int i = 0; i < text.textInfo.characterCount; i++)
                    {
                        var c = text.textInfo.characterInfo[i];
                        if (c.elementType == TMP_TextElementType.Sprite && c.spriteAsset)
                        {
                            var glyph = c.spriteAsset.spriteCharacterTable[c.spriteIndex].glyph.glyphRect;
                            Consider(Sample(c.spriteAsset.spriteSheet,new Rect(glyph.x,glyph.y,glyph.width,glyph.height),c.color));
                        }
                        else Consider(c.color);
                    }
                }
                else Consider(graphic.color);
            }
            if (float.IsNegativeInfinity(score)) throw new InvalidOperationException("No native filter icon graphic found on "+root.name);
            return best;

            void Consider(Color color)
            {
                if (color.a < .05f) return;
                Color.RGBToHSV(color,out _,out float saturation,out float value);
                // Prefer colored icon pixels over white frames/text; neutral materials icons remain neutral.
                float candidate = saturation+.1f*(1-Mathf.Abs(value-.6f));
                if (candidate > score) { best = color; best.a = 1; score = candidate; }
            }
        }

        static Color Sample(Texture source, Rect region, Color tint)
        {
            var previous = RenderTexture.active;
            var render = RenderTexture.GetTemporary(source.width,source.height,0);
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source,render); RenderTexture.active = render;
                copy = new Texture2D((int)region.width,(int)region.height,TextureFormat.RGBA32,false);
                copy.ReadPixels(region,0,0);
                Color all = Color.clear, colored = Color.clear;
                float allWeight = 0, coloredWeight = 0;
                foreach (Color32 pixel in copy.GetPixels32())
                {
                    Color color = (Color)pixel*tint;
                    if (color.a < .05f) continue;
                    float weight = color.a;
                    all += color*weight; allWeight += weight;
                    Color.RGBToHSV(color,out _,out float saturation,out _);
                    if (saturation > .08f) { colored += color*weight; coloredWeight += weight; }
                }
                Color result = coloredWeight > 0 ? colored/coloredWeight : allWeight > 0 ? all/allWeight : tint;
                result.a = 1; return result;
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(render);
                if (copy) UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
