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
    internal sealed class TileEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, IPointerClickHandler
    {
        public GridView Owner;
        public Action Enter, Exit, QuickCraft;
        public void OnPointerEnter(PointerEventData _) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData _) => Exit?.Invoke();
        public void OnSelect(BaseEventData _) => Enter?.Invoke();
        public void OnDeselect(BaseEventData _) => Exit?.Invoke();
        public void OnPointerClick(PointerEventData data) { if (data.button == PointerEventData.InputButton.Right) QuickCraft?.Invoke(); }
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
            public TextMeshProUGUI Unknown, Level, Status;
            public UITooltip Tooltip;
            public CanvasGroup Visibility;
            public GameObject Selection, UsedIn;
            public bool Known, Ready, Visible;
        }
        public PlayerCamera Camera { get; private set; }
        readonly List<Tile> tiles = new List<Tile>();
        readonly Dictionary<Recipe,bool> requirementBadges = new Dictionary<Recipe,bool>();
        readonly HashSet<Recipe> usedInBadges = new HashSet<Recipe>();
        readonly Dictionary<string,Color> categoryTints = new Dictionary<string,Color>();
        Recipe badgeSelection;
        static readonly FieldInfo CategoryField = AccessTools.Field(typeof(PlayerCamera), "selectedRecipeCategory");
        NativeCatalog catalog;
        BoardLayout layout;
        RectTransform viewport, stockViewport, content, stockContent, searchRect, clearRect;
        Transform nativeCraftTarget;
        ScrollRect scroll;
        CanvasGroup stockGroup;
        GridBoundary boundary;
        Toggle gridToggle;
        Vector2 searchMin, searchMax, clearPosition, stockScroll, gridScroll = new Vector2(0,1);
        float stockAlpha;
        bool stockInteractable, stockRaycasts, stockGroupAdded, stockHorizontal, clearMoved, restored;
        bool? gridMode;
        string categoryFilter, searchFilter;
        readonly List<RectState> searchChildren = new List<RectState>();
        TMP_Text nativeIntStyle;
        TMP_FontAsset font;
        Material fontMaterial;
        TMP_SpriteAsset fontSprites;
        Tile hovered;
        float cellSide, width, nextPoll;
        int fingerprint, lastCraftFrame = -1;
        readonly Color ink = Color.white;
        sealed class RectState
        {
            readonly RectTransform rect;
            readonly Vector2 min, max, low, high;
            public RectState(RectTransform rect) { this.rect = rect; min = rect.anchorMin; max = rect.anchorMax; low = rect.offsetMin; high = rect.offsetMax; }
            public void Restore() { if (rect) { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high; } }
        }

        public void Initialize(PlayerCamera camera)
        {
            Camera = camera;
            if (CategoryField == null) throw new MissingFieldException("PlayerCamera.selectedRecipeCategory is required to preserve native category filtering.");
            Stretch((RectTransform)transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var recipeBox = Resources.Load<GameObject>("Special/RecipeBox");
            if (!recipeBox) throw new InvalidOperationException("Native Special/RecipeBox prefab is missing.");
            var template = recipeBox.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
            nativeIntStyle = recipeBox.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
            if (!template || !template.font || !nativeIntStyle) throw new InvalidOperationException("Native recipe label or INT style is missing.");
            font = template.font; fontMaterial = template.fontSharedMaterial;
            var preview = camera.craftingPanel.transform.GetChild(6);
            var previewButton = preview.GetComponentInParent<Button>();
            nativeCraftTarget = previewButton && previewButton.transform.IsChildOf(camera.craftingPanel.transform) ? previewButton.transform : preview;
            var nativeRequirements = camera.craftingPanel.transform.GetChild(8).GetComponent<TextMeshProUGUI>();
            fontSprites = nativeRequirements.spriteAsset ? nativeRequirements.spriteAsset : template.spriteAsset ? template.spriteAsset : TMP_Settings.defaultSpriteAsset;
            scroll = camera.recipeScrollRect;
            stockContent = scroll.content;
            if (stockContent != camera.recipeListContent) throw new InvalidOperationException("Native recipe ScrollRect.content differs from recipeListContent; cannot safely switch views.");
            stockViewport = scroll.viewport ? scroll.viewport : stockContent.parent as RectTransform;
            if (!stockViewport) throw new InvalidOperationException("Native recipe scroll viewport is missing.");
            viewport = Rect("CraftingGridViewport",stockViewport);
            Stretch(viewport,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            UpdateGridViewport();
            ReadCategoryTints();
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
            var hint = Text("QuickCraftHint",content,16); hint.text = "Right click to quick craft"; hint.alignment = TextAlignmentOptions.Left;
            Stretch(hint.rectTransform,new Vector2(0,1),Vector2.one,new Vector2(4,-26),new Vector2(-4,-2));
            Canvas.ForceUpdateCanvases();
            RefreshState();
            Plugin.Log.LogInfo($"Inline crafting interface attached to native scroll area. Game {Application.version}, Unity {Application.unityVersion}.");
        }
        void UpdateGridViewport()
        {
            Vector2 start = Vector2.zero, end = Vector2.zero;
            Reserve(scroll.verticalScrollbar,true); Reserve(scroll.horizontalScrollbar,false);
            viewport.offsetMin = start; viewport.offsetMax = -end;

            void Reserve(Scrollbar bar, bool vertical)
            {
                if (!bar) return;
                var corners = new Vector3[4]; ((RectTransform)bar.transform).GetWorldCorners(corners);
                var points = corners.Select(stockViewport.InverseTransformPoint).ToArray();
                float left = points.Min(p => p.x), right = points.Max(p => p.x), bottom = points.Min(p => p.y), top = points.Max(p => p.y);
                var area = stockViewport.rect;
                if (vertical)
                {
                    if (top <= area.yMin || bottom >= area.yMax) return;
                    var inset = GridRules.ScrollbarInsets(area.width,left-area.xMin,right-area.xMin);
                    start.x = inset.start; end.x = inset.end;
                }
                else
                {
                    if (right <= area.xMin || left >= area.xMax) return;
                    var inset = GridRules.ScrollbarInsets(area.height,bottom-area.yMin,top-area.yMin);
                    start.y = inset.start; end.y = inset.end;
                }
            }
        }
        void ReadCategoryTints()
        {
            for (int i = 1; i < Camera.recipeCategorySelectImages.Length; i++)
            {
                var button = Camera.recipeCategorySelectImages[i];
                if (!button) continue;
                string category = ((Recipes.RecipeCategory)(i-1)).ToString();
                Color color;
                try { color = NativeFilterColors.Read(button); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Could not sample native {category} filter icon: {ex.Message}"); color = button.color; }
                categoryTints[category] = Color.Lerp(Color.black,color,.18f);
                Plugin.Log.LogInfo($"Native {category} filter hue: #{ColorUtility.ToHtmlStringRGB(color)}.");
            }
        }
        void CreateGridToggle()
        {
            searchRect = Camera.recipeFilterField.transform as RectTransform;
            if (!searchRect) throw new InvalidOperationException("Native crafting search field has no RectTransform.");
            searchMin = searchRect.offsetMin; searchMax = searchRect.offsetMax;
            // Fixed-width native text areas clip their leading characters when the field shrinks.
            var input = Camera.recipeFilterField;
            foreach (var child in new[] { input.textViewport,input.textComponent ? input.textComponent.rectTransform : null,input.placeholder ? input.placeholder.rectTransform : null }.Where(r => r && r != searchRect && r.IsChildOf(searchRect)).Distinct())
                StretchSearchChild(child);
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
            input.ForceLabelUpdate();
        }
        void StretchSearchChild(RectTransform rect)
        {
            searchChildren.Add(new RectState(rect));
            var parent = rect.parent as RectTransform;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            float left = parent.InverseTransformPoint(corners[0]).x-parent.rect.xMin;
            float right = parent.rect.xMax-parent.InverseTransformPoint(corners[2]).x;
            rect.anchorMin = new Vector2(0,rect.anchorMin.y); rect.anchorMax = new Vector2(1,rect.anchorMax.y);
            rect.offsetMin = new Vector2(left,rect.offsetMin.y); rect.offsetMax = new Vector2(-right,rect.offsetMax.y);
        }
        void ApplyMode(bool wanted)
        {
            gridToggle.SetIsOnWithoutNotify(wanted);
            if (gridMode == wanted) return;
            scroll.StopMovement();
            if (gridMode == true) gridScroll = new Vector2(scroll.horizontalNormalizedPosition,scroll.verticalNormalizedPosition);
            else stockScroll = new Vector2(scroll.horizontalNormalizedPosition,scroll.verticalNormalizedPosition);
            ClearHover();
            gridMode = wanted;
            stockGroup.alpha = wanted ? 0 : stockAlpha;
            stockGroup.interactable = !wanted && stockInteractable; stockGroup.blocksRaycasts = !wanted && stockRaycasts;
            viewport.gameObject.SetActive(wanted);
            content.gameObject.SetActive(wanted);
            scroll.viewport = wanted ? viewport : stockViewport;
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
        TextMeshProUGUI CornerText(string name, RectTransform parent, bool top)
        {
            var text = Text(name,parent,Mathf.Clamp(cellSide*.32f,14,24));
            text.alignment = top ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.BottomRight;
            Stretch(text.rectTransform,new Vector2(0,top ? 1 : 0),new Vector2(1,top ? 1 : 0),new Vector2(2,top ? -28 : 1),new Vector2(-2,top ? -1 : 28));
            return text;
        }
        GameObject SelectionFrame(RectTransform tile)
        {
            var frame = Rect("SelectedRecipe",tile);
            Stretch(frame,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            Edge("Left",Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(2,0));
            Edge("Right",new Vector2(1,0),Vector2.one,new Vector2(-2,0),Vector2.zero);
            Edge("Top",new Vector2(0,1),Vector2.one,new Vector2(0,-2),Vector2.zero);
            Edge("Bottom",Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,2));
            return frame.gameObject;

            void Edge(string name, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
            {
                var edge = Rect(name,frame); Stretch(edge,min,max,low,high);
                var image = edge.gameObject.AddComponent<Image>(); image.color = Color.white; image.raycastTarget = false;
            }
        }
        void RebuildCatalog()
        {
            catalog?.Dispose(); catalog = new NativeCatalog(); RebuildLayout();
            Plugin.Log.LogInfo($"Inline grid catalog: {catalog.Entries.Count} recipes, {layout.Columns} columns x {layout.Rows} rows; {layout.MappedCount} hand-picked, {catalog.Entries.Count-layout.MappedCount} appended.");
        }
        Vector2 IconSize(Sprite sprite)
        {
            // Reuse the native preview's natural pixel sizing, scaled to a cell-sized preview box.
            Vector2 size = PlayerCamera.ImageSizeDelta(sprite.texture,12f,150f);
            if (sprite.rect.width != sprite.texture.width || sprite.rect.height != sprite.texture.height)
            {
                // For atlases, apply that same native rule to this sprite's region rather than the atlas.
                size = sprite.rect.size*12f;
                size *= Mathf.Min(1,150f/Mathf.Max(size.x,size.y));
            }
            return size*(cellSide*.94f/150f);
        }
        void RebuildLayout()
        {
            ClearHover();
            foreach (var tile in tiles) { tile.Rect.gameObject.SetActive(false); Destroy(tile.Rect.gameObject); }
            tiles.Clear();
            width = viewport.rect.width;
            layout = GridLayout.Create(catalog.Entries.Select(e => e.Layout),LayoutResource.Spec,GridRules.ColumnCount(width,LayoutResource.Spec.ReferenceColumns));
            cellSide = GridRules.CellSide(width,layout.Columns);
            foreach (var entry in catalog.Entries)
            {
                var rect = Rect("Recipe-"+entry.Layout.Key,content); TopLeft(rect); rect.sizeDelta = new Vector2(cellSide,cellSide);
                var cell = layout.Cells[entry.Layout.Key];
                rect.anchoredPosition = new Vector2(4+cell.X*cellSide,-28-cell.Y*cellSide);
                var tile = new Tile { Entry = entry,Rect = rect,Background = rect.gameObject.AddComponent<Image>(),Visibility = rect.gameObject.AddComponent<CanvasGroup>() };
                var iconRect = Rect("Icon",rect); iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(.5f,.5f);
                iconRect.anchoredPosition = Vector2.zero; iconRect.sizeDelta = entry.Sprite ? IconSize(entry.Sprite) : Vector2.zero;
                tile.Icon = iconRect.gameObject.AddComponent<Image>(); tile.Icon.preserveAspect = true; tile.Icon.raycastTarget = false;
                tile.Unknown = Text("Unknown",rect,20); Stretch(tile.Unknown.rectTransform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
                tile.Unknown.text = "\u00d7"; tile.Unknown.color = new Color32(32,35,30,255);
                tile.Level = CornerText("NativeINT",rect,true); tile.Level.font = nativeIntStyle.font;
                tile.Level.fontSharedMaterial = nativeIntStyle.fontSharedMaterial; tile.Level.fontStyle = nativeIntStyle.fontStyle;
                tile.Level.fontSize = Mathf.Min(nativeIntStyle.fontSize,cellSide*.38f);
                tile.Status = CornerText("NativeAvailability",rect,false); tile.Status.richText = true;
                var usedIn = Rect("UsedIn",rect); usedIn.anchorMin = usedIn.anchorMax = usedIn.pivot = Vector2.one;
                usedIn.anchoredPosition = new Vector2(-3,-3); usedIn.sizeDelta = new Vector2(5,5);
                var dot = usedIn.gameObject.AddComponent<Image>(); dot.color = Color.white; dot.raycastTarget = false;
                tile.UsedIn = usedIn.gameObject;
                tile.Selection = SelectionFrame(rect);
                tile.Tooltip = rect.gameObject.AddComponent<UITooltip>(); tile.Tooltip.skipLocale = true;
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = tile.Background; button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => Guard(() => Select(tile)));
                var events = rect.gameObject.AddComponent<TileEvents>(); events.Owner = this;
                events.Enter = () => Hover(tile); events.Exit = () => { if (hovered == tile) ClearHover(); };
                events.QuickCraft = () => Guard(() => Craft(tile));
                tiles.Add(tile);
            }
            content.sizeDelta = new Vector2(Math.Max(width,8+layout.Columns*cellSide),32+layout.Rows*cellSide);
            if (gridMode == true) scroll.horizontal = content.rect.width > viewport.rect.width+1;
            gridScroll = new Vector2(0,1);
            if (gridMode == true) scroll.normalizedPosition = gridScroll;
        }
        public void RefreshState()
        {
            if (restored) return;
            bool wanted = Plugin.Instance.UseGrid;
            UpdateGridViewport();
            if (wanted && (catalog == null || !catalog.IsCurrent)) RebuildCatalog();
            ApplyMode(wanted);
            if (!wanted) return;
            categoryFilter = CategoryField.GetValue(Camera)?.ToString(); searchFilter = Camera.recipeFilter ?? "";
            foreach (var tile in tiles)
            {
                tile.Known = NativeCatalog.Known(tile.Entry.Recipe);
                tile.Ready = tile.Known && tile.Entry.Recipe.GetItemsForRecipe() != null;
            }
            UpdateRequirementBadges();
            fingerprint = NativeCatalog.Fingerprint(Camera);
            PaintTiles(); nextPoll = Time.unscaledTime+.4f;
        }
        void PaintTiles()
        {
            var selected = SelectedRecipe();
            foreach (var tile in tiles)
            {
                var recipe = tile.Entry.Recipe;
                tile.Visible = GridRules.MatchesSearch(tile.Known,tile.Known && !string.IsNullOrEmpty(searchFilter) ? recipe.simpleName : "",searchFilter);
                tile.Visibility.alpha = tile.Visible ? 1 : 0; tile.Visibility.interactable = tile.Visible; tile.Visibility.blocksRaycasts = tile.Visible;
                tile.Background.color = GridRules.HighlightCategory(tile.Known,tile.Entry.Layout.Category,categoryFilter) && categoryTints.TryGetValue(tile.Entry.Layout.Category,out var tint) ? tint : Color.clear;
                tile.Selection.SetActive(tile.Known && recipe == selected);
                tile.UsedIn.SetActive(tile.Known && usedInBadges.Contains(recipe));
                tile.Unknown.gameObject.SetActive(!tile.Known); tile.Icon.gameObject.SetActive(tile.Known && tile.Entry.Sprite);
                tile.Icon.sprite = tile.Ready ? tile.Entry.Sprite : tile.Entry.Gray;
                var color = tile.Entry.Color;
                if (!tile.Ready) { float gray = color.grayscale; color = new Color(gray,gray,gray,hovered == tile ? .78f : .07f); }
                tile.Icon.color = color;
                tile.Level.gameObject.SetActive(tile.Known && tile.Visible && hovered == tile);
                tile.Level.text = recipe.INT.ToString(); tile.Level.color = Camera.RecipeINTToColor(recipe.INT);
                bool hasBadge = requirementBadges.TryGetValue(recipe,out var requirementAvailable) && tile.Known;
                tile.Status.gameObject.SetActive(hasBadge);
                if (hasBadge) tile.Status.text = requirementAvailable ? "<sprite index=23>" : "<sprite index=24>";
                tile.Tooltip.tipName = tile.Known ? recipe.simpleName : Locale.GetOther("unknownobject");
                tile.Tooltip.tipDesc = tile.Known ? recipe.description : "";
            }
        }
        Recipe SelectedRecipe() => Camera.selectedRecipe >= 0 && Camera.selectedRecipe < Recipes.recipes.Count ? Recipes.recipes[Camera.selectedRecipe] : null;
        void UpdateRequirementBadges()
        {
            requirementBadges.Clear(); usedInBadges.Clear(); badgeSelection = SelectedRecipe();
            if (badgeSelection == null || !NativeCatalog.Known(badgeSelection)) return;
            foreach (var tile in tiles)
            {
                var consumer = tile.Entry.Recipe;
                if (tile.Known && consumer != badgeSelection && consumer.items.Any(slot =>
                    NativeCatalog.IsRequirementOutput(badgeSelection,slot,slot.specific ? slot.specificId : null,false))) usedInBadges.Add(consumer);
            }
            var allocation = badgeSelection.GetItemsForRecipeThorough();
            for (int i = 0; i < badgeSelection.items.Count; i++)
            {
                var slot = badgeSelection.items[i]; bool available = allocation[i];
                string id = NativeCatalog.RequirementOutputId(slot,allocation[i]);
                foreach (var tile in tiles)
                {
                    var recipe = tile.Entry.Recipe;
                    if (!tile.Known || recipe == badgeSelection || !NativeCatalog.IsRequirementOutput(recipe,slot,id,available)) continue;
                    requirementBadges[recipe] = available && (!requirementBadges.TryGetValue(recipe,out var previous) || previous);
                }
            }
        }
        void Hover(Tile tile)
        {
            if (gridMode != true || !tile.Visible) return;
            hovered = tile; PaintTiles();
        }
        void ClearHover() { hovered = null; if (!restored && tiles.Count > 0) PaintTiles(); }
        void Select(Tile tile)
        {
            if (gridMode != true || !tile.Visible || !tile.Known) return;
            int index = Recipes.recipes.IndexOf(tile.Entry.Recipe);
            if (index >= 0) { Camera.SelectRecipe(index); UpdateRequirementBadges(); PaintTiles(); }
        }
        public bool ShouldKeepOpenForQuickCraft(PlayerCamera camera)
        {
            if (camera != Camera || !isActiveAndEnabled || !camera.craftingPanel.activeSelf || KeyBinds.GetBind("iteminteract") != KeyCode.Mouse1 || !Input.GetMouseButtonDown(1) || ExplicitClosePressed()) return false;
            // Right-click crafts on grid tiles and the native preview; explicit close/back keys still win.
            var hits = UIUtil.GetEventSystemRaycastResults();
            return lastCraftFrame == Time.frameCount || OnNativeCraftTarget(hits.FirstOrDefault().gameObject) ||
                gridMode == true && hits.Any(hit => hit.gameObject.TryGetComponent<TileEvents>(out var events) && events.Owner == this);
        }
        static bool ExplicitClosePressed() => Input.GetKeyDown(KeyBinds.GetBind("craft")) || Input.GetKeyDown(KeyBinds.GetBind("pause")) || Input.GetKeyDown(KeyBinds.GetBind("woundview"));
        bool OnNativeCraftTarget(GameObject hit) => hit && nativeCraftTarget && (hit.transform == nativeCraftTarget || hit.transform.IsChildOf(nativeCraftTarget));
        void Craft(Tile tile)
        {
            if (gridMode == true && tile.Visible) Craft(tile.Entry.Recipe);
        }
        void Craft(Recipe recipe)
        {
            if (recipe == null || lastCraftFrame == Time.frameCount || !Camera.craftingPanel.activeSelf) return;
            lastCraftFrame = Time.frameCount;
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
        public void OnPointerExit(PointerEventData data)
        {
            if (gridMode == true && !RectTransformUtility.RectangleContainsScreenPoint(viewport,data.position,data.enterEventCamera)) ClearHover();
        }
        void Update()
        {
            if (!Camera || restored || !Camera.craftingPanel.activeSelf) return;
            // Poll the shortcut without adding a pointer handler that could intercept the native Button's left click.
            if (Input.GetMouseButtonDown(1) && !ExplicitClosePressed() && OnNativeCraftTarget(UIUtil.GetEventSystemRaycastResults().FirstOrDefault().gameObject))
                Guard(() => Craft(SelectedRecipe()));
            if (gridMode != true || catalog == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime+.4f;
            Guard(() => {
                UpdateGridViewport();
                if (!catalog.IsCurrent) { RebuildCatalog(); RefreshState(); }
                else if (Math.Abs(width-viewport.rect.width) > 2) { RebuildLayout(); RefreshState(); }
                else if (NativeCatalog.Fingerprint(Camera) != fingerprint) RefreshState();
                else if (SelectedRecipe() != badgeSelection) { UpdateRequirementBadges(); PaintTiles(); }
            });
        }
        void OnDisable() { if (!restored && catalog != null) ClearHover(); }
        void OnEnable() { if (!restored && gridToggle) Guard(RefreshState); }
        public void RestoreVanilla()
        {
            if (restored) return;
            restored = true;
            if (scroll && stockContent) { scroll.StopMovement(); scroll.viewport = stockViewport; scroll.content = stockContent; scroll.horizontal = stockHorizontal; scroll.normalizedPosition = stockScroll; }
            if (stockGroup)
            {
                stockGroup.alpha = stockAlpha; stockGroup.interactable = stockInteractable; stockGroup.blocksRaycasts = stockRaycasts;
                if (stockGroupAdded) Destroy(stockGroup);
            }
            if (searchRect) { searchRect.offsetMin = searchMin; searchRect.offsetMax = searchMax; }
            foreach (var state in searchChildren) state.Restore();
            if (Camera && Camera.recipeFilterField) Camera.recipeFilterField.ForceLabelUpdate();
            if (clearMoved && clearRect) clearRect.anchoredPosition = clearPosition;
            if (boundary) { boundary.Exit = null; Destroy(boundary); }
            foreach (var obj in new[] { viewport ? viewport.gameObject : null,gridToggle ? gridToggle.gameObject : null })
                if (obj) { obj.SetActive(false); Destroy(obj); }
            gameObject.SetActive(false);
        }
        void OnDestroy() { RestoreVanilla(); catalog?.Dispose(); }
    }
}
