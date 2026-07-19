using System;
using HarmonyLib;
using UnityEngine;

namespace PowerupMinimap
{
    /// <summary>
    /// Harmony patches that hook into the collectible/pickup lifecycle to register
    /// and unregister world-space blips with the MinimapBlipOverlay.
    /// </summary>
    internal static class CollectiblePatches
    {
        // ─────────────────────────────────────────────────────────────────────
        // CollectibleItemBase — base class for all ground-spawn pickups
        // ─────────────────────────────────────────────────────────────────────

        [HarmonyPatch(typeof(CollectibleItemBase), "OnNetworkSpawn")]
        [HarmonyPostfix]
        private static void CollectibleItemBase_OnNetworkSpawn(CollectibleItemBase __instance)
        {
            TryRegister(__instance);
        }

        [HarmonyPatch(typeof(CollectibleItemBase), "OnNetworkDespawn")]
        [HarmonyPostfix]
        private static void CollectibleItemBase_OnNetworkDespawn(CollectibleItemBase __instance)
        {
            TryUnregister(__instance);
        }

        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void TryRegister(CollectibleItemBase item)
        {
            try
            {
                if (item == null) return;
                EnsureOverlay();
                PickupCategory cat = ClassifyCollectible(item);
                if (!IsVisible(cat)) return;
                int id = (int)item.gameObject.GetEntityId();
                MinimapBlipOverlay.Register(id, item.transform, cat);
            }
            catch (Exception ex)
            {
                PowerupMinimapPlugin.Log.LogDebug($"[PowerupMinimap] Register failed: {ex.Message}");
            }
        }

        private static void TryUnregister(CollectibleItemBase item)
        {
            try
            {
                if (item == null) return;
                MinimapBlipOverlay.Unregister((int)item.gameObject.GetEntityId());
            }
            catch { }
        }

        private static void RegisterGO(GameObject go, PickupCategory cat)
        {
            try
            {
                if (go == null) return;
                EnsureOverlay();
                if (!IsVisible(cat)) return;
                MinimapBlipOverlay.Register((int)go.GetEntityId(), go.transform, cat);
            }
            catch (Exception ex)
            {
                PowerupMinimapPlugin.Log.LogDebug($"[PowerupMinimap] RegisterGO failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Classify a CollectibleItemBase into a PickupCategory by type name (and full hierarchy).
        /// We avoid GetComponent&lt;T&gt; for game types because their namespaces are opaque;
        /// instead we walk the component list and match by class name string.
        /// </summary>
        private static PickupCategory ClassifyCollectible(CollectibleItemBase item)
        {
            // Build a combined string from the full type hierarchy for matching
            string typeName = item.GetType().Name;

            // Quick pass on the primary type name
            if (ContainsAny(typeName, "HP", "Heal", "Health", "Potion", "DropHP"))  return PickupCategory.HP;
            if (ContainsAny(typeName, "Magnet"))                                     return PickupCategory.Magnet;
            if (ContainsAny(typeName, "Bomb", "Explosive"))                          return PickupCategory.Bomb;
            if (ContainsAny(typeName, "Exp", "XP", "Experience"))                   return PickupCategory.ExpBoost;
            if (ContainsAny(typeName, "Speed"))                                      return PickupCategory.Speed;
            if (ContainsAny(typeName, "Buff", "Builder", "Attack", "Upgrade"))      return PickupCategory.Buff;
            if (ContainsAny(typeName, "Coin", "Currency", "Gold"))                  return PickupCategory.Generic;

            // Component-level check using name-based reflection (avoids compile-time type dependency)
            foreach (var comp in item.GetComponents<UnityEngine.Component>())
            {
                if (comp == null) continue;
                string cn = comp.GetType().Name;
                if (ContainsAny(cn, "HP", "Heal", "Health", "Potion"))              return PickupCategory.HP;
                if (ContainsAny(cn, "Magnet"))                                       return PickupCategory.Magnet;
                if (ContainsAny(cn, "Bomb", "Explosive"))                            return PickupCategory.Bomb;
                if (ContainsAny(cn, "Exp", "XP", "Experience"))                     return PickupCategory.ExpBoost;
                if (ContainsAny(cn, "Speed"))                                        return PickupCategory.Speed;
                if (ContainsAny(cn, "Attack", "Buff", "Upgrade"))                   return PickupCategory.Buff;
            }

            return PickupCategory.Generic;
        }

        private static bool ContainsAny(string source, params string[] tokens)
        {
            foreach (var t in tokens)
                if (source.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

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

        // ─── Overlay singleton guard ──────────────────────────────────────────

        private static void EnsureOverlay()
        {
            if (MinimapBlipOverlay.Instance != null) return;
            var go = new GameObject("PowerupMinimapOverlay");
            go.AddComponent<MinimapBlipOverlay>();
        }
    }
}
