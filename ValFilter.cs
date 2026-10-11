using System;
using System.Collections.Generic;
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
        const string pluginVersion = "0.1.0";

        private readonly Harmony HarmonyInstance = new Harmony(pluginGUID);

        public static ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource(pluginName);

        public static ConfigEntry<bool> ClearOnClose;
        public static ConfigEntry<float> SearchWidth;
        public static ConfigEntry<Vector2> SearchOffset;
        public static ConfigEntry<KeyCode> FocusKey;

        public void Awake()
        {
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

    internal static class RecipeSearch
    {
        private static GuiInputField searchField;
        private static string term = "";

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
                Main.logger.LogWarning("Could not find the build menu search searchField to clone; search box not created.");
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
            if (searchField != null)
            {
                searchField.SetTextWithoutNotify("");
                searchField.DeactivateInputField();
            }
        }

        private static void OnSearchChanged(string value)
        {
            term = value.Trim();
            var gui = InventoryGui.instance;
            if (gui != null && InventoryGui.IsVisible())
                Traverse.Create(gui).Method("UpdateCraftingPanel", false).GetValue();
        }

        public static void Filter(List<Recipe> recipes)
        {
            if (string.IsNullOrEmpty(term))
                return;

            recipes.RemoveAll(r => !Matches(r));
        }

        private static bool Matches(Recipe recipe)
        {
            var shared = recipe?.m_item?.m_itemData?.m_shared;
            if (shared == null)
                return false;

            string name = Localization.instance.Localize(shared.m_name);
            return name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                || shared.m_name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
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
        private static void Prefix(List<Recipe> recipes) => RecipeSearch.Filter(recipes);
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
