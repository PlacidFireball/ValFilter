using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GUIFramework;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValFilter
{
    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        const string pluginGUID = "com.placidfireball.valfilter";
        const string pluginName = "ValFilter";
        const string pluginVersion = "0.2.1";

        private readonly Harmony HarmonyInstance = new Harmony(pluginGUID);

        public static ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource(pluginName);

        public static ConfigEntry<bool> ClearOnClose;
        public static ConfigEntry<float> SearchWidth;
        public static ConfigEntry<Vector2> SearchOffset;
        public static ConfigEntry<KeyCode> FocusKey;
        public static ConfigEntry<SearchMode> SearchMode;
        public static ConfigEntry<bool> TypoTolerance;

        public void Awake()
        {
            SearchMode = Config.Bind("Search", "Mode", ValFilter.SearchMode.Substring,
                "Substring: the name must contain exactly what you typed, in the game's usual order. " +
                "Fuzzy (opt-in): letters in order, not necessarily adjacent (\"bzarw\" finds Bronzehead arrow), best matches first.");
            TypoTolerance = Config.Bind("Search", "TypoTolerance", true,
                "Fuzzy mode only: also match words of 4+ letters with a typo or two (\"baer\" finds Bear). " +
                "Typo matches are listed after exact ones.");
            ClearOnClose = Config.Bind("General", "ClearOnClose", true,
                "Clear the search text whenever the inventory/crafting panel closes.");
            FocusKey = Config.Bind("General", "FocusKey", KeyCode.F,
                "Key that focuses the search box while the inventory is open. Set to None to disable.");
            SearchWidth = Config.Bind("Layout", "Width", 220f,
                "Width of the search box in UI units.");
            SearchOffset = Config.Bind("Layout", "Offset", new Vector2(40f, 0f),
                "Offset of the search box from the right edge of the Upgrade tab. The cloned field's key hint badge hangs off its left edge, so leave room for it.");

            HarmonyInstance.PatchAll(typeof(Main).Assembly);
            logger.LogInfo($"{pluginName} {pluginVersion} loaded");
        }

        public void OnDestroy()
        {
            HarmonyInstance.UnpatchSelf();
        }
    }

    public enum SearchMode
    {
        Fuzzy,
        Substring,
    }

    internal static class RecipeSearch
    {
        private static GuiInputField searchField;
        private static string term = "";
        private static FuzzyMatcher.Query query = new FuzzyMatcher.Query("");

        public static bool HasFocus => searchField != null && searchField.isFocused;

        /// Lazily builds the search box by cloning the build menu's search field, so it inherits
        /// the game's font, sprites and gamepad/virtual-keyboard handling.
        public static void EnsureCreated(InventoryGui gui)
        {
            if (searchField != null)
                return;

            var hud = Hud.instance;
            var template = hud != null && hud.m_buildUi != null
                ? Traverse.Create(hud.m_buildUi).Field<GuiInputField>("m_searchField").Value
                : null;
            if (template == null)
            {
                Main.logger.LogWarning("Could not find the build menu search field to clone; search box not created.");
                return;
            }

            // Anchor next to the Craft/Upgrade tabs, which sit in otherwise empty space above the recipe list.
            var tab = (RectTransform)gui.m_tabUpgrade.transform;
            var go = UnityEngine.Object.Instantiate(template.gameObject, tab.parent, false);
            go.name = "ValFilterSearch";
            go.SetActive(true);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = tab.anchorMin;
            rt.anchorMax = tab.anchorMin;
            rt.pivot = new Vector2(0f, tab.pivot.y);
            rt.sizeDelta = new Vector2(Main.SearchWidth.Value, tab.rect.height);
            float tabRight = tab.anchoredPosition.x + tab.rect.width * (1f - tab.pivot.x);
            rt.anchoredPosition = new Vector2(tabRight, tab.anchoredPosition.y) + Main.SearchOffset.Value;

            searchField = go.GetComponent<GuiInputField>();
            // Drop any serialized listeners carried over from the template.
            searchField.onValueChanged = new TMP_InputField.OnChangeEvent();
            searchField.text = term;
            searchField.onValueChanged.AddListener(OnSearchChanged);
        }

        /// Mirrors the build menu's behavior: pressing the focus key (F by default) jumps into the search box.
        /// Keyboard only; the left stick click the build menu also uses is multi-craft in the inventory.
        public static void HandleFocusKey()
        {
            var key = Main.FocusKey.Value;
            if (key == KeyCode.None || searchField == null || HasFocus || !searchField.gameObject.activeInHierarchy)
                return;
            if (!InventoryGui.IsVisible() || Console.IsVisible() || TextInput.IsVisible()
                || (Chat.instance != null && Chat.instance.HasFocus()))
                return;

            if (ZInput.GetKeyDown(key))
                searchField.ActivateInputField();
        }

        public static void Clear()
        {
            term = "";
            query = new FuzzyMatcher.Query("");
            if (searchField != null)
            {
                searchField.SetTextWithoutNotify("");
                searchField.DeactivateInputField();
            }
        }

        private static void OnSearchChanged(string value)
        {
            term = value.Trim();
            query = new FuzzyMatcher.Query(term);
            var gui = InventoryGui.instance;
            if (gui != null && InventoryGui.IsVisible())
                ApplyFilter(gui, reselect: true);
        }

        // Filtering works on the recipe buttons vanilla already built instead of rebuilding the list.
        // Rebuilding destroys and re-instantiates a button per recipe, which hitches badly with every
        // recipe unlocked; toggling and repositioning existing buttons is cheap.
        //
        // After vanilla's UpdateRecipeList we snapshot the full sorted m_availableRecipes, then narrow
        // m_availableRecipes to the matches so selection, clicks and gamepad navigation only see
        // visible entries. Before the next rebuild we restore the full list so vanilla destroys every
        // button, hidden ones included.

        private static readonly FieldInfo availableRecipesField = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
        private static readonly FieldInfo recipeListBaseSizeField = AccessTools.Field(typeof(InventoryGui), "m_recipeListBaseSize");
        private static readonly Type recipeDataPairType = AccessTools.Inner(typeof(InventoryGui), "RecipeDataPair");
        private static readonly PropertyInfo pairRecipeProp = AccessTools.Property(recipeDataPairType, "Recipe");
        private static readonly PropertyInfo pairElementProp = AccessTools.Property(recipeDataPairType, "InterfaceElement");
        private static readonly PropertyInfo pairCanCraftProp = AccessTools.Property(recipeDataPairType, "CanCraft");
        private static readonly MethodInfo getSelectedRecipeIndexMethod = AccessTools.Method(typeof(InventoryGui), "GetSelectedRecipeIndex");
        private static readonly MethodInfo setRecipeMethod = AccessTools.Method(typeof(InventoryGui), "SetRecipe");

        private struct Entry
        {
            public object Pair; // boxed InventoryGui.RecipeDataPair
            public GameObject Element;
            public Recipe Recipe;
            public bool CanCraft;
            public int Index; // position in vanilla's sorted list, the final tiebreaker
        }

        private struct Match
        {
            public Entry Entry;
            public int Score;
            public int NameLength;
        }

        private static readonly List<Entry> allEntries = new List<Entry>();
        private static readonly List<Match> matches = new List<Match>();
        // Localized item names keyed by token ($item_sword_bronze), so typing doesn't re-localize.
        private static readonly Dictionary<string, FuzzyMatcher.Candidate> candidateCache = new Dictionary<string, FuzzyMatcher.Candidate>();

        // Keep vanilla's craftable-first grouping, rank by score within each group, then prefer shorter
        // names ("Bow" over "Bow of the hunt"), then fall back to vanilla's own order.
        private static readonly Comparison<Match> byRank = (a, b) =>
        {
            int c = b.Entry.CanCraft.CompareTo(a.Entry.CanCraft);
            if (c == 0) c = b.Score.CompareTo(a.Score);
            if (c == 0) c = a.NameLength.CompareTo(b.NameLength);
            if (c == 0) c = a.Entry.Index.CompareTo(b.Entry.Index);
            return c;
        };

        /// Called before vanilla rebuilds the list: hand back every entry so vanilla destroys all buttons.
        public static void RestoreFullList(InventoryGui gui)
        {
            if (allEntries.Count == 0)
                return;

            var available = (IList)availableRecipesField.GetValue(gui);
            available.Clear();
            foreach (var entry in allEntries)
                available.Add(entry.Pair);
            allEntries.Clear();
        }

        /// Called after vanilla rebuilds the list: snapshot it, then apply the current filter.
        public static void CaptureAndFilter(InventoryGui gui)
        {
            allEntries.Clear();
            foreach (var pair in (IList)availableRecipesField.GetValue(gui))
            {
                allEntries.Add(new Entry
                {
                    Pair = pair,
                    Element = (GameObject)pairElementProp.GetValue(pair),
                    Recipe = (Recipe)pairRecipeProp.GetValue(pair),
                    CanCraft = (bool)pairCanCraftProp.GetValue(pair),
                    Index = allEntries.Count,
                });
            }

            // UpdateCraftingPanel picks the selected recipe itself right after UpdateRecipeList returns.
            if (!string.IsNullOrEmpty(term))
                ApplyFilter(gui, reselect: false);
        }

        private static void ApplyFilter(InventoryGui gui, bool reselect)
        {
            bool fuzzy = Main.SearchMode.Value == SearchMode.Fuzzy;

            matches.Clear();
            foreach (var entry in allEntries)
            {
                int score = Score(entry.Recipe, fuzzy, out int nameLength);
                entry.Element.SetActive(score > 0);
                if (score > 0)
                    matches.Add(new Match { Entry = entry, Score = score, NameLength = nameLength });
            }

            // Substring mode keeps vanilla's order, like the original filter.
            if (fuzzy && !query.IsEmpty)
                matches.Sort(byRank);

            var available = (IList)availableRecipesField.GetValue(gui);
            available.Clear();
            foreach (var match in matches)
            {
                ((RectTransform)match.Entry.Element.transform).anchoredPosition =
                    new Vector2(0f, available.Count * -gui.m_recipeListSpace);
                available.Add(match.Entry.Pair);
            }

            float baseSize = (float)recipeListBaseSizeField.GetValue(gui);
            float height = Mathf.Max(baseSize, available.Count * gui.m_recipeListSpace);
            gui.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            if (!reselect)
                return;

            gui.m_recipeListScroll.value = 1f; // back to the top of the list
            int index;
            if (available.Count == 0)
                index = -1;
            else if (fuzzy && !query.IsEmpty)
                index = 0; // best match, so typing then crafting just works
            else
                index = (int)getSelectedRecipeIndexMethod.Invoke(gui, new object[] { true });
            setRecipeMethod.Invoke(gui, new object[] { index, false });
        }

        /// 0 = filtered out; otherwise higher ranks earlier (fuzzy mode only).
        private static int Score(Recipe recipe, bool fuzzy, out int nameLength)
        {
            nameLength = 0;
            if (query.IsEmpty)
                return 1;

            var shared = recipe?.m_item?.m_itemData?.m_shared;
            if (shared == null)
                return 0;

            if (!candidateCache.TryGetValue(shared.m_name, out var candidate))
            {
                candidate = new FuzzyMatcher.Candidate(Localization.instance.Localize(shared.m_name));
                candidateCache[shared.m_name] = candidate;
            }
            nameLength = candidate.Text.Length;

            if (fuzzy)
            {
                int score = FuzzyMatcher.Score(query, candidate, Main.TypoTolerance.Value);
                if (score > 0)
                    return score;
            }
            else if (candidate.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }

            // Last resort: the internal token, e.g. "$item_bronzearrow" (handy if you know the English
            // name but play in another language). Ranked below everything else.
            return term.Length >= 3 && shared.m_name.IndexOf(query.Compact, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class InventoryGui_Show_Patch
    {
        private static void Prefix(InventoryGui __instance) => RecipeSearch.EnsureCreated(__instance);
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryGui_Update_Patch
    {
        private static void Postfix() => RecipeSearch.HandleFocusKey();
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class InventoryGui_Hide_Patch
    {
        private static void Postfix()
        {
            if (Main.ClearOnClose.Value)
                RecipeSearch.Clear();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGui_UpdateRecipeList_Patch
    {
        private static void Prefix(InventoryGui __instance) => RecipeSearch.RestoreFullList(__instance);

        private static void Postfix(InventoryGui __instance) => RecipeSearch.CaptureAndFilter(__instance);
    }

    /// The game suppresses hotkeys (inventory close, hotbar 1-8, movement, camera) while chat has focus.
    /// Piggyback on that so typing in the search box doesn't trigger them.
    [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
    internal static class Chat_HasFocus_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (RecipeSearch.HasFocus)
                __result = true;
        }
    }
}
