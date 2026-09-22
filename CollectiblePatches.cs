using System;
using UnityEngine;

namespace PowerupMinimap
{
    /// <summary>
    /// Helper methods for classifying and registering collectible pickup items
    /// with MinimapBlipOverlay without dangerous native Harmony detours.
    /// </summary>
    public static class CollectiblePatches
    {
        public static void TryRegister(CollectibleItemBase item)
        {
            try
            {
                if (item == null || item.gameObject == null) return;
                if (item.collected || item.IsCollected) return;

                PickupCategory cat = ClassifyCollectible(item);
                if (!IsVisible(cat)) return;

                int id = item.gameObject.GetInstanceID();
                MinimapBlipOverlay.Register(id, item.transform, cat);
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

        public static bool IsVisible(PickupCategory cat) => cat switch
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
