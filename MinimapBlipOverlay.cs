using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

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
    /// IL2CPP-compatible singleton MonoBehaviour for the minimap blip overlay.
    /// Accesses MinimapController properties directly with zero reflection overhead.
    /// </summary>
    public sealed class MinimapBlipOverlay : MonoBehaviour
    {
        public MinimapBlipOverlay(IntPtr ptr) : base(ptr) { }

        public static MinimapBlipOverlay? Instance { get; private set; }

        // ── Registry ──────────────────────────────────────────────────────────
        private static readonly Dictionary<int, PickupEntry> _pickups = new();
        private static readonly object _lock = new object();

        // ── Minimap references ────────────────────────────────────────────────
        private MinimapController? _minimapController;
        private RectTransform?     _mapImageRT;
        private RectTransform?     _maskRT;
        private Canvas?            _canvas;
        private Camera?            _canvasCam;

        // ── Cached stable values (refreshed periodically) ───────────────────
        private float _worldMinX, _worldMinZ, _worldMaxX, _worldMaxZ;
        private bool  _boundsValid;
        private bool  _loggedBounds;
        private bool  _isCircular;
        private float _boundsRefreshTimer;
        private const float BoundsRefreshInterval = 1.0f;

        // ── Periodic scan timer for collectibles ─────────────────────────────
        private float _scanTimer;
        private const float ScanInterval = 0.2f;

        // ── Per-frame draw list (reused, zero heap alloc) ─────────────────────
        private readonly List<(Vector3 world, PickupCategory cat)> _drawList = new(64);

        // ── Blip texture ──────────────────────────────────────────────────────
        private Texture2D? _blipTex;

        // ── Pulse ─────────────────────────────────────────────────────────────
        private float _pulsePhase;

        // ── IMGUI label style ─────────────────────────────────────────────────
        private GUIStyle? _labelStyle;

        // ── Stale-entry pruning throttle ──────────────────────────────────────
        private float _pruneTimer;
        private const float PruneInterval = 0.5f;
        private readonly List<int> _removeBuffer = new(8);

        // ── Reusable corner buffer for GUIRectOf ──────────────────────────────
        private static readonly Il2CppStructArray<Vector3> _cornersBuffer = new(4);

        // ─────────────────────────────────────────────────────────────────────
        // Static registry helpers
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

            _blipTex = MakeCircleTexture(64);
            SceneManager.sceneLoaded += (UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= (UnityAction<Scene, LoadSceneMode>)OnSceneLoaded;
            if (Instance == this) Instance = null;
            if (_blipTex != null) Destroy(_blipTex);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            PowerupMinimapPlugin.Log?.LogInfo($"[PowerupMinimap] Scene loaded: {scene.name}");
            _minimapController = null;
            _mapImageRT = null;
            _maskRT = null;
            _canvas = null;
            _canvasCam = null;
            _boundsValid = false;
            _loggedBounds = false;
            _boundsRefreshTimer = BoundsRefreshInterval;

            lock (_lock)
            {
                _pickups.Clear();
            }
        }

        private void Update()
        {
            if (!PowerupMinimapPlugin.Enabled.Value) return;

            float dt = Time.deltaTime;

            // Pulse animation
            float ps = PowerupMinimapPlugin.PulseSpeed.Value;
            if (ps > 0f) _pulsePhase = (_pulsePhase + dt * ps * Mathf.PI * 2f) % (Mathf.PI * 2f);

            // Re-acquire minimap controller and rects
            RefreshMinimapRef();

            // Periodic scan for existing/dropped items in scene
            _scanTimer += dt;
            if (_scanTimer >= ScanInterval)
            {
                _scanTimer = 0f;
                ScanExistingCollectibles();
            }

            // Throttled stale-entry prune
            _pruneTimer += dt;
            if (_pruneTimer >= PruneInterval)
            {
                _pruneTimer = 0f;
                lock (_lock)
                {
                    _removeBuffer.Clear();
                    foreach (var kv in _pickups)
                    {
                        if (kv.Value.Transform == null || !kv.Value.Transform.gameObject.activeInHierarchy)
                            _removeBuffer.Add(kv.Key);
                    }
                    foreach (var id in _removeBuffer) _pickups.Remove(id);
                }
            }

            // Throttled world bounds refresh
            _boundsRefreshTimer += dt;
            if (_boundsRefreshTimer >= BoundsRefreshInterval)
            {
                _boundsRefreshTimer = 0f;
                _boundsValid = TryGetWorldBounds(out _worldMinX, out _worldMinZ, out _worldMaxX, out _worldMaxZ);

                if (_boundsValid && !_loggedBounds)
                {
                    _loggedBounds = true;
                    PowerupMinimapPlugin.Log?.LogInfo($"[PowerupMinimap] Map bounds confirmed: min=({_worldMinX:F1}, {_worldMinZ:F1}) max=({_worldMaxX:F1}, {_worldMaxZ:F1})");
                }

                if (_minimapController != null)
                {
                    _isCircular = _minimapController.circularMask;
                }
            }
        }

        private void ScanExistingCollectibles()
        {
            try
            {
                var items = UnityEngine.Object.FindObjectsByType<CollectibleItemBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (items != null && items.Length > 0)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var item = items[i];
                        if (item != null && item.gameObject != null && !item.collected && !item.IsCollected)
                        {
                            CollectiblePatches.TryRegister(item);
                        }
                    }
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // IMGUI rendering
        // ─────────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!PowerupMinimapPlugin.Enabled.Value) return;
            if (_mapImageRT == null || _blipTex == null) return;
            if (!_boundsValid) return;

            float worldW = _worldMaxX - _worldMinX;
            float worldH = _worldMaxZ - _worldMinZ;
            if (worldW < 0.001f || worldH < 0.001f) return;

            // Check folded vs unfolded state
            RectTransform? activeMaskRT = _maskRT;
            bool isCircular = _isCircular;

            if (_minimapController != null)
            {
                if (_minimapController._viewState == MinimapController.MinimapViewState.Unfolded)
                {
                    var goUnfolded = _minimapController.frameUnfolded;
                    if (goUnfolded != null && goUnfolded.activeInHierarchy)
                    {
                        var rtU = goUnfolded.GetComponent<RectTransform>();
                        if (rtU != null)
                        {
                            activeMaskRT = rtU;
                            isCircular = false;
                        }
                    }
                }
            }

            if (activeMaskRT == null) return;

            Rect maskRect = GUIRectOf(activeMaskRT);
            if (maskRect.width < 1f || maskRect.height < 1f) return;

            // Pulse scale
            float pulse    = 1f + Mathf.Sin(_pulsePhase) * PowerupMinimapPlugin.PulseScale.Value;
            float baseSize = PowerupMinimapPlugin.BlipSize.Value * pulse;

            if (_labelStyle == null) BuildLabelStyle();

            // Snapshot active pickups (reused list, zero allocation)
            _drawList.Clear();
            lock (_lock)
            {
                foreach (var kv in _pickups)
                {
                    if (kv.Value.Transform == null || !kv.Value.Transform.gameObject.activeInHierarchy) continue;
                    _drawList.Add((kv.Value.Transform.position, kv.Value.Category));
                }
            }

            if (_drawList.Count == 0) return;

            float circCX = maskRect.width  * 0.5f;
            float circCY = maskRect.height * 0.5f;
            float circR2 = isCircular ? (circCX * circCX) : 0f;

            GUI.BeginClip(maskRect);

            foreach (var (worldPos, cat) in _drawList)
            {
                if (!IsVisible(cat)) continue;

                float u = (worldPos.x - _worldMinX) / worldW;
                float v = (worldPos.z - _worldMinZ) / worldH;

                float localX = (u - _mapImageRT.pivot.x) * _mapImageRT.rect.width;
                float localY = (v - _mapImageRT.pivot.y) * _mapImageRT.rect.height;

                Vector3 uiWorld = _mapImageRT.TransformPoint(new Vector3(localX, localY, 0f));
                Vector2 screen  = RectTransformUtility.WorldToScreenPoint(_canvasCam, uiWorld);

                float px = screen.x - maskRect.xMin;
                float py = (Screen.height - screen.y) - maskRect.yMin;

                if (isCircular)
                {
                    float dx = px - circCX, dy = py - circCY;
                    if (dx * dx + dy * dy > circR2) continue;
                }
                else
                {
                    if (px < 0f || px > maskRect.width || py < 0f || py > maskRect.height) continue;
                }

                DrawBlip(px, py, baseSize, GetColor(cat), cat);
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
                _minimapController = UnityEngine.Object.FindAnyObjectByType<MinimapController>(FindObjectsInactive.Include);
                if (_minimapController == null) return;

                PowerupMinimapPlugin.Log?.LogInfo("[PowerupMinimap] Found MinimapController in scene!");
                _mapImageRT = null;
                _maskRT     = null;
                _canvas     = null;
                _canvasCam  = null;
                _boundsRefreshTimer = BoundsRefreshInterval;
            }

            if (_mapImageRT == null || _maskRT == null)
            {
                _mapImageRT = _minimapController.mapImageRect;
                _maskRT     = _minimapController.maskRect;

                if (_mapImageRT == null) _mapImageRT = FindMinimapRect(_minimapController);
                if (_maskRT     == null) _maskRT     = _minimapController.GetComponent<RectTransform>();

                if (_mapImageRT != null && _maskRT != null)
                {
                    PowerupMinimapPlugin.Log?.LogInfo($"[PowerupMinimap] Acquired mapImageRect ({_mapImageRT.name}) and maskRect ({_maskRT.name})");
                }
            }

            if (_canvas == null && _mapImageRT != null)
            {
                _canvas    = _mapImageRT.GetComponentInParent<Canvas>();
                _canvasCam = (_canvas != null) ? _canvas.worldCamera : null;
            }
        }

        private static RectTransform? FindMinimapRect(MinimapController ctrl)
        {
            foreach (var name in new[] { "MinimapImage", "MapImage", "Minimap", "Map", "MinimapPanel" })
            {
                var t = ctrl.transform.Find(name);
                if (t != null)
                {
                    var rt = t.GetComponent<RectTransform>();
                    if (rt != null) return rt;
                }
            }
            var self = ctrl.GetComponent<RectTransform>();
            if (self != null) return self;
            if (ctrl.transform.childCount > 0)
            {
                var child = ctrl.transform.GetChild(0).GetComponent<RectTransform>();
                if (child != null) return child;
            }
            return null;
        }

        private bool TryGetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = minZ = maxX = maxZ = 0f;

            if (_minimapController != null)
            {
                Vector2 mn = _minimapController._minWorld;
                Vector2 mx = _minimapController._maxWorld;
                if (Mathf.Abs(mx.x - mn.x) > 1f && Mathf.Abs(mx.y - mn.y) > 1f)
                {
                    minX = mn.x; minZ = mn.y;
                    maxX = mx.x; maxZ = mx.y;
                    return true;
                }
            }

            // Fallback: active terrain
            var terrain = Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 tp = terrain.transform.position;
                Vector3 ts = terrain.terrainData.size;
                minX = tp.x; minZ = tp.z;
                maxX = tp.x + ts.x; maxZ = tp.z + ts.z;
                return true;
            }

            // Fallback: from registered pickups
            lock (_lock)
            {
                if (_pickups.Count == 0) return false;
                minX = float.MaxValue; minZ = float.MaxValue;
                maxX = float.MinValue; maxZ = float.MinValue;
                foreach (var kv in _pickups)
                {
                    if (kv.Value.Transform == null) continue;
                    Vector3 p = kv.Value.Transform.position;
                    minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z);
                    maxX = Mathf.Max(maxX, p.x); maxZ = Mathf.Max(maxZ, p.z);
                }
                float pad = 50f;
                minX -= pad; minZ -= pad; maxX += pad; maxZ += pad;
                return maxX > minX && maxZ > minZ;
            }
        }

        private void DrawBlip(float cx, float cy, float size, Color col, PickupCategory cat)
        {
            float r = size * 0.5f;

            // Outer glow
            Color glow = col; glow.a *= 0.35f;
            GUI.color = glow;
            GUI.DrawTexture(new Rect(cx - r * 1.8f, cy - r * 1.8f, size * 1.8f, size * 1.8f), _blipTex!);

            // Inner circle
            GUI.color = col;
            GUI.DrawTexture(new Rect(cx - r, cy - r, size, size), _blipTex!);

            // Letter label
            if (_labelStyle != null && size >= 6f)
            {
                GUI.color = Color.white;
                _labelStyle.fontSize = Mathf.RoundToInt(size * 0.7f);
                GUI.Label(new Rect(cx - r, cy - r, size, size), CategoryLetter(cat), _labelStyle);
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
            PickupCategory.HP       => PowerupMinimapPlugin.ParsedColorHP,
            PickupCategory.Buff     => PowerupMinimapPlugin.ParsedColorBuff,
            PickupCategory.Bomb     => PowerupMinimapPlugin.ParsedColorBomb,
            PickupCategory.Magnet   => PowerupMinimapPlugin.ParsedColorMagnet,
            PickupCategory.ExpBoost => PowerupMinimapPlugin.ParsedColorExpBoost,
            PickupCategory.Speed    => PowerupMinimapPlugin.ParsedColorSpeed,
            PickupCategory.Chest    => PowerupMinimapPlugin.ParsedColorChest,
            _                       => PowerupMinimapPlugin.ParsedColorGeneric,
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

        private static Rect GUIRectOf(RectTransform rt)
        {
            Canvas? c = rt.GetComponentInParent<Canvas>();
            Camera? cam = (c != null) ? c.worldCamera : null;

            rt.GetWorldCorners(_cornersBuffer);
            Vector2 s0 = RectTransformUtility.WorldToScreenPoint(cam, _cornersBuffer[0]);
            Vector2 s2 = RectTransformUtility.WorldToScreenPoint(cam, _cornersBuffer[2]);

            return new Rect(s0.x, Screen.height - s2.y, s2.x - s0.x, s2.y - s0.y);
        }

        private static Texture2D MakeCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            float half = size * 0.5f, r = half - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half + 0.5f, dy = y - half + 0.5f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((r - Mathf.Sqrt(dx * dx + dy * dy)) / 1.5f)));
                }
            }
            tex.Apply();
            return tex;
        }

        private void BuildLabelStyle()
        {
            _labelStyle = new GUIStyle()
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize  = 8,
            };
            _labelStyle.normal.textColor = Color.white;
        }
    }
}
