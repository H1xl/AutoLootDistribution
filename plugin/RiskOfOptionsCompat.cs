using System.Runtime.CompilerServices;
using H1xl.Shared;

namespace AutoLootDistribution
{
    /// <summary>
    /// In-game settings page for Risk Of Options. Called only when that mod is installed (<see cref="RooPage"/>), so
    /// its types are never loaded otherwise. The options edit the same config entries as the file, so changes apply
    /// at once.
    /// </summary>
    internal static class RiskOfOptionsCompat
    {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Register(string pluginFolder)
        {
            RooHelper.Page(
                "Items from kills (Artifact of Sacrifice), chests, shrines, printers and scrappers fly to the player who earned them. " +
                "Only the host's settings matter.", pluginFolder);

            // Same order as the config sections: General, Delivery, Recipients, the source groups, Item filter, Fallback, Debug.
            RooHelper.CheckBox(Settings.enabled);

            RooHelper.CheckBox(Settings.animation);
            RooHelper.Slider(Settings.travelTime, 0.1f, 5f, 0.1f, "{0:0.0} s", () => !Settings.animation.Value);
            RooHelper.CheckBox(Settings.notify);
            RooHelper.CheckBox(Settings.commandToPlayer);
            RooHelper.CheckBox(Settings.commandIcons);
            RooHelper.CheckBox(Settings.autoPickupChoices);

            RooHelper.CheckBox(Settings.allowDead);
            RooHelper.CheckBox(Settings.ignoreDisconnected);

            // One header per source group.
            foreach (Settings.SourceSetting s in Settings.Sources)
            {
                RooHelper.Choice(s.mode);
                if (s.source == Source.WanderingChef)
                    RooHelper.CheckBox(Settings.chefProtect, () => s.mode.Value == DistributionMode.Disabled);
            }

            RooHelper.Text(Settings.tiers);
            RooHelper.CheckBox(Settings.skipObjective);
            RooHelper.CheckBox(Settings.listEnabled);
            RooHelper.CheckBox(Settings.listIgnoredSolo, ListOff);
            RooHelper.Choice(Settings.listMode, ListOff);
            RooHelper.Text(Settings.itemList, ListOff);

            RooHelper.Choice(Settings.fallback);

            RooHelper.CheckBox(Settings.verbose);
            RooHelper.CheckBox(Settings.duplicatorSkip);
        }

        private static bool ListOff() => !Settings.listEnabled.Value;
    }
}
