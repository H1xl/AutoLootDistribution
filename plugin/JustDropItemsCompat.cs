using System;
using System.Reflection;
using H1xl.Shared;
using RoR2;

namespace AutoLootDistribution
{
    /// <summary>
    /// Just Drop Items (com.H1xl.JustDropItems) tracks, per player and item, which copies came out of a printer or were rerolled
    /// by the Recycler; that decides how the item behaves once it is dropped again (Drifter's Tinker, the Recycler). It reads the
    /// marks when a pickup is picked up, which items given by this mod skip, so they are reported here. Soft dependency:
    /// found by reflection, nothing happens without the mod.
    /// </summary>
    internal static class JustDropItemsCompat
    {
        private static Action<CharacterMaster, ItemIndex, bool, bool> itemGranted;
        private static bool searched;

        /// <summary>Host only. Reports an item given to a player, with the flags of the pickup it came from.</summary>
        public static void Notify(CharacterMaster master, ItemIndex item, bool recycled, bool duplicated)
        {
            if ((!recycled && !duplicated) || !master || item == ItemIndex.None) return;
            var method = Find();
            if (method == null) return;
            try { method(master, item, recycled, duplicated); }
            catch (Exception e) { Log.Warn("JustDropItems.Api.ItemGranted: " + e.Message); }
        }

        // Looked up on the first call: all plugins are loaded by then, whatever the load order.
        private static Action<CharacterMaster, ItemIndex, bool, bool> Find()
        {
            if (searched) return itemGranted;
            searched = true;
            Type api = Type.GetType("JustDropItems.Api, JustDropItems");
            if (api == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    api = assembly.GetType("JustDropItems.Api", false);
                    if (api != null) break;
                }
            }
            MethodInfo method = api?.GetMethod("ItemGranted", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(CharacterMaster), typeof(ItemIndex), typeof(bool), typeof(bool) }, null);
            if (method == null || method.ReturnType != typeof(void)) return null;
            try
            {
                itemGranted = (Action<CharacterMaster, ItemIndex, bool, bool>)Delegate.CreateDelegate(
                    typeof(Action<CharacterMaster, ItemIndex, bool, bool>), method);
            }
            catch (Exception e)
            {
                Log.Warn("JustDropItems.Api.ItemGranted found but not usable: " + e.Message);
                return null;
            }
            Log.Info("Just Drop Items found: printed and recycled items are reported to it.");
            return itemGranted;
        }
    }
}
