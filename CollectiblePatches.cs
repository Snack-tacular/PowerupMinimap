using System;
using HarmonyLib;
using UnityEngine;

namespace PowerupMinimap
{
    /// <summary>
    /// Harmony patches that hook into collectible lifecycle events to register
    /// and unregister world-space blips with the MinimapBlipOverlay.
    /// In IL2CPP, virtual methods overridden in derived classes have distinct
    /// function pointers in the native vtable, so each derived class must be patched.
    /// </summary>
    public static class CollectiblePatches
    {
        // ── Spawn Patches ────────────────────────────────────────────────────

        [HarmonyPatch(typeof(CollectibleItemBase), nameof(CollectibleItemBase.Awake))]
        public static class CollectibleItemBase_Awake_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectibleItemBase __instance) => TryRegister(__instance);
        }

        [HarmonyPatch(typeof(CollectibleItemBase), nameof(CollectibleItemBase.OnNetworkSpawn))]
        public static class CollectibleItemBase_Spawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectibleItemBase __instance) => TryRegister(__instance);
        }

        [HarmonyPatch(typeof(CollectableItemHP), nameof(CollectableItemHP.OnNetworkSpawn))]
        public static class CollectableItemHP_Spawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectableItemHP __instance) => TryRegister(__instance);
        }

        [HarmonyPatch(typeof(CollectableItemBomb), nameof(CollectableItemBomb.OnNetworkSpawn))]
        public static class CollectableItemBomb_Spawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectableItemBomb __instance) => TryRegister(__instance);
        }

        [HarmonyPatch(typeof(CollectableItemMagnet), nameof(CollectableItemMagnet.OnNetworkSpawn))]
        public static class CollectableItemMagnet_Spawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectableItemMagnet __instance) => TryRegister(__instance);
        }

        [HarmonyPatch(typeof(CollectableItemBuff), nameof(CollectableItemBuff.OnNetworkSpawn))]
        public static class CollectableItemBuff_Spawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectableItemBuff __instance) => TryRegister(__instance);
        }

        [HarmonyPatch(typeof(CollectableItemBuilderUpgrade), nameof(CollectableItemBuilderUpgrade.OnNetworkSpawn))]
        public static class CollectableItemBuilderUpgrade_Spawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectableItemBuilderUpgrade __instance) => TryRegister(__instance);
        }

        // ── Despawn / Collection / Destruction Patches ────────────────────────

        [HarmonyPatch(typeof(CollectibleItemBase), nameof(CollectibleItemBase.OnNetworkDespawn))]
        public static class CollectibleItemBase_Despawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectibleItemBase __instance) => TryUnregister(__instance);
        }

        [HarmonyPatch(typeof(CollectableItemBomb), nameof(CollectableItemBomb.OnNetworkDespawn))]
        public static class CollectableItemBomb_Despawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectableItemBomb __instance) => TryUnregister(__instance);
        }

        [HarmonyPatch(typeof(CollectibleItemBase), nameof(CollectibleItemBase.StartCollect))]
        public static class CollectibleItemBase_StartCollect_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectibleItemBase __instance) => TryUnregister(__instance);
        }

        [HarmonyPatch(typeof(CollectibleItemBase), nameof(CollectibleItemBase.OnDestroy))]
        public static class CollectibleItemBase_OnDestroy_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectibleItemBase __instance) => TryUnregister(__instance);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static void TryRegister(CollectibleItemBase item)
        {
            try
            {
                if (item == null || item.gameObject == null) return;
                PickupCategory cat = ClassifyCollectible(item);
                if (!IsVisible(cat)) return;
                int id = item.gameObject.GetInstanceID();
                MinimapBlipOverlay.Register(id, item.transform, cat);
                PowerupMinimapPlugin.Log?.LogInfo($"[PowerupMinimap] Tracked {item.GetIl2CppType()?.Name} (ID {id}, category {cat}) at {item.transform.position}");
            }
            catch (Exception ex)
            {
                PowerupMinimapPlugin.Log?.LogError($"[PowerupMinimap] Register failed: {ex.Message}");
            }
        }

        public static void TryUnregister(CollectibleItemBase item)
        {
            try
            {
                if (item == null || item.gameObject == null) return;
                int id = item.gameObject.GetInstanceID();
                MinimapBlipOverlay.Unregister(id);
                PowerupMinimapPlugin.Log?.LogInfo($"[PowerupMinimap] Untracked (ID {id})");
            }
            catch { }
        }

        /// <summary>
        /// Classifies a CollectibleItemBase into a PickupCategory.
        /// </summary>
        public static PickupCategory ClassifyCollectible(CollectibleItemBase item)
        {
            if (item == null) return PickupCategory.Generic;

            if (item.TryCast<CollectableItemHP>() != null) return PickupCategory.HP;
            if (item.TryCast<CollectableItemBomb>() != null) return PickupCategory.Bomb;
            if (item.TryCast<CollectableItemMagnet>() != null) return PickupCategory.Magnet;
            if (item.TryCast<CollectableItemBuilderUpgrade>() != null) return PickupCategory.Buff;

            var buffItem = item.TryCast<CollectableItemBuff>();
            if (buffItem != null)
            {
                if (buffItem.buffDefinition != null)
                {
                    string bId = buffItem.buffDefinition.buffId ?? "";
                    string bName = buffItem.buffDefinition.displayName ?? "";
                    if (ContainsAny(bId, "Speed") || ContainsAny(bName, "Speed")) return PickupCategory.Speed;
                    if (ContainsAny(bId, "Exp", "XP") || ContainsAny(bName, "Exp", "XP")) return PickupCategory.ExpBoost;
                }
                return PickupCategory.Buff;
            }

            // Fallback: check type name and GameObject name
            string typeName = item.GetIl2CppType()?.Name ?? "";
            string goName = item.gameObject != null ? item.gameObject.name : "";

            if (ContainsAny(typeName, "HP", "Heal", "Health", "Potion", "DropHP") || ContainsAny(goName, "HP", "Heal", "Health", "Potion")) return PickupCategory.HP;
            if (ContainsAny(typeName, "Magnet") || ContainsAny(goName, "Magnet")) return PickupCategory.Magnet;
            if (ContainsAny(typeName, "Bomb", "Explosive") || ContainsAny(goName, "Bomb", "Explosive")) return PickupCategory.Bomb;
            if (ContainsAny(typeName, "Exp", "XP", "Experience") || ContainsAny(goName, "Exp", "XP", "Experience")) return PickupCategory.ExpBoost;
            if (ContainsAny(typeName, "Speed") || ContainsAny(goName, "Speed")) return PickupCategory.Speed;
            if (ContainsAny(typeName, "Buff", "Builder", "Attack", "Upgrade") || ContainsAny(goName, "Buff", "Builder", "Attack", "Upgrade")) return PickupCategory.Buff;
            if (ContainsAny(typeName, "Coin", "Currency", "Gold") || ContainsAny(goName, "Coin", "Currency", "Gold")) return PickupCategory.Generic;

            return PickupCategory.Generic;
        }

        private static bool ContainsAny(string source, params string[] tokens)
        {
            foreach (var t in tokens)
            {
                if (source.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
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
    }
}
