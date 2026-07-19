using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PowerupMinimap
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class PowerupMinimapPlugin : BaseUnityPlugin
    {
        public static PowerupMinimapPlugin Instance { get; private set; } = null!;
        internal static ManualLogSource Log { get; private set; } = null!;

        // ── Config ────────────────────────────────────────────────────────────
        public static ConfigEntry<bool>  Enabled          { get; private set; } = null!;
        public static ConfigEntry<float> BlipSize         { get; private set; } = null!;
        public static ConfigEntry<float> PulseSpeed       { get; private set; } = null!;
        public static ConfigEntry<float> PulseScale       { get; private set; } = null!;

        // Per-type colour overrides
        public static ConfigEntry<Color> ColorHP          { get; private set; } = null!;
        public static ConfigEntry<Color> ColorBuff        { get; private set; } = null!;
        public static ConfigEntry<Color> ColorBomb        { get; private set; } = null!;
        public static ConfigEntry<Color> ColorMagnet      { get; private set; } = null!;
        public static ConfigEntry<Color> ColorExpBoost    { get; private set; } = null!;
        public static ConfigEntry<Color> ColorSpeed       { get; private set; } = null!;
        public static ConfigEntry<Color> ColorChest       { get; private set; } = null!;
        public static ConfigEntry<Color> ColorGeneric     { get; private set; } = null!;

        // Filtering
        public static ConfigEntry<bool>  ShowHP           { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowBuff         { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowBomb         { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowMagnet       { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowExpBoost     { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowSpeed        { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowChest        { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowGeneric      { get; private set; } = null!;

        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance = this;
            Log = base.Logger;

            BindConfig();

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(CollectiblePatches));

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo("Powerup Minimap loaded successfully!");
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                // Ensure overlay is initialized when a scene loads
                var go = GameObject.Find("PowerupMinimapOverlay");
                if (go == null)
                {
                    go = new GameObject("PowerupMinimapOverlay");
                    go.AddComponent<MinimapBlipOverlay>();
                }
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Error in OnSceneLoaded: {ex}");
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony.UnpatchSelf();
        }

        private void BindConfig()
        {
            const string sGeneral  = "1 - General";
            const string sAppearance = "2 - Appearance";
            const string sColors   = "3 - Colors";
            const string sFilters  = "4 - Filters";

            Enabled       = Config.Bind(sGeneral,    "Enabled",     true,  "Enable or disable the entire mod.");
            BlipSize      = Config.Bind(sAppearance, "BlipSize",    8f,    "Radius in minimap pixels of each powerup blip.");
            PulseSpeed    = Config.Bind(sAppearance, "PulseSpeed",  2.5f,  "How fast the blips pulse (cycles per second). Set to 0 to disable pulsing.");
            PulseScale    = Config.Bind(sAppearance, "PulseScale",  0.35f, "Fractional size change during pulsing (0 = no pulse, 0.5 = ±50% size).");

            ColorHP       = Config.Bind(sColors, "ColorHP",       new Color(0.20f, 0.90f, 0.30f, 1f), "Colour for healing / HP pickups.");
            ColorBuff     = Config.Bind(sColors, "ColorBuff",     new Color(0.90f, 0.60f, 0.10f, 1f), "Colour for buff pickups.");
            ColorBomb     = Config.Bind(sColors, "ColorBomb",     new Color(0.95f, 0.25f, 0.10f, 1f), "Colour for bomb pickups.");
            ColorMagnet   = Config.Bind(sColors, "ColorMagnet",   new Color(0.55f, 0.30f, 0.95f, 1f), "Colour for magnet pickups.");
            ColorExpBoost = Config.Bind(sColors, "ColorExpBoost", new Color(0.10f, 0.70f, 0.95f, 1f), "Colour for experience boost pickups.");
            ColorSpeed    = Config.Bind(sColors, "ColorSpeed",    new Color(0.95f, 0.95f, 0.10f, 1f), "Colour for speed buff pickups.");
            ColorChest    = Config.Bind(sColors, "ColorChest",    new Color(1.00f, 0.80f, 0.10f, 1f), "Colour for buff chest items.");
            ColorGeneric  = Config.Bind(sColors, "ColorGeneric",  new Color(0.80f, 0.80f, 0.80f, 1f), "Colour for any other pickup types.");

            ShowHP        = Config.Bind(sFilters, "ShowHP",       true, "Show HP / healing pickups on the minimap.");
            ShowBuff      = Config.Bind(sFilters, "ShowBuff",     true, "Show buff pickups on the minimap.");
            ShowBomb      = Config.Bind(sFilters, "ShowBomb",     true, "Show bomb pickups on the minimap.");
            ShowMagnet    = Config.Bind(sFilters, "ShowMagnet",   true, "Show magnet pickups on the minimap.");
            ShowExpBoost  = Config.Bind(sFilters, "ShowExpBoost", true, "Show experience boost pickups on the minimap.");
            ShowSpeed     = Config.Bind(sFilters, "ShowSpeed",    true, "Show speed buff pickups on the minimap.");
            ShowChest     = Config.Bind(sFilters, "ShowChest",    true, "Show buff chests on the minimap.");
            ShowGeneric   = Config.Bind(sFilters, "ShowGeneric",  true, "Show all other pickup types on the minimap.");
        }

    }
}
