using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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
    internal sealed class GridBoundary : MonoBehaviour, IPointerExitHandler
    {
        public Action<PointerEventData> Exit;
        public void OnPointerExit(PointerEventData data) => Exit?.Invoke(data);
    }

    internal sealed class GridView : MonoBehaviour, IPointerExitHandler
    {
        sealed class Tile
        {
            public NativeEntry Entry;
            public RectTransform Rect;
            public Image Background, Icon;
            public TextMeshProUGUI Unknown, Level, Yield;
            public CanvasGroup Visibility;
            public bool Known, Ready, Visible;
        }
        public PlayerCamera Camera { get; private set; }
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<GameObject> ingredients = new List<GameObject>();
        readonly HoverDelay<Tile> hoverDetails = new HoverDelay<Tile>(.1f);
        static readonly FieldInfo CategoryField = AccessTools.Field(typeof(PlayerCamera), "selectedRecipeCategory");
        NativeCatalog catalog;
        BoardLayout layout;
        IngredientSpace space;
        RectTransform viewport, content, stockContent, searchRect, clearRect;
        ScrollRect scroll;
        CanvasGroup stockGroup;
        GridBoundary boundary;
        Toggle gridToggle;
        Vector2 searchMin, searchMax, clearPosition, stockScroll, gridScroll = new Vector2(0,1);
        float stockAlpha;
        bool stockInteractable, stockRaycasts, stockGroupAdded, stockHorizontal, clearMoved, restored;
        bool? gridMode;
        string categoryFilter, searchFilter;
        TextMeshProUGUI tooltip, tooltipShadow;
        TMP_FontAsset font;
        Material fontMaterial;
        TMP_SpriteAsset fontSprites;
        Tile inspected;
        RectTransform tipAnchor;
        float cellSide, width, height, nextPoll;
        int fingerprint, lastCraftFrame = -1;
        readonly Color ink = new Color32(238,239,227,255);
        static readonly Color Surface = Color.black;
        static readonly Color Shortage = new Color32(255,153,149,255);

        public void Initialize(PlayerCamera camera)
        {
            Camera = camera;
            if (CategoryField == null) throw new MissingFieldException("PlayerCamera.selectedRecipeCategory is required to preserve native category filtering.");
            Stretch((RectTransform)transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var template = camera.craftingPanel.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.font);
            if (!template) throw new InvalidOperationException("No native crafting font found on PlayerCamera.craftingPanel.");
            font = template.font; fontMaterial = template.fontSharedMaterial; fontSprites = template.spriteAsset;
            scroll = camera.recipeScrollRect;
            stockContent = scroll.content;
            if (stockContent != camera.recipeListContent) throw new InvalidOperationException("Native recipe ScrollRect.content differs from recipeListContent; cannot safely switch views.");
            viewport = scroll.viewport ? scroll.viewport : stockContent.parent as RectTransform;
            if (!viewport) throw new InvalidOperationException("Native recipe scroll viewport is missing.");
            stockHorizontal = scroll.horizontal;
            stockScroll = new Vector2(scroll.horizontalNormalizedPosition,scroll.verticalNormalizedPosition);
            stockGroup = stockContent.GetComponent<CanvasGroup>(); stockGroupAdded = !stockGroup;
            if (stockGroupAdded) stockGroup = stockContent.gameObject.AddComponent<CanvasGroup>();
            stockAlpha = stockGroup.alpha; stockInteractable = stockGroup.interactable; stockRaycasts = stockGroup.blocksRaycasts;
            // The stock content stays intact for native refreshes. Only ScrollRect.content changes.
            content = Rect("Content",viewport); TopLeft(content);
            content.name = "CraftingGridContent"; content.gameObject.AddComponent<Image>().color = Color.black;
            content.gameObject.SetActive(false);
            boundary = viewport.gameObject.AddComponent<GridBoundary>(); boundary.Exit = OnPointerExit;
            CreateGridToggle();
            tooltipShadow = Text("GridTooltipShadow",viewport,14); tooltipShadow.color = Color.black;
            tooltip = Text("GridTooltip",viewport,14); tooltip.color = ink;
            foreach (var text in new[] { tooltipShadow,tooltip })
            {
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0,1);
                text.rectTransform.pivot = new Vector2(.5f,0);
                text.alignment = TextAlignmentOptions.Center;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.gameObject.SetActive(false);
            }
            Canvas.ForceUpdateCanvases();
            RefreshState();
            Plugin.Log.LogInfo($"Inline crafting interface attached to native scroll area. Game {Application.version}, Unity {Application.unityVersion}.");
        }
        void CreateGridToggle()
        {
            searchRect = Camera.recipeFilterField.transform as RectTransform;
            if (!searchRect) throw new InvalidOperationException("Native crafting search field has no RectTransform.");
            searchMin = searchRect.offsetMin; searchMax = searchRect.offsetMax;
            float originalWidth = searchRect.rect.width, reserve = Mathf.Min(124,originalWidth*.35f);
            var rect = Rect("GridPreference",searchRect.parent);
            rect.anchorMin = searchRect.anchorMin; rect.anchorMax = searchRect.anchorMax; rect.pivot = searchRect.pivot;
            rect.offsetMin = searchMin+new Vector2(originalWidth-reserve+4,0); rect.offsetMax = searchMax;
            searchRect.offsetMax = searchMax-new Vector2(reserve,0);
            if (Camera.searchClearButton && !Camera.searchClearButton.transform.IsChildOf(searchRect))
            {
                clearRect = Camera.searchClearButton.transform as RectTransform;
                if (clearRect)
                {
                    clearPosition = clearRect.anchoredPosition; clearMoved = true;
                    clearRect.position += searchRect.parent.TransformVector(new Vector3(-reserve,0,0));
                }
            }
            var target = rect.gameObject.AddComponent<Image>(); target.color = Color.black;
            var box = Rect("Checkbox",rect); box.anchorMin = box.anchorMax = new Vector2(0,.5f); box.pivot = new Vector2(.5f,.5f);
            box.anchoredPosition = new Vector2(15,0); box.sizeDelta = new Vector2(22,22); box.gameObject.AddComponent<Image>().color = Color.white;
            var inner = Rect("Inside",box); Stretch(inner,new Vector2(.12f,.12f),new Vector2(.88f,.88f),Vector2.zero,Vector2.zero); inner.gameObject.AddComponent<Image>().color = Color.black;
            var check = Rect("Check",inner); Stretch(check,new Vector2(.25f,.25f),new Vector2(.75f,.75f),Vector2.zero,Vector2.zero);
            var mark = check.gameObject.AddComponent<Image>(); mark.color = Color.white;
            var label = Text("Grid",rect,20); label.text = "Grid"; label.alignment = TextAlignmentOptions.Left;
            Stretch(label.rectTransform,Vector2.zero,Vector2.one,new Vector2(33,0),Vector2.zero);
            gridToggle = rect.gameObject.AddComponent<Toggle>(); gridToggle.targetGraphic = target; gridToggle.graphic = mark; gridToggle.transition = Selectable.Transition.None;
            gridToggle.SetIsOnWithoutNotify(Plugin.Instance.UseGrid);
            gridToggle.onValueChanged.AddListener(value => Guard(() => Plugin.Instance.SetGridMode(value)));
        }
        void ApplyMode(bool wanted)
        {
            gridToggle.SetIsOnWithoutNotify(wanted);
            if (gridMode == wanted) return;
            scroll.StopMovement();
            if (gridMode == true) gridScroll = new Vector2(scroll.horizontalNormalizedPosition,scroll.verticalNormalizedPosition);
            else stockScroll = new Vector2(scroll.horizontalNormalizedPosition,scroll.verticalNormalizedPosition);
            ClearInspection();
            gridMode = wanted;
            stockGroup.alpha = wanted ? 0 : stockAlpha;
            stockGroup.interactable = !wanted && stockInteractable; stockGroup.blocksRaycasts = !wanted && stockRaycasts;
            content.gameObject.SetActive(wanted);
            scroll.content = wanted ? content : stockContent;
            scroll.horizontal = wanted ? content.rect.width > viewport.rect.width+1 : stockHorizontal;
            scroll.normalizedPosition = wanted ? gridScroll : stockScroll;
            if (wanted && EventSystem.current && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(stockContent)) EventSystem.current.SetSelectedGameObject(null);
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
            Plugin.Log.LogInfo($"Inline grid catalog: {catalog.Entries.Count} recipes, {layout.Columns} columns × {layout.Strips[0].Height} rows.");
        }
        void RebuildLayout()
        {
            ClearInspection();
            foreach (var tile in tiles) { tile.Rect.gameObject.SetActive(false); Destroy(tile.Rect.gameObject); }
            tiles.Clear();
            width = viewport.rect.width; height = viewport.rect.height;
            layout = GridLayout.CreateVertical(catalog.Entries.Select(e => e.Layout));
            if (layout.Columns == 0) throw new InvalidOperationException("The native recipe registry contains no displayable recipes.");
            cellSide = Mathf.Max(37,Mathf.Min(56,(width-8-2*(layout.Columns-1))/layout.Columns));
            cellSide = Mathf.Max(37,Mathf.Min(cellSide,(height-32-2*(layout.Strips[0].Height-1))/Math.Max(1,layout.Strips[0].Height)));
            space = new IngredientSpace(layout,catalog.Entries.Max(e => e.Recipe.items.Count));
            foreach (var entry in catalog.Entries)
            {
                var rect = Rect("Recipe-"+entry.Layout.Key,content); TopLeft(rect); rect.sizeDelta = new Vector2(cellSide,cellSide);
                Position(rect,layout.Cells[entry.Layout.Key]);
                var tile = new Tile { Entry = entry,Rect = rect,Background = rect.gameObject.AddComponent<Image>() };
                tile.Visibility = rect.gameObject.AddComponent<CanvasGroup>();
                tile.Background.color = Color.clear;
                var iconRect = Rect("Icon",rect); Stretch(iconRect,new Vector2(.03f,.03f),new Vector2(.97f,.97f),Vector2.zero,Vector2.zero);
                tile.Icon = iconRect.gameObject.AddComponent<Image>(); tile.Icon.preserveAspect = true; tile.Icon.raycastTarget = false;
                tile.Unknown = Text("Unknown",rect,20); Stretch(tile.Unknown.rectTransform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); tile.Unknown.text = "×"; tile.Unknown.color = new Color32(120,130,114,255);
                tile.Level = Badge("INT",rect,true); tile.Yield = Badge("Yield",rect,false);
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = tile.Background; button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => Guard(() => Craft(tile)));
                var events = rect.gameObject.AddComponent<TileEvents>();
                events.Enter = () => Guard(() => Inspect(tile)); events.Exit = () => { hoverDetails.Leave(tile); HideTooltip(rect); };
                tiles.Add(tile);
            }
            ResizeContent(0); gridScroll = new Vector2(0,1);
            if (gridMode == true) scroll.normalizedPosition = gridScroll;
        }
        float StripTop(int strip)
        {
            float y = 0; for (int i = 0; i < strip; i++) y += 28+layout.Strips[i].Height*(cellSide+2)+8;
            return y+28;
        }
        void Position(RectTransform rect, Cell cell) => rect.anchoredPosition = new Vector2(4+cell.X*(cellSide+2),-StripTop(cell.Strip)-cell.Y*(cellSide+2));
        void ResizeContent(int extraRows)
        {
            int last = layout.Strips.Count-1;
            content.sizeDelta = new Vector2(Math.Max(viewport.rect.width,8+layout.Columns*(cellSide+2)-2),StripTop(last)+(layout.Strips[last].Height+extraRows)*(cellSide+2));
            if (gridMode == true) scroll.horizontal = content.rect.width > viewport.rect.width+1;
        }
        public void RefreshState()
        {
            if (restored) return;
            bool wanted = Plugin.Instance.UseGrid;
            if (wanted && (catalog == null || !catalog.IsCurrent)) RebuildCatalog();
            ApplyMode(wanted);
            if (!wanted) return;
            string nextCategory = CategoryField.GetValue(Camera)?.ToString(), nextSearch = Camera.recipeFilter ?? "";
            if (categoryFilter != nextCategory || searchFilter != nextSearch) { categoryFilter = nextCategory; searchFilter = nextSearch; ClearInspection(); }
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
                bool matches = GridRules.MatchesFilter(tile.Known,tile.Known && !string.IsNullOrEmpty(searchFilter) ? tile.Entry.Recipe.simpleName : "",tile.Entry.Layout.Category,categoryFilter,searchFilter);
                tile.Visible = GridRules.Visible(tile.Known,matches,highlight);
                tile.Visibility.alpha = tile.Visible ? 1 : 0; tile.Visibility.interactable = tile.Visible; tile.Visibility.blocksRaycasts = tile.Visible;
                tile.Background.color = highlight ? Tint(tile.Entry.Layout.Category) : Color.clear;
                tile.Unknown.gameObject.SetActive(!tile.Known); tile.Icon.gameObject.SetActive(tile.Known && tile.Entry.Sprite);
                tile.Icon.sprite = tile.Ready ? tile.Entry.Sprite : tile.Entry.Gray;
                var color = tile.Entry.Color;
                if (!tile.Ready) { float gray = color.grayscale; color = new Color(gray,gray,gray,inspecting ? .78f : .07f); }
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
            if (gridMode != true || !tile.Visible) return;
            if (inspected != tile) { inspected = tile; PaintInspection(); }
            if (tile.Known) hoverDetails.Enter(tile,Time.unscaledTime);
            ShowTooltip(tile.Rect,tile.Known ? NativeCatalog.Name(tile.Entry.Recipe)+" · "+NativeCatalog.CategoryName(tile.Entry.Recipe) : "Unknown recipe");
        }
        void UpdateNativeDetails(Tile tile)
        {
            if (gridMode != true || !tile.Known || !tile.Visible || inspected != tile) return;
            int index = Recipes.recipes.IndexOf(tile.Entry.Recipe);
            if (index < 0) return;
            // Native SelectRecipe also plays audio. Its read-only details refresh does not.
            Camera.selectedRecipe = index;
            Camera.RefreshCurrentlySelectedRecipe();
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
            if (gridMode != true || !tile.Visible || lastCraftFrame == Time.frameCount || !Camera.craftingPanel.activeSelf) return;
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
            float w = Math.Min(viewport.rect.width-8,tooltip.GetPreferredValues(tooltip.text).x+4);
            var point = viewport.InverseTransformPoint(tipAnchor.TransformPoint(new Vector3(cellSide*.5f,0,0)));
            float x = Mathf.Clamp(point.x-viewport.rect.xMin,4+w*.5f,viewport.rect.width-4-w*.5f);
            var position = new Vector2(x,point.y-viewport.rect.yMax+2);
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
            inspected = null; hoverDetails.Clear(); HideTooltip(tipAnchor); ClearIngredients(); if (tiles.Count > 0) PaintTiles();
        }
        public void OnPointerExit(PointerEventData data)
        {
            if (gridMode == true && !RectTransformUtility.RectangleContainsScreenPoint(viewport,data.position,data.enterEventCamera)) ClearInspection();
        }
        void Update()
        {
            if (!Camera || restored || gridMode != true || catalog == null) return;
            var due = hoverDetails.TakeReady(Time.unscaledTime);
            if (due != null) Guard(() => UpdateNativeDetails(due));
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime+.4f;
            Guard(() => {
                if (!catalog.IsCurrent) { RebuildCatalog(); RefreshState(); }
                else if (Math.Abs(width-viewport.rect.width) > 2 || Math.Abs(height-viewport.rect.height) > 2) { RebuildLayout(); RefreshState(); }
                else if (NativeCatalog.Fingerprint(Camera) != fingerprint)
                {
                    RefreshState();
                    if (inspected != null && inspected.Known && Camera.selectedRecipe == Recipes.recipes.IndexOf(inspected.Entry.Recipe)) Camera.RefreshCurrentlySelectedRecipe();
                }
            });
        }
        void LateUpdate() { if (tooltip) PositionTooltip(); }
        void OnDisable() { if (!restored && catalog != null) ClearInspection(); }
        void OnEnable() { if (!restored && gridToggle) Guard(RefreshState); }
        public void RestoreVanilla()
        {
            if (restored) return;
            restored = true;
            hoverDetails.Clear();
            if (scroll && stockContent) { scroll.StopMovement(); scroll.content = stockContent; scroll.horizontal = stockHorizontal; scroll.normalizedPosition = stockScroll; }
            if (stockGroup)
            {
                stockGroup.alpha = stockAlpha; stockGroup.interactable = stockInteractable; stockGroup.blocksRaycasts = stockRaycasts;
                if (stockGroupAdded) Destroy(stockGroup);
            }
            if (searchRect) { searchRect.offsetMin = searchMin; searchRect.offsetMax = searchMax; }
            if (clearMoved && clearRect) clearRect.anchoredPosition = clearPosition;
            if (boundary) { boundary.Exit = null; Destroy(boundary); }
            foreach (var obj in new[] { content ? content.gameObject : null,gridToggle ? gridToggle.gameObject : null,tooltip ? tooltip.gameObject : null,tooltipShadow ? tooltipShadow.gameObject : null })
                if (obj) { obj.SetActive(false); Destroy(obj); }
            gameObject.SetActive(false);
        }
        void OnDestroy() { RestoreVanilla(); catalog?.Dispose(); }
    }
}
