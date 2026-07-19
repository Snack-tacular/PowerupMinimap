using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerupMinimap
{
    /// <summary>
    /// Category enum used to pick colour and label for each pickup.
    /// </summary>
    public enum PickupCategory
    {
        HP,
        Buff,
        Bomb,
        Magnet,
        ExpBoost,
        Speed,
        Chest,
        Generic
    }

    /// <summary>
    /// Tracks a single pickupable object: stores its world position and blip category.
    /// </summary>
    public sealed class PickupEntry
    {
        public Transform? Transform;
        public PickupCategory Category;
    }

    /// <summary>
    /// Singleton MonoBehaviour that owns the minimap blip overlay.
    /// It finds the game's MinimapController each scene, computes a world→minimap
    /// projection, and draws coloured circles for every tracked pickup.
    /// </summary>
    public sealed class MinimapBlipOverlay : MonoBehaviour
    {
        public static MinimapBlipOverlay? Instance { get; private set; }

        // Registry of live pickups — keyed by instance ID for fast lookup
        private static readonly Dictionary<int, PickupEntry> _pickups = new();
        private static readonly object _lock = new object();

        // ── Minimap reference cache ───────────────────────────────────────────
        private MinimapController? _minimapController;
        private RectTransform?     _mapImageRT;        // The moving map image RectTransform
        private RectTransform?     _maskRT;            // The visible minimap mask RectTransform

        // ── Blip textures ─────────────────────────────────────────────────────
        private Texture2D? _blipTex;                   // Solid circle sprite

        // ── Pulse state ───────────────────────────────────────────────────────
        private float _pulsePhase = 0f;

        // ── IMGUI label style ─────────────────────────────────────────────────
        private GUIStyle? _labelStyle;

        // ─────────────────────────────────────────────────────────────────────
        // Static registry helpers (called from patches on any thread)
        // ─────────────────────────────────────────────────────────────────────

        public static void Register(int id, Transform transform, PickupCategory category)
        {
            lock (_lock)
            {
                _pickups[id] = new PickupEntry { Transform = transform, Category = category };
            }
        }

        public static void Unregister(int id)
        {
            lock (_lock)
            {
                _pickups.Remove(id);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _blipTex = MakeCircleTexture(64);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_blipTex != null) Destroy(_blipTex);
        }

        private void Update()
        {
            if (!PowerupMinimapPlugin.Enabled.Value) return;

            // Refresh minimap reference every update (it might be recreated on scene load)
            RefreshMinimapRef();

            // Advance pulse animation
            float ps = PowerupMinimapPlugin.PulseSpeed.Value;
            if (ps > 0f) _pulsePhase = (_pulsePhase + Time.deltaTime * ps * Mathf.PI * 2f) % (Mathf.PI * 2f);

            // Prune stale entries whose GameObject was destroyed
            lock (_lock)
            {
                var toRemove = new List<int>();
                foreach (var kv in _pickups)
                    if (kv.Value.Transform == null) toRemove.Add(kv.Key);
                foreach (var id in toRemove) _pickups.Remove(id);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // IMGUI rendering — draws directly on top of the minimap widget
        // ─────────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!PowerupMinimapPlugin.Enabled.Value) return;
            if (_mapImageRT == null || _blipTex == null) return;

            // Determine active view state and mask RectTransform
            RectTransform? activeMaskRT = _maskRT;
            bool isCircular = false;

            if (_minimapController != null)
            {
                var t = _minimapController.GetType();
                var fState = HarmonyLib.AccessTools.Field(t, "_viewState");
                string stateStr = fState?.GetValue(_minimapController)?.ToString() ?? "";

                if (stateStr == "Unfolded")
                {
                    var fFrameUnfolded = HarmonyLib.AccessTools.Field(t, "frameUnfolded");
                    if (fFrameUnfolded != null)
                    {
                        var goUnfolded = fFrameUnfolded.GetValue(_minimapController) as GameObject;
                        if (goUnfolded != null && goUnfolded.activeInHierarchy)
                        {
                            var rtUnfolded = goUnfolded.GetComponent<RectTransform>();
                            if (rtUnfolded != null) activeMaskRT = rtUnfolded;
                        }
                    }
                }
                else
                {
                    // Only apply circular mask if not unfolded
                    var fCirc = HarmonyLib.AccessTools.Field(t, "circularMask");
                    if (fCirc != null)
                    {
                        isCircular = (bool)fCirc.GetValue(_minimapController);
                    }
                }
            }

            if (activeMaskRT == null) return;

            // Calculate GUI rect of the active minimap mask window
            Rect maskRect = GUIRectOf(activeMaskRT);
            if (maskRect.width < 1f || maskRect.height < 1f) return;

            // Collect positions
            List<(Vector3 world, PickupCategory cat)> entries;
            lock (_lock)
            {
                entries = new List<(Vector3, PickupCategory)>(_pickups.Count);
                foreach (var kv in _pickups)
                {
                    if (kv.Value.Transform == null) continue;
                    entries.Add((kv.Value.Transform.position, kv.Value.Category));
                }
            }

            if (entries.Count == 0) return;

            // We need world→minimap bounds. Get them from the MinimapController.
            if (!TryGetWorldBounds(out float worldMinX, out float worldMinZ, out float worldMaxX, out float worldMaxZ))
                return;

            float worldW = worldMaxX - worldMinX;
            float worldH = worldMaxZ - worldMinZ;
            if (worldW < 0.001f || worldH < 0.001f) return;

            // Compute pulse multiplier
            float pulse = 1f + Mathf.Sin(_pulsePhase) * PowerupMinimapPlugin.PulseScale.Value;
            float baseSize = PowerupMinimapPlugin.BlipSize.Value * pulse;

            if (_labelStyle == null) BuildLabelStyle();

            // Get the camera of the canvas so we can project to screen space correctly
            Canvas canvas = _mapImageRT.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null) ? canvas.worldCamera : null;

            GUI.BeginClip(maskRect);

            foreach (var (worldPos, cat) in entries)
            {
                if (!IsVisible(cat)) continue;

                // 1. Map world coordinates to UV on the scrollable map image
                float u = (worldPos.x - worldMinX) / worldW;
                float v = (worldPos.z - worldMinZ) / worldH;

                // 2. Map UV to local coordinates of the scrollable map image
                float localX = (u - _mapImageRT.pivot.x) * _mapImageRT.rect.width;
                float localY = (v - _mapImageRT.pivot.y) * _mapImageRT.rect.height;

                // 3. Convert local coordinates to UI world space (canvas space)
                Vector3 uiWorldPoint = _mapImageRT.TransformPoint(new Vector3(localX, localY, 0f));

                // 4. Convert UI world space to screen space (pixels)
                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, uiWorldPoint);

                // 5. Convert screen space to IMGUI-clip local space
                // IMGUI is Y-down, screenPos is Y-up
                float px = screenPos.x - maskRect.xMin;
                float py = (Screen.height - screenPos.y) - maskRect.yMin;

                // If circular mask is enabled, discard blips outside the circle radius
                if (isCircular)
                {
                    float dx = px - maskRect.width * 0.5f;
                    float dy = py - maskRect.height * 0.5f;
                    float r = maskRect.width * 0.5f;
                    if (dx * dx + dy * dy > r * r) continue;
                }
                else
                {
                    // For square/rectangular mask, manually clip to ensure they don't draw outside clip bounds
                    if (px < 0f || px > maskRect.width || py < 0f || py > maskRect.height)
                        continue;
                }

                Color col = GetColor(cat);
                DrawBlip(px, py, baseSize, col, cat);
            }

            GUI.EndClip();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshMinimapRef()
        {
            if (_minimapController == null)
            {
                _minimapController = UnityEngine.Object.FindAnyObjectByType<MinimapController>();
                _mapImageRT = null;
                _maskRT = null;
            }

            if (_minimapController == null) return;

            if (_mapImageRT == null || _maskRT == null)
            {
                var t = _minimapController.GetType();
                var fMap = HarmonyLib.AccessTools.Field(t, "mapImageRect");
                if (fMap != null) _mapImageRT = fMap.GetValue(_minimapController) as RectTransform;

                var fMask = HarmonyLib.AccessTools.Field(t, "maskRect");
                if (fMask != null) _maskRT = fMask.GetValue(_minimapController) as RectTransform;

                // Fallbacks
                if (_mapImageRT == null) _mapImageRT = FindMinimapRect(_minimapController);
                if (_maskRT == null) _maskRT = _minimapController.GetComponent<RectTransform>();
            }
        }

        private static RectTransform? FindMinimapRect(MinimapController ctrl)
        {
            // Walk the hierarchy looking for the actual map image panel
            foreach (var name in new[] { "MinimapImage", "MapImage", "Minimap", "Map", "MinimapPanel" })
            {
                var t = ctrl.transform.Find(name);
                if (t != null)
                {
                    var rt = t.GetComponent<RectTransform>();
                    if (rt != null) return rt;
                }
            }
            // Fall back to the controller's own RT (or first child)
            var selfRt = ctrl.GetComponent<RectTransform>();
            if (selfRt != null) return selfRt;

            if (ctrl.transform.childCount > 0)
            {
                var childRt = ctrl.transform.GetChild(0).GetComponent<RectTransform>();
                if (childRt != null) return childRt;
            }
            return null;
        }

        private bool TryGetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = minZ = maxX = maxZ = 0f;

            if (_minimapController != null)
            {
                var ctrl = _minimapController;
                var t = ctrl.GetType();

                // Strategy 1: Use game's own _minWorld and _maxWorld fields
                var fMin = HarmonyLib.AccessTools.Field(t, "_minWorld");
                var fMax = HarmonyLib.AccessTools.Field(t, "_maxWorld");
                if (fMin != null && fMax != null)
                {
                    var minV = fMin.GetValue(ctrl);
                    var maxV = fMax.GetValue(ctrl);
                    if (minV is Vector2 min2 && maxV is Vector2 max2 && Mathf.Abs(max2.x - min2.x) > 1f)
                    {
                        minX = min2.x;
                        minZ = min2.y; // Vector2.y maps to World Z
                        maxX = max2.x;
                        maxZ = max2.y; // Vector2.y maps to World Z
                        return true;
                    }
                }

                // Look for common field names
                foreach (var fname in new[] { "worldBounds", "mapBounds", "bounds", "_bounds", "WorldBounds", "MapBounds" })
                {
                    var f = HarmonyLib.AccessTools.Field(t, fname);
                    if (f != null)
                    {
                        object val = f.GetValue(ctrl);
                        if (val is Bounds b && b.size.magnitude > 0.1f)
                        {
                            minX = b.min.x; minZ = b.min.z;
                            maxX = b.max.x; maxZ = b.max.z;
                            return true;
                        }
                        if (val is Rect r && r.width > 0.1f)
                        {
                            minX = r.xMin; minZ = r.yMin;
                            maxX = r.xMax; maxZ = r.yMax;
                            return true;
                        }
                    }
                }

                // Look for a minimap camera and derive from its orthographic size
                foreach (var fname in new[] { "minimapCamera", "_minimapCamera", "mapCamera", "_camera", "Camera" })
                {
                    var f = HarmonyLib.AccessTools.Field(t, fname);
                    if (f == null) continue;
                    var cam = f.GetValue(ctrl) as Camera;
                    if (cam != null && cam.orthographic)
                    {
                        float s = cam.orthographicSize;
                        Vector3 p = cam.transform.position;
                        float aspect = cam.aspect > 0f ? cam.aspect : 1f;
                        minX = p.x - s * aspect;
                        maxX = p.x + s * aspect;
                        minZ = p.z - s;
                        maxZ = p.z + s;
                        return true;
                    }
                }
            }

            // Strategy 2: use all terrain bounds
            var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
            if (terrains != null && terrains.Length > 0)
            {
                minX = float.MaxValue; minZ = float.MaxValue;
                maxX = float.MinValue; maxZ = float.MinValue;
                foreach (var ter in terrains)
                {
                    if (ter == null || ter.terrainData == null) continue;
                    Vector3 tp = ter.transform.position;
                    Vector3 ts = ter.terrainData.size;
                    minX = Mathf.Min(minX, tp.x);
                    minZ = Mathf.Min(minZ, tp.z);
                    maxX = Mathf.Max(maxX, tp.x + ts.x);
                    maxZ = Mathf.Max(maxZ, tp.z + ts.z);
                }
                if (maxX > minX && maxZ > minZ) return true;
            }

            // Strategy 3: derive bounds from all known pickup positions + a generous pad
            lock (_lock)
            {
                if (_pickups.Count == 0) return false;
                minX = float.MaxValue; minZ = float.MaxValue;
                maxX = float.MinValue; maxZ = float.MinValue;
                foreach (var kv in _pickups)
                {
                    if (kv.Value.Transform == null) continue;
                    Vector3 p = kv.Value.Transform.position;
                    minX = Mathf.Min(minX, p.x);
                    minZ = Mathf.Min(minZ, p.z);
                    maxX = Mathf.Max(maxX, p.x);
                    maxZ = Mathf.Max(maxZ, p.z);
                }
                float pad = 50f;
                minX -= pad; minZ -= pad;
                maxX += pad; maxZ += pad;
                return maxX > minX && maxZ > minZ;
            }
        }

        private void DrawBlip(float cx, float cy, float size, Color col, PickupCategory cat)
        {
            float r = size * 0.5f;
            var rect = new Rect(cx - r, cy - r, size, size);

            // Outer glow (larger, more transparent)
            Color glow = col;
            glow.a *= 0.35f;
            GUI.color = glow;
            GUI.DrawTexture(new Rect(cx - r * 1.8f, cy - r * 1.8f, size * 1.8f, size * 1.8f), _blipTex!);

            // Inner filled circle
            GUI.color = col;
            GUI.DrawTexture(rect, _blipTex!);

            // Category letter label
            if (_labelStyle != null && size >= 6f)
            {
                GUI.color = Color.white;
                string letter = CategoryLetter(cat);
                _labelStyle.fontSize = Mathf.RoundToInt(size * 0.7f);
                GUI.Label(new Rect(cx - r, cy - r, size, size), letter, _labelStyle);
            }

            GUI.color = Color.white;
        }

        private static string CategoryLetter(PickupCategory cat) => cat switch
        {
            PickupCategory.HP       => "+",
            PickupCategory.Buff     => "B",
            PickupCategory.Bomb     => "💣",
            PickupCategory.Magnet   => "M",
            PickupCategory.ExpBoost => "E",
            PickupCategory.Speed    => "S",
            PickupCategory.Chest    => "C",
            _                       => "?"
        };

        private static Color GetColor(PickupCategory cat) => cat switch
        {
            PickupCategory.HP       => PowerupMinimapPlugin.ColorHP.Value,
            PickupCategory.Buff     => PowerupMinimapPlugin.ColorBuff.Value,
            PickupCategory.Bomb     => PowerupMinimapPlugin.ColorBomb.Value,
            PickupCategory.Magnet   => PowerupMinimapPlugin.ColorMagnet.Value,
            PickupCategory.ExpBoost => PowerupMinimapPlugin.ColorExpBoost.Value,
            PickupCategory.Speed    => PowerupMinimapPlugin.ColorSpeed.Value,
            PickupCategory.Chest    => PowerupMinimapPlugin.ColorChest.Value,
            _                       => PowerupMinimapPlugin.ColorGeneric.Value,
        };

        private static bool IsVisible(PickupCategory cat) => cat switch
        {
            PickupCategory.HP       => PowerupMinimapPlugin.ShowHP.Value,
            PickupCategory.Buff     => PowerupMinimapPlugin.ShowBuff.Value,
            PickupCategory.Bomb     => PowerupMinimapPlugin.ShowBomb.Value,
            PickupCategory.Magnet   => PowerupMinimapPlugin.ShowMagnet.Value,
            PickupCategory.ExpBoost => PowerupMinimapPlugin.ShowExpBoost.Value,
            PickupCategory.Speed    => PowerupMinimapPlugin.ShowSpeed.Value,
            PickupCategory.Chest    => PowerupMinimapPlugin.ShowChest.Value,
            _                       => PowerupMinimapPlugin.ShowGeneric.Value,
        };

        // Convert a RectTransform to a screen-space Rect for IMGUI use.
        private static Rect GUIRectOf(RectTransform rt)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null) ? canvas.worldCamera : null;

            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Vector2 screen0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 screen2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

            float xMin = screen0.x;
            float yMin = screen0.y;
            float xMax = screen2.x;
            float yMax = screen2.y;

            // Unity GUI is top-left origin; screen is bottom-left
            float guiY = Screen.height - yMax;
            return new Rect(xMin, guiY, xMax - xMin, yMax - yMin);
        }

        // Build a circular texture for the blip sprite
        private static Texture2D MakeCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            float half = size * 0.5f;
            float r = half - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half + 0.5f;
                    float dy = y - half + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((r - dist) / 1.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private void BuildLabelStyle()
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize  = 8,
            };
            _labelStyle.normal.textColor = Color.white;
        }
    }
}
