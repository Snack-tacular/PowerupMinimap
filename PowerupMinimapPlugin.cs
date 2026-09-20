using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PowerupMinimap
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class PowerupMinimapPlugin : BasePlugin
    {
        public static PowerupMinimapPlugin Instance { get; private set; } = null!;
        public new static ManualLogSource Log { get; private set; } = null!;

        // ── Config ────────────────────────────────────────────────────────────
        public static ConfigEntry<bool>  Enabled          { get; private set; } = null!;
        public static ConfigEntry<float> BlipSize         { get; private set; } = null!;
        public static ConfigEntry<float> PulseSpeed       { get; private set; } = null!;
        public static ConfigEntry<float> PulseScale       { get; private set; } = null!;

        // Per-type colour hex overrides (strings supported natively by BepInEx 6)
        public static ConfigEntry<string> ColorHP          { get; private set; } = null!;
        public static ConfigEntry<string> ColorBuff        { get; private set; } = null!;
        public static ConfigEntry<string> ColorBomb        { get; private set; } = null!;
        public static ConfigEntry<string> ColorMagnet      { get; private set; } = null!;
        public static ConfigEntry<string> ColorExpBoost    { get; private set; } = null!;
        public static ConfigEntry<string> ColorSpeed       { get; private set; } = null!;
        public static ConfigEntry<string> ColorChest       { get; private set; } = null!;
        public static ConfigEntry<string> ColorGeneric     { get; private set; } = null!;

        // Parsed Color cache for zero-allocation access during rendering
        public static Color ParsedColorHP       { get; private set; }
        public static Color ParsedColorBuff     { get; private set; }
        public static Color ParsedColorBomb     { get; private set; }
        public static Color ParsedColorMagnet   { get; private set; }
        public static Color ParsedColorExpBoost { get; private set; }
        public static Color ParsedColorSpeed    { get; private set; }
        public static Color ParsedColorChest    { get; private set; }
        public static Color ParsedColorGeneric  { get; private set; }

        // Filtering
        public static ConfigEntry<bool>  ShowHP           { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowBuff         { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowBomb         { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowMagnet       { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowExpBoost     { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowSpeed        { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowChest        { get; private set; } = null!;
        public static ConfigEntry<bool>  ShowGeneric      { get; private set; } = null!;

        private Harmony? _harmony;

        public override void Load()
        {
            Instance = this;
            Log = base.Log;

            BindConfig();

            try
            {
                // Register custom MonoBehaviour with IL2CPP runtime
                ClassInjector.RegisterTypeInIl2Cpp<MinimapBlipOverlay>();

                _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
                _harmony.PatchAll(typeof(PowerupMinimapPlugin).Assembly);

                // Instantiate persistent overlay GameObject
                var go = new GameObject("PowerupMinimapOverlay");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<MinimapBlipOverlay>();

                Log.LogInfo("Powerup Minimap (IL2CPP) loaded successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to initialize Powerup Minimap: " + ex);
            }
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return base.Unload();
        }

        private void BindConfig()
        {
            const string sGeneral    = "1 - General";
            const string sAppearance = "2 - Appearance";
            const string sColors     = "3 - Colors";
            const string sFilters    = "4 - Filters";

            Enabled       = Config.Bind(sGeneral,    "Enabled",     true,  "Enable or disable the entire mod.");
            BlipSize      = Config.Bind(sAppearance, "BlipSize",    8f,    "Radius in minimap pixels of each powerup blip.");
            PulseSpeed    = Config.Bind(sAppearance, "PulseSpeed",  2.5f,  "How fast the blips pulse (cycles per second). Set to 0 to disable pulsing.");
            PulseScale    = Config.Bind(sAppearance, "PulseScale",  0.35f, "Fractional size change during pulsing (0 = no pulse, 0.5 = ±50% size).");

            ColorHP       = Config.Bind(sColors, "ColorHP",       "#33E64D", "Hex colour for healing / HP pickups (e.g. #RRGGBB).");
            ColorBuff     = Config.Bind(sColors, "ColorBuff",     "#E69919", "Hex colour for buff pickups.");
            ColorBomb     = Config.Bind(sColors, "ColorBomb",     "#F24019", "Hex colour for bomb pickups.");
            ColorMagnet   = Config.Bind(sColors, "ColorMagnet",   "#8C4DF2", "Hex colour for magnet pickups.");
            ColorExpBoost = Config.Bind(sColors, "ColorExpBoost", "#19B3F2", "Hex colour for experience boost pickups.");
            ColorSpeed    = Config.Bind(sColors, "ColorSpeed",    "#F2F219", "Hex colour for speed buff pickups.");
            ColorChest    = Config.Bind(sColors, "ColorChest",    "#FFCC1A", "Hex colour for buff chest items.");
            ColorGeneric  = Config.Bind(sColors, "ColorGeneric",  "#CCCCCC", "Hex colour for any other pickup types.");

            UpdateParsedColors();

            ColorHP.SettingChanged       += (_, _) => UpdateParsedColors();
            ColorBuff.SettingChanged     += (_, _) => UpdateParsedColors();
            ColorBomb.SettingChanged     += (_, _) => UpdateParsedColors();
            ColorMagnet.SettingChanged   += (_, _) => UpdateParsedColors();
            ColorExpBoost.SettingChanged += (_, _) => UpdateParsedColors();
            ColorSpeed.SettingChanged    += (_, _) => UpdateParsedColors();
            ColorChest.SettingChanged    += (_, _) => UpdateParsedColors();
            ColorGeneric.SettingChanged  += (_, _) => UpdateParsedColors();

            ShowHP        = Config.Bind(sFilters, "ShowHP",       true,  "Show HP / healing pickups on the minimap.");
            ShowBuff      = Config.Bind(sFilters, "ShowBuff",     true,  "Show buff pickups on the minimap.");
            ShowBomb      = Config.Bind(sFilters, "ShowBomb",     true,  "Show bomb pickups on the minimap.");
            ShowMagnet    = Config.Bind(sFilters, "ShowMagnet",   true,  "Show magnet pickups on the minimap.");
            ShowExpBoost  = Config.Bind(sFilters, "ShowExpBoost", true,  "Show experience boost pickups on the minimap.");
            ShowSpeed     = Config.Bind(sFilters, "ShowSpeed",    true,  "Show speed buff pickups on the minimap.");
            ShowChest     = Config.Bind(sFilters, "ShowChest",    false, "Show buff chests on the minimap (default false as game has native icon).");
            ShowGeneric   = Config.Bind(sFilters, "ShowGeneric",  true,  "Show all other pickup types on the minimap.");
        }

        private static void UpdateParsedColors()
        {
            ParsedColorHP       = ParseColor(ColorHP.Value,       new Color(0.20f, 0.90f, 0.30f, 1f));
            ParsedColorBuff     = ParseColor(ColorBuff.Value,     new Color(0.90f, 0.60f, 0.10f, 1f));
            ParsedColorBomb     = ParseColor(ColorBomb.Value,     new Color(0.95f, 0.25f, 0.10f, 1f));
            ParsedColorMagnet   = ParseColor(ColorMagnet.Value,   new Color(0.55f, 0.30f, 0.95f, 1f));
            ParsedColorExpBoost = ParseColor(ColorExpBoost.Value, new Color(0.10f, 0.70f, 0.95f, 1f));
            ParsedColorSpeed    = ParseColor(ColorSpeed.Value,    new Color(0.95f, 0.95f, 0.10f, 1f));
            ParsedColorChest    = ParseColor(ColorChest.Value,    new Color(1.00f, 0.80f, 0.10f, 1f));
            ParsedColorGeneric  = ParseColor(ColorGeneric.Value,  new Color(0.80f, 0.80f, 0.80f, 1f));
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex.Trim().TrimStart('#');

            try
            {
                if (hex.Length == 6)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    return new Color(r / 255f, g / 255f, b / 255f, 1f);
                }
                if (hex.Length == 8)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    byte a = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                    return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                }
                if (hex.Length == 3)
                {
                    byte r = byte.Parse(new string(hex[0], 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(new string(hex[1], 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(new string(hex[2], 2), System.Globalization.NumberStyles.HexNumber);
                    return new Color(r / 255f, g / 255f, b / 255f, 1f);
                }
            }
            catch
            {
            }

            return fallback;
        }
    }
}
