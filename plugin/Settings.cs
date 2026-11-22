using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using H1xl.Shared;
using RoR2;

namespace AutoLootDistribution
{
    /// <summary>Where a dropped pickup came from. Set by the source hooks, read by the droplet hook.</summary>
    public enum Source : byte
    {
        None,
        MonsterKill, SonorousWhispers, Mimic, SolusExtractor, CraterKeys,
        Chest, LunarPod, VoidCradle, RustedLockbox, EncrustedLockbox, AdaptiveChest, ScavengerBag,
        ShrineOfChance, CleansingPool, CharityNode,
        Printer, Scrapper, DroneScrapper, Multishop, Cauldron, LunarShop, WanderingChef,
        Boss, Wake, TotemOfSacrifice,
        Drifter, JunkDrone,
        Unknown,
        /// <summary>Drops the mod must never touch, not even as Unknown (a vanilla "choose one" drop). Has no setting.</summary>
        Ignored,
        Count,
    }

    /// <summary>Fallback methods: the same as <see cref="DistributionMode"/> without Owner.</summary>
    public enum FallbackMode { Disabled, Nearest, Random, RoundRobin, Balanced }

    public enum ListMode { Exclude, IncludeOnly }

    /// <summary>
    /// Config entries plus cached copies of their values. The droplet hook reads only the cached
    /// fields and the precomputed per-item tables.
    /// </summary>
    internal static class Settings
    {
        // Items that are good only for some survivors. English or internal names; case, spaces and punctuation are ignored.
        private const string DefaultItemList =
            "Bustling Fungus, Rejuvenation Rack, Spare Drone Parts, Box of Dynamite, Lysate Cell, Shatterspleen, " +
            "Rose Buckler, Ignition Tank, H3AD-5T v2, Purity, Collector's Compulsion";

        private const string DefaultTiers = "Tier1, Tier2, Tier3, Boss";

        internal static ConfigEntry<bool> duplicatorSkip;
        public static bool DuplicatorSkip;
        internal static ConfigEntry<bool> enabled, verbose, animation, notify, commandToPlayer, commandIcons, autoPickupChoices, allowDead, ignoreDisconnected;
        internal static ConfigEntry<float> travelTime;
        /// <summary>One drop source: its config entries and where they live.</summary>
        internal sealed class SourceSetting
        {
            public Source source;
            public string section, key, oldKey;
            public DistributionMode defaultMode;
            public string description;
            public ConfigEntry<DistributionMode> mode;
        }

        // Sub-sections of the sources. Risk Of Options shows each section as its own header.
        private const string SMonsters = "4.1 Sources - Monsters", SChests = "4.2 Sources - Chests", SShrines = "4.3 Sources - Shrines",
                             SShops = "4.4 Sources - Shops and crafting", SEvents = "4.5 Sources - Events",
                             SMade = "4.6 Sources - Survivors and drones", SUnknown = "4.7 Sources - Unknown";
        // Before dev-3 (see docs/DEVLOG.md) all sources were in one section, with fallbacks in another; their values are carried over.
        private const string OldSources = "4. Sources", OldFallbacks = "6. Fallbacks";

        internal static readonly List<SourceSetting> Sources = new List<SourceSetting>();
        internal static ConfigEntry<bool> skipObjective, listEnabled, listIgnoredSolo;
        internal static ConfigEntry<ListMode> listMode;
        internal static ConfigEntry<FallbackMode> fallback;
        internal static ConfigEntry<bool> chefProtect;
        public static bool ChefProtect;
        internal static ConfigEntry<string> itemList, tiers;

        public static bool Enabled, Verbose, Animation, Notify, CommandToPlayer, CommandIcons, AutoPickupChoices, AllowDead, IgnoreDisconnected;
        public static float TravelTime;
        private static bool ignoreListSolo;
        private static readonly DistributionMode[] modeBySource = new DistributionMode[(int)Source.Count];
        private static DistributionMode fallbackMethod;

        // allowedItem[(int)itemIndex]: the item may be distributed. allowedItemNoList: the same without the item list
        // (single player with IgnoreItemListInSingleplayer, and command cubes, where the player picks the item).
        private static bool[] allowedItem = Array.Empty<bool>();
        private static bool[] allowedItemNoList = Array.Empty<bool>();

        public static DistributionMode ModeFor(Source s) => modeBySource[(int)s];
        /// <summary>One fallback for all sources.</summary>
        public static DistributionMode Fallback => fallbackMethod;

        public static bool IsTierAllowed(ItemIndex index) => Lookup(allowedItemNoList, index);

        // Hex items and quest/key items: Substandard Duplicator must not copy them (see DuplicatorSkipsHexAndKeyItems).
        private static bool[] noDuplicatorCopy = Array.Empty<bool>();
        public static bool BlocksDuplicatorCopy(ItemIndex index) => DuplicatorSkip && Lookup(noDuplicatorCopy, index);

        public static bool IsItemAllowed(ItemIndex index) =>
            Lookup(ignoreListSolo && PlayerCharacterMasterController.instances.Count <= 1 ? allowedItemNoList : allowedItem, index);

        private static bool Lookup(bool[] table, ItemIndex index)
        {
            int i = (int)index;
            return (uint)i < (uint)table.Length && table[i];
        }

        public static void Init(ConfigFile cfg)
        {
            const string G = "1. General", D = "2. Delivery", R = "3. Recipients", F = "5. Item filter", FB = "6. Fallback", Dbg = "7. Debug";
            config = cfg;

            enabled = cfg.Bind(G, "Enabled", true,
                "Turns the mod on or off. Host only. Full descriptions of all settings: README.md in the mod folder or on the Thunderstore page.");

            animation = cfg.Bind(D, "FlyingItemAnimation", true,
                "Items fly to the player as an icon, like items taken by Mithrix or Hex of Greed. If off, they are given instantly.");
            travelTime = cfg.Bind(D, "AnimationDuration", 1f,
                new ConfigDescription("Flight time of the icon, in seconds.", new AcceptableValueRange<float>(0.1f, 5f)));
            notify = cfg.Bind(D, "PickupNotification", true, "Show the pickup popup and chat line, and run pickup effects.");
            commandToPlayer = cfg.Bind(D, "MoveCommandCubesToPlayer", true,
                "Artifact of Command: move the command cube to the player's feet. If off, it stays where it dropped.");
            commandIcons = cfg.Bind(D, "CommandCubeIcons", true,
                "Artifact of Command: show flying command cubes as cube icons in their tier colour. Works for you if you have the mod; " +
                "players without it see Scrap icons.");
            autoPickupChoices = cfg.Bind(D, "AutoPickupChoices", true,
                "The item chosen from a command cube, Void Potential or other \"choose one\" orb flies to the player who chose it " +
                "instead of dropping on the ground. Equipment still drops.");

            allowDead = cfg.Bind(R, "GiveToDeadPlayers", true, "Dead players can receive items.");
            ignoreDisconnected = cfg.Bind(R, "SkipDisconnectedPlayers", true, "Players who left the game receive nothing.");

            const string killer = " Owner = the killer (minions count for their owner).", opener = " Owner = the player who opened it.",
                         user = " Owner = the player who used it.", noOwner = " No owner: use another method or a fallback.";
            AddSource(SMonsters, Source.MonsterKill, "MonsterKills", "MonsterKills", DistributionMode.Owner,
                "Items dropped by killed monsters (Artifact of Sacrifice)." + killer);
            AddSource(SMonsters, Source.SonorousWhispers, "SonorousWhispers", "MonsterKills", DistributionMode.Owner,
                "Items dropped by Sonorous Whispers (bosses and elites)." + killer);
            AddSource(SMonsters, Source.CraterKeys, "CraterKeys", null, DistributionMode.Owner,
                "Sentry Keys, Prison Matrices and Ocular Batteries dropped by monsters in the Repurposed Crater events. " +
                "They are quest items: distributed only with SkipObjectiveItems off and their tier in Tiers." + killer);
            AddSource(SMonsters, Source.Mimic, "Mimics", null, DistributionMode.Owner,
                "The chest reward a killed Mimic drops (Hex of Mimicry). Stolen items always go back to their owners by themselves." + killer);
            AddSource(SMonsters, Source.SolusExtractor, "SolusExtractors", null, DistributionMode.Owner,
                "Temporary copies of stolen items a killed Solus Extractor drops. The item filter isn't used. " +
                "Owner = the player it stole from.");
            AddSource(SChests, Source.Chest, "Chests", "Chests", DistributionMode.Owner,
                "Small, large, legendary, category and cloaked chests, and other chests not listed below." + opener);
            AddSource(SChests, Source.LunarPod, "LunarPods", "Chests", DistributionMode.Disabled, "Lunar pods." + opener);
            AddSource(SChests, Source.VoidCradle, "VoidCradles", "Chests", DistributionMode.Disabled, "Void cradles." + opener);
            AddSource(SChests, Source.RustedLockbox, "RustedLockboxes", "Chests", DistributionMode.Owner, "Rusted Key lockboxes." + opener);
            AddSource(SChests, Source.EncrustedLockbox, "EncrustedLockboxes", "Chests", DistributionMode.Disabled, "Encrusted Key lockboxes." + opener);
            AddSource(SChests, Source.AdaptiveChest, "AdaptiveChests", "Chests", DistributionMode.Owner, "Adaptive Chests." + opener);
            AddSource(SChests, Source.ScavengerBag, "ScavengerBags", "ScavengerBags", DistributionMode.Disabled,
                "Scavenger bags (also the lunar Scavenger's)." + opener);
            AddSource(SShrines, Source.ShrineOfChance, "ShrinesOfChance", "ShrinesOfChance", DistributionMode.Owner, "Shrines of Chance." + user);
            AddSource(SShrines, Source.CleansingPool, "CleansingPools", "PrintersScrappersShops", DistributionMode.Owner, "Cleansing Pools." + user);
            AddSource(SShrines, Source.CharityNode, "CharityNodes", "PrintersScrappersShops", DistributionMode.Owner, "Charity Nodes." + user);
            AddSource(SShops, Source.Printer, "Printers", "PrintersScrappersShops", DistributionMode.Owner, "3D printers." + user);
            AddSource(SShops, Source.Scrapper, "Scrappers", "PrintersScrappersShops", DistributionMode.Owner, "Scrappers." + user);
            AddSource(SShops, Source.DroneScrapper, "DroneScrappers", null, DistributionMode.Owner,
                "Drone Scrappers (scrap for a scrapped drone)." + user);
            AddSource(SShops, Source.Multishop, "Multishops", "PrintersScrappersShops", DistributionMode.Owner,
                "Multishop terminals, Shipping Request Form deliveries and other shop terminals." + user);
            AddSource(SShops, Source.Cauldron, "Cauldrons", "PrintersScrappersShops", DistributionMode.Owner, "Cauldrons." + user);
            AddSource(SShops, Source.LunarShop, "BazaarLunarShop", "PrintersScrappersShops", DistributionMode.Disabled,
                "Lunar shop in the Bazaar Between Time." + user);
            AddSource(SShops, Source.WanderingChef, "WanderingChef", null, DistributionMode.Owner,
                "Dishes cooked by the Wandering CHEF. The item filter isn't used: the player chose the recipe." + user);
            chefProtect = cfg.Bind(SShops, "WanderingChefProtectFromCorruption", true,
                "Wandering CHEF: if the recipient has the void item that would corrupt the cooked item, it stays on the ground " +
                "so it can be passed to another player. If off, it is always given.");
            AddSource(SEvents, Source.Boss, "BossRewards", "BossRewards", DistributionMode.Disabled, "Teleporter and other boss rewards." + noOwner);
            AddSource(SEvents, Source.Wake, "TheWake", "OtherEventRewards", DistributionMode.Disabled, "Rewards of The Wake." + noOwner);
            AddSource(SEvents, Source.TotemOfSacrifice, "TotemOfSacrifice", "OtherEventRewards", DistributionMode.Disabled,
                "Rewards of the Totem of Sacrifice altar." + noOwner);
            AddSource(SMade, Source.Drifter, "Drifter", null, DistributionMode.Owner,
                "Temporary items the Drifter makes (Salvage, and items turned into scrap by Tinker). " +
                "The item filter isn't used: the Drifter made them. Owner = that Drifter.");
            AddSource(SMade, Source.JunkDrone, "JunkDrone", null, DistributionMode.Owner,
                "Temporary items dropped by a Junk Drone's Surprise!. Owner = the player who owns the drone.");
            AddSource(SUnknown, Source.Unknown, "UnknownSources", null, DistributionMode.Disabled,
                "Any other dropped item: game events and objects not listed above, items dropped by other mods. " +
                "The item filter still applies; choose-one orbs, Artifact of Delusion and Command choices are never touched." + noOwner);
            tiers = cfg.Bind(F, "Tiers", DefaultTiers,
                "Comma-separated item tiers that may be distributed. Game tiers: Tier1 (white), Tier2 (green), Tier3 (red), Boss (yellow), " +
                "Lunar, VoidTier1, VoidTier2, VoidTier3, VoidBoss, FoodTier, Cursed, Page, Hex, NoTier.");
            skipObjective = cfg.Bind(F, "SkipObjectiveItems", false,
                "Leave quest and key items on the ground: Artifact Key, Prison Matrix, Sentry Key, Ocular Battery, " +
                "Encrypted Cerebellum, Exposed Cerebellum, Peace Flower.");
            listEnabled = cfg.Bind(F, "ItemListEnabled", true, "Use the item list.");
            listIgnoredSolo = cfg.Bind(F, "IgnoreItemListInSingleplayer", true, "Don't use the item list in single player.");
            listMode = cfg.Bind(F, "ItemListMode", ListMode.Exclude,
                "Exclude = listed items stay on the ground. IncludeOnly = only listed items are distributed.");
            itemList = cfg.Bind(F, "ItemList", DefaultItemList, "Comma-separated item names (English or internal).");

            fallback = cfg.Bind(FB, "Fallback", FallbackMode.Disabled,
                "For every source: used when its method finds nobody (the owner is dead or left, or the source has no owner). " +
                "Disabled = leave on the ground.");
            verbose = cfg.Bind(Dbg, "VerboseLogging", true, "Log every distributed item: source, recipient and method.");
            // Changes to the game's own rules, off by default.
            duplicatorSkip = cfg.Bind(Dbg, "DuplicatorSkipsHexAndKeyItems", false,
                "Changes the game's behaviour: Substandard Duplicator gives no temporary copy for Hex items and quest/key items " +
                "(Sentry Key, Prison Matrix, Ocular Battery and others), whether they are picked up from the ground or given by this mod.");
            MigrateOldSources(cfg);

            // Changes from the file or from Risk Of Options apply immediately. The item tables are rebuilt only
            // when an item filter setting changed.
            cfg.SettingChanged += (_, e) =>
            {
                CacheValues();
                if (e.ChangedSetting.Definition.Section == F) RebuildItemTable();
            };
            CacheValues();
        }

        private static void CacheValues()
        {
            Enabled = enabled.Value;
            Verbose = verbose.Value;
            Animation = animation.Value;
            TravelTime = travelTime.Value;
            Notify = notify.Value;
            CommandToPlayer = commandToPlayer.Value;
            CommandIcons = commandIcons.Value;
            AutoPickupChoices = autoPickupChoices.Value;
            DuplicatorSkip = duplicatorSkip.Value;
            AllowDead = allowDead.Value;
            IgnoreDisconnected = ignoreDisconnected.Value;
            ignoreListSolo = listIgnoredSolo.Value;

            foreach (SourceSetting s in Sources)
            {
                modeBySource[(int)s.source] = s.mode.Value;
            }
            fallbackMethod = ToMode(fallback.Value);
            ChefProtect = chefProtect.Value;
        }

        private static ConfigFile config;

        private static void AddSource(string section, Source source, string key, string oldKey, DistributionMode defaultMode, string description)
        {
            var s = new SourceSetting
            {
                source = source, section = section, key = key, oldKey = oldKey, defaultMode = defaultMode, description = description,
            };
            s.mode = config.Bind(section, key, defaultMode, description + " Disabled = leave on the ground.");
            Sources.Add(s);
        }

        /// <summary>
        /// Carries the values of the pre-dev-3 combined settings ("4. Sources" and "6. Fallbacks") over to the split ones,
        /// then drops the old lines from the file. Runs only for a config written by an older version.
        /// </summary>
        private static void MigrateOldSources(ConfigFile cfg)
        {
            try
            {
                PropertyInfo prop = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(prop?.GetValue(cfg) is Dictionary<ConfigDefinition, string> orphans) || orphans.Count == 0) return;

                bool migrated = false;
                foreach (SourceSetting s in Sources)
                {
                    if (s.oldKey == null) continue;
                    if (orphans.TryGetValue(new ConfigDefinition(OldSources, s.oldKey), out string mode) &&
                        Enum.TryParse(mode.Trim(), out DistributionMode m))
                    {
                        s.mode.Value = m;
                        migrated = true;
                    }
                }

                // Old per-source fallbacks ("6. Fallbacks", or "<Source>Fallback" in the source sections of a test build):
                // the single fallback takes the value most of them used, ignoring Disabled ones.
                var stale = new List<ConfigDefinition>();
                var votes = new Dictionary<FallbackMode, int>();
                foreach (KeyValuePair<ConfigDefinition, string> o in orphans)
                {
                    ConfigDefinition d = o.Key;
                    bool oldFallback = d.Section == OldFallbacks || (d.Section.StartsWith("4.") && d.Key.EndsWith("Fallback"));
                    // "4.6 Sources - Unknown" moved to 4.7 in a test build.
                    // Also settings a test build had elsewhere: "4.6 Sources - Unknown" (now 4.7), the Duplicator one and
                    // VerboseLogging (both now in Debug; VerboseLogging takes the new default, not the old value).
                    if (d.Section == OldSources || oldFallback || d.Section == "4.6 Sources - Unknown" ||
                        (d.Section == "2. Delivery" && d.Key == "DuplicatorSkipsHexAndKeyItems") ||
                        (d.Section == "1. General" && d.Key == "VerboseLogging")) stale.Add(d);
                    if (oldFallback && Enum.TryParse(o.Value.Trim(), out FallbackMode f) && f != FallbackMode.Disabled)
                        votes[f] = votes.TryGetValue(f, out int n) ? n + 1 : 1;
                }
                if (votes.Count > 0)
                {
                    FallbackMode best = FallbackMode.Disabled;
                    int bestVotes = 0;
                    foreach (KeyValuePair<FallbackMode, int> v in votes)
                        if (v.Value > bestVotes) { best = v.Key; bestVotes = v.Value; }
                    fallback.Value = best;
                    migrated = true;
                }
                foreach (ConfigDefinition d in stale) orphans.Remove(d);
                if (stale.Count > 0) cfg.Save();
                if (migrated) Log.Info("Config: the source settings from an older version were split per source; their values were kept.");
            }
            catch (Exception e)
            {
                Log.Warn("Config: couldn't carry over the old source settings, the new ones use their defaults. " + e.Message);
            }
        }

        private static DistributionMode ToMode(FallbackMode f)
        {
            switch (f)
            {
                case FallbackMode.Nearest: return DistributionMode.Nearest;
                case FallbackMode.Random: return DistributionMode.Random;
                case FallbackMode.RoundRobin: return DistributionMode.RoundRobin;
                case FallbackMode.Balanced: return DistributionMode.Balanced;
                default: return DistributionMode.Disabled;
            }
        }

        /// <summary>Builds the per-item tables. Runs at game load and when a filter setting changes, never per drop.</summary>
        public static void RebuildItemTable()
        {
            if (ItemCatalog.itemCount == 0) return; // catalog not ready yet; called again from RoR2Application.onLoad

            var tierNames = ItemFilter.ParseList(tiers.Value);
            var listed = ItemFilter.ParseList(itemList.Value);
            var unmatched = new HashSet<string>(listed);
            bool useList = listEnabled.Value;
            var options = new ItemFilter.Options
            {
                useList = useList, includeOnly = listMode.Value == ListMode.IncludeOnly, skipObjective = skipObjective.Value,
            };

            var table = new bool[ItemCatalog.itemCount];
            var tableNoList = new bool[table.Length];
            var noCopy = new bool[table.Length];
            int count = 0;
            for (int i = 0; i < table.Length; i++)
            {
                ItemDef def = ItemCatalog.GetItemDef((ItemIndex)i);
                if (!def) continue;

                ItemTierDef tierDef = ItemTierCatalog.GetItemTierDef(def.tier);
                var item = new ItemFilter.Item
                {
                    internalName = ItemFilter.Normalize(def.name),
                    englishName = ItemFilter.Normalize(Language.GetString(def.nameToken, "en")),
                    tierOk = ItemFilter.TierAllowed(tierNames, def.tier.ToString(), tierDef ? tierDef.name : null),
                    hidden = def.hidden,
                    questTag = def.ContainsTag(ItemTag.ObjectiveRelated) || def.ContainsTag(ItemTag.PowerShape),
                    worldUnique = def.ContainsTag(ItemTag.WorldUnique),
                    hex = def.tier == ItemTier.Hex,
                };
                ItemFilter.Result r = ItemFilter.Decide(in item, in options, listed);
                if (r.inList) { unmatched.Remove(item.internalName); unmatched.Remove(item.englishName); }
                noCopy[i] = r.noDuplicatorCopy;
                tableNoList[i] = r.allowedNoList;
                table[i] = r.allowed;
                if (r.allowed) count++;
            }
            allowedItem = table;
            allowedItemNoList = tableNoList;
            noDuplicatorCopy = noCopy;

            if (useList && unmatched.Count > 0)
                Log.Warn("ItemList: no item matches these names: " + string.Join(", ", unmatched));
            Log.Info($"Item filter rebuilt: {count} of {table.Length} items can be distributed.");
        }
    }
}
