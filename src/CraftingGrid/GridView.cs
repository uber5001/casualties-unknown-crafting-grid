using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CasualtiesUnknown.CraftingGrid
{
    internal sealed class TileEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Action Enter, Exit;
        public void OnPointerEnter(PointerEventData _) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData _) => Exit?.Invoke();
        public void OnSelect(BaseEventData _) => Enter?.Invoke();
        public void OnDeselect(BaseEventData _) => Exit?.Invoke();
    }

    internal sealed class GridView : MonoBehaviour, IPointerExitHandler
    {
        sealed class Tile
        {
            public NativeEntry Entry;
            public RectTransform Rect;
            public Image Background, Icon;
            public TextMeshProUGUI Unknown, Level, Yield;
            public bool Known, Ready;
        }
        public PlayerCamera Camera { get; private set; }
        readonly List<(CanvasGroup group, float alpha, bool interactable, bool raycasts, bool added)> hidden = new List<(CanvasGroup,float,bool,bool,bool)>();
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<GameObject> ingredients = new List<GameObject>();
        NativeCatalog catalog;
        BoardLayout layout;
        IngredientSpace space;
        RectTransform root, viewport, content;
        ScrollRect scroll;
        TextMeshProUGUI tooltip, tooltipShadow;
        TMP_FontAsset font;
        Material fontMaterial;
        TMP_SpriteAsset fontSprites;
        Tile inspected;
        RectTransform tipAnchor;
        float cellSide, width, nextPoll;
        int fingerprint, lastCraftFrame = -1;
        readonly Color ink = new Color32(238,239,227,255);
        static readonly Color Surface = new Color32(35,38,35,255);
        static readonly Color Shortage = new Color32(255,153,149,255);

        public void Initialize(PlayerCamera camera)
        {
            Camera = camera;
            root = (RectTransform)transform;
            Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var template = camera.craftingPanel.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.font);
            if (!template) throw new InvalidOperationException("No native crafting font found on PlayerCamera.craftingPanel.");
            font = template.font; fontMaterial = template.fontSharedMaterial; fontSprites = template.spriteAsset;
            camera.recipeFilterField.DeactivateInputField();
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(camera.craftingPanel.transform)) EventSystem.current.SetSelectedGameObject(null);
            // Leave stock controls alive for native scripts, but hide and remove them from focus and raycasts.
            foreach (Transform child in camera.craftingPanel.transform)
            {
                if (child == transform) continue;
                var group = child.GetComponent<CanvasGroup>(); bool added = !group;
                if (added) group = child.gameObject.AddComponent<CanvasGroup>();
                hidden.Add((group,group.alpha,group.interactable,group.blocksRaycasts,added));
                group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
            }
            gameObject.AddComponent<Image>().color = Surface;
            viewport = Rect("Viewport",root);
            Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(14,12), new Vector2(-14,-12));
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content",viewport); TopLeft(content);
            scroll = gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = true; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            tooltipShadow = Text("TooltipShadow",root,14); tooltipShadow.color = Color.black;
            tooltip = Text("Tooltip",root,14); tooltip.color = ink;
            foreach (var text in new[] { tooltipShadow,tooltip })
            {
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0,1);
                text.rectTransform.pivot = new Vector2(.5f,0);
                text.alignment = TextAlignmentOptions.Center;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.gameObject.SetActive(false);
            }
            Canvas.ForceUpdateCanvases();
            RebuildCatalog();
            Plugin.Log.LogInfo($"Crafting grid attached to native crafting panel: {catalog.Entries.Count} live recipes, {layout.Columns} columns, {layout.Strips.Count} strip(s). Game {Application.version}, Unity {Application.unityVersion}.");
        }
        static RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent,false); return (RectTransform)obj.transform;
        }
        static void TopLeft(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0,1); rect.pivot = new Vector2(0,1); rect.anchoredPosition = Vector2.zero;
        }
        static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        }
        TextMeshProUGUI Text(string name, Transform parent, float size)
        {
            var rect = Rect(name,parent); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSharedMaterial = fontMaterial; text.spriteAsset = fontSprites;
            text.fontSize = size; text.color = ink; text.raycastTarget = false;
            text.enableWordWrapping = false; text.richText = false; text.alignment = TextAlignmentOptions.Center;
            return text;
        }
        TextMeshProUGUI Badge(string name, RectTransform parent, bool top)
        {
            var text = Text(name,parent,Mathf.Clamp(cellSide*.25f,10,15));
            text.alignment = top ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.BottomRight;
            Stretch(text.rectTransform, new Vector2(0,top ? 1 : 0), new Vector2(1,top ? 1 : 0), new Vector2(2,top ? -19 : 1), new Vector2(-2,top ? -1 : 19));
            text.enableAutoSizing = true; text.fontSizeMin = 8; text.fontSizeMax = Mathf.Clamp(cellSide*.25f,10,15);
            var shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(1,-1);
            return text;
        }
        void RebuildCatalog()
        {
            catalog?.Dispose(); catalog = new NativeCatalog(); RebuildLayout();
        }
        void RebuildLayout()
        {
            ClearInspection();
            foreach (var tile in tiles) { tile.Rect.gameObject.SetActive(false); Destroy(tile.Rect.gameObject); }
            tiles.Clear();
            width = viewport.rect.width;
            int maxColumns = Math.Max(1,Mathf.FloorToInt((width+2)/39));
            layout = GridLayout.Create(catalog.Entries.Select(e => e.Layout),maxColumns);
            if (layout.Columns == 0) throw new InvalidOperationException("The native recipe registry contains no displayable recipes.");
            cellSide = Mathf.Max(37,Mathf.Min(56,(width-2*(layout.Columns-1))/layout.Columns));
            if (layout.Strips.Count == 1) cellSide = Mathf.Max(37,Mathf.Min(cellSide,(viewport.rect.height-28-2*(layout.Strips[0].Height-1))/Math.Max(1,layout.Strips[0].Height)));
            space = new IngredientSpace(layout,catalog.Entries.Max(e => e.Recipe.items.Count));
            foreach (var entry in catalog.Entries)
            {
                var rect = Rect("Recipe-"+entry.Layout.Key,content); TopLeft(rect); rect.sizeDelta = new Vector2(cellSide,cellSide);
                Position(rect,layout.Cells[entry.Layout.Key]);
                var tile = new Tile { Entry = entry,Rect = rect,Background = rect.gameObject.AddComponent<Image>() };
                tile.Background.color = Color.clear;
                var iconRect = Rect("Icon",rect); Stretch(iconRect,new Vector2(.03f,.03f),new Vector2(.97f,.97f),Vector2.zero,Vector2.zero);
                tile.Icon = iconRect.gameObject.AddComponent<Image>(); tile.Icon.preserveAspect = true; tile.Icon.raycastTarget = false;
                tile.Unknown = Text("Unknown",rect,20); Stretch(tile.Unknown.rectTransform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); tile.Unknown.text = "×"; tile.Unknown.color = new Color32(120,130,114,255);
                tile.Level = Badge("INT",rect,true); tile.Yield = Badge("Yield",rect,false);
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = tile.Background; button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => Guard(() => Craft(tile)));
                var events = rect.gameObject.AddComponent<TileEvents>();
                events.Enter = () => Guard(() => Inspect(tile)); events.Exit = () => HideTooltip(rect);
                tiles.Add(tile);
            }
            ResizeContent(0); scroll.verticalNormalizedPosition = 1; scroll.horizontalNormalizedPosition = 0;
        }
        float StripTop(int strip)
        {
            float y = 0; for (int i = 0; i < strip; i++) y += 28+layout.Strips[i].Height*(cellSide+2)+8;
            return y+28;
        }
        void Position(RectTransform rect, Cell cell) => rect.anchoredPosition = new Vector2(cell.X*(cellSide+2),-StripTop(cell.Strip)-cell.Y*(cellSide+2));
        void ResizeContent(int extraRows)
        {
            int last = layout.Strips.Count-1;
            content.sizeDelta = new Vector2(layout.Columns*(cellSide+2)-2,StripTop(last)+(layout.Strips[last].Height+extraRows)*(cellSide+2));
        }
        public void RefreshState()
        {
            if (!catalog.IsCurrent) RebuildCatalog();
            foreach (var tile in tiles) { tile.Known = NativeCatalog.Known(tile.Entry.Recipe); tile.Ready = tile.Known && tile.Entry.Recipe.GetItemsForRecipe() != null; }
            fingerprint = NativeCatalog.Fingerprint(Camera);
            if (inspected != null) PaintInspection(); else PaintTiles();
            nextPoll = Time.unscaledTime+.4f;
        }
        void PaintTiles()
        {
            var related = inspected != null && inspected.Known ? catalog.Related[inspected.Entry.Recipe] : null;
            foreach (var tile in tiles)
            {
                bool inspecting = inspected == tile && tile.Known;
                bool highlight = inspecting || tile.Known && related != null && related.Contains(tile.Entry.Recipe);
                tile.Background.color = highlight ? Tint(tile.Entry.Layout.Category) : Color.clear;
                tile.Unknown.gameObject.SetActive(!tile.Known); tile.Icon.gameObject.SetActive(tile.Known && tile.Entry.Sprite);
                tile.Icon.sprite = tile.Ready ? tile.Entry.Sprite : tile.Entry.Gray;
                var color = tile.Entry.Color;
                if (!tile.Ready) { float gray = color.grayscale; color = new Color(gray,gray,gray,inspecting ? .78f : .18f); }
                tile.Icon.color = color;
                tile.Level.gameObject.SetActive(inspecting); tile.Yield.gameObject.SetActive(inspecting);
                if (inspecting)
                {
                    tile.Level.text = "INT "+tile.Entry.Recipe.INT;
                    int diff = Camera.body.skills.INT-tile.Entry.Recipe.INT;
                    tile.Level.color = diff >= 0 ? new Color32(148,219,167,255) : tile.Known ? new Color32(243,207,117,255) : Shortage;
                    var output = tile.Entry.Recipe.result;
                    tile.Yield.text = output.isLiquid ? (output.resultCondition*output.amount).ToString("0.#")+" mL" : "×"+output.amount;
                }
            }
        }
        void Inspect(Tile tile)
        {
            if (inspected != tile) { inspected = tile; PaintInspection(); }
            ShowTooltip(tile.Rect,tile.Known ? NativeCatalog.Name(tile.Entry.Recipe)+" · "+NativeCatalog.CategoryName(tile.Entry.Recipe) : "Unknown recipe");
        }
        void PaintInspection()
        {
            ClearIngredients(); PaintTiles();
            if (inspected == null || !inspected.Known) return;
            var bill = catalog.Bill(inspected.Entry.Recipe); var cells = space.Place(bill.Count);
            int extraRows = cells.Count == 0 ? 0 : Math.Max(0,cells.Where(c => c.Strip == layout.Strips.Count-1).Select(c => c.Y+1-layout.Strips[c.Strip].Height).DefaultIfEmpty(0).Max());
            ResizeContent(extraRows);
            for (int i = 0; i < bill.Count; i++)
            {
                var req = bill[i]; var rect = Rect("Ingredient",content); TopLeft(rect); rect.sizeDelta = new Vector2(cellSide,cellSide); Position(rect,cells[i]);
                ingredients.Add(rect.gameObject);
                var background = rect.gameObject.AddComponent<Image>(); background.color = Tint(req.Category);
                if (req.Sprite)
                {
                    var iconRect = Rect("Icon",rect); Stretch(iconRect,new Vector2(.03f,.03f),new Vector2(.97f,.97f),Vector2.zero,Vector2.zero);
                    var image = iconRect.gameObject.AddComponent<Image>(); image.sprite = req.Sprite; image.color = req.Color; image.preserveAspect = true; image.raycastTarget = false;
                }
                else
                {
                    var generic = Text("Quality",rect,12); Stretch(generic.rectTransform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
                    generic.text = req.Name; generic.enableWordWrapping = true; generic.enableAutoSizing = true; generic.fontSizeMin = 9; generic.fontSizeMax = 12;
                }
                var badge = Badge("Required",rect,false); badge.text = req.Badge; badge.color = req.Short ? Shortage : ink;
                // Ingredient buttons accept focus for inspection, and have no craft listener.
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background; button.transition = Selectable.Transition.None;
                var events = rect.gameObject.AddComponent<TileEvents>();
                events.Enter = () => ShowTooltip(rect,req.Name+(string.IsNullOrEmpty(req.Qualifier) ? "" : " · "+req.Qualifier));
                events.Exit = () => HideTooltip(rect);
            }
        }
        static Color Tint(string category)
        {
            Color pigment;
            switch (category)
            {
                case "Food": pigment = new Color32(238,196,140,255); break;
                case "Medicine": pigment = new Color32(229,160,169,255); break;
                case "Tools": pigment = new Color32(155,191,224,255); break;
                case "Utilities": pigment = new Color32(194,161,209,255); break;
                default: pigment = new Color32(139,197,162,255); break;
            }
            return Color.Lerp(Surface,pigment,.18f);
        }
        void Craft(Tile tile)
        {
            if (lastCraftFrame == Time.frameCount || !Camera.craftingPanel.activeSelf) return;
            lastCraftFrame = Time.frameCount;
            var recipe = tile.Entry.Recipe;
            int registryIndex = Recipes.recipes.IndexOf(recipe);
            if (registryIndex < 0 || !NativeCatalog.Ready(recipe)) { Camera.PlayUISound(PlayerCamera.UISoundType.Deny); RefreshState(); return; }
            Camera.selectedRecipe = registryIndex;
            Camera.TryCraft();
        }
        void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { var camera = Camera; Plugin.Instance.Fail(ex); if (camera) camera.RefreshRecipeList(); }
        }
        void ShowTooltip(RectTransform anchor, string text)
        {
            if (!anchor) return;
            tipAnchor = anchor;
            tooltip.text = tooltipShadow.text = text;
            tooltip.gameObject.SetActive(true); tooltipShadow.gameObject.SetActive(true);
            PositionTooltip();
        }
        void PositionTooltip()
        {
            if (!tipAnchor || !tooltip.gameObject.activeSelf) return;
            float w = Math.Min(root.rect.width-8,tooltip.GetPreferredValues(tooltip.text).x+4);
            var point = root.InverseTransformPoint(tipAnchor.TransformPoint(new Vector3(cellSide*.5f,0,0)));
            float x = Mathf.Clamp(point.x-root.rect.xMin,4+w*.5f,root.rect.width-4-w*.5f);
            var position = new Vector2(x,point.y-root.rect.yMax+2);
            tooltip.rectTransform.sizeDelta = tooltipShadow.rectTransform.sizeDelta = new Vector2(w,22);
            tooltip.rectTransform.anchoredPosition = position;
            tooltipShadow.rectTransform.anchoredPosition = position+new Vector2(1,-1);
            tooltipShadow.transform.SetAsLastSibling(); tooltip.transform.SetAsLastSibling();
        }
        void HideTooltip(RectTransform anchor)
        {
            if (tipAnchor != anchor) return;
            tipAnchor = null; if (tooltip) tooltip.gameObject.SetActive(false); if (tooltipShadow) tooltipShadow.gameObject.SetActive(false);
        }
        void ClearIngredients()
        {
            foreach (var ingredient in ingredients) if (ingredient) { ingredient.SetActive(false); Destroy(ingredient); }
            ingredients.Clear(); if (layout != null) ResizeContent(0);
        }
        void ClearInspection()
        {
            inspected = null; HideTooltip(tipAnchor); ClearIngredients(); if (tiles.Count > 0) PaintTiles();
        }
        public void OnPointerExit(PointerEventData data)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(root,data.position,data.enterEventCamera)) ClearInspection();
        }
        void Update()
        {
            if (!Camera || catalog == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime+.4f;
            Guard(() => {
                if (!catalog.IsCurrent) { RebuildCatalog(); RefreshState(); }
                else if (Math.Abs(width-viewport.rect.width) > 2) { RebuildLayout(); RefreshState(); }
                else if (NativeCatalog.Fingerprint(Camera) != fingerprint) RefreshState();
            });
        }
        void LateUpdate() { if (tooltip) PositionTooltip(); }
        void OnDisable() { if (catalog != null) ClearInspection(); }
        void OnEnable() { if (catalog != null) Guard(RefreshState); }
        public void RestoreVanilla()
        {
            foreach (var state in hidden)
            {
                if (!state.group) continue;
                state.group.alpha = state.alpha; state.group.interactable = state.interactable; state.group.blocksRaycasts = state.raycasts;
                if (state.added) Destroy(state.group);
            }
            hidden.Clear(); gameObject.SetActive(false);
        }
        void OnDestroy() { RestoreVanilla(); catalog?.Dispose(); }
    }
}
