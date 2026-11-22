using System.Collections.Generic;
using Xunit;

namespace AutoLootDistribution.Tests
{
    public class ItemFilterTests
    {
        private static ItemFilter.Item Item(string name, string english = "", bool tierOk = true, bool hidden = false,
                                            bool questTag = false, bool worldUnique = false, bool hex = false) =>
            new ItemFilter.Item
            {
                internalName = ItemFilter.Normalize(name), englishName = ItemFilter.Normalize(english), tierOk = tierOk,
                hidden = hidden, questTag = questTag, worldUnique = worldUnique, hex = hex,
            };

        private static readonly ItemFilter.Options NoList = new ItemFilter.Options { skipObjective = true };
        private static readonly ItemFilter.Options Exclude = new ItemFilter.Options { useList = true, skipObjective = true };
        private static readonly ItemFilter.Options IncludeOnly = new ItemFilter.Options { useList = true, includeOnly = true, skipObjective = true };

        private static ItemFilter.Result Decide(ItemFilter.Item item, ItemFilter.Options o, string list = "") =>
            ItemFilter.Decide(in item, in o, ItemFilter.ParseList(list));

        [Theory]
        [InlineData("H3AD-5T v2", "h3ad5tv2")]
        [InlineData("  Collector's Compulsion ", "collectorscompulsion")]
        [InlineData("Tier1", "tier1")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("--- !!", "")]
        public void Normalize_KeepsLowerLettersAndDigits(string input, string expected)
        {
            Assert.Equal(expected, ItemFilter.Normalize(input));
        }

        [Fact]
        public void ParseList_SplitsNormalizesDropsEmpty()
        {
            HashSet<string> set = ItemFilter.ParseList("Bustling Fungus, ,ROSE buckler,,  Bustling fungus ");
            Assert.Equal(new HashSet<string> { "bustlingfungus", "rosebuckler" }, set);
            Assert.Empty(ItemFilter.ParseList(""));
        }

        [Fact]
        public void TierAllowed_ByEnumOrDefName()
        {
            var tiers = ItemFilter.ParseList("Tier1, tier2def");
            Assert.True(ItemFilter.TierAllowed(tiers, "Tier1", "Tier1Def"));
            Assert.True(ItemFilter.TierAllowed(tiers, "AssignedAtRuntime", "Tier2Def")); // modded tier by its def name
            Assert.False(ItemFilter.TierAllowed(tiers, "Tier3", "Tier3Def"));
            Assert.False(ItemFilter.TierAllowed(tiers, "Tier3", null));
        }

        [Fact]
        public void TierNotAllowed_NeverDistributed()
        {
            ItemFilter.Result r = Decide(Item("Hoof", tierOk: false), IncludeOnly, "Hoof");
            Assert.False(r.allowed);
            Assert.False(r.allowedNoList);
        }

        [Fact]
        public void Hidden_NeverDistributed()
        {
            ItemFilter.Result r = Decide(Item("DrizzlePlayerHelper", hidden: true), NoList);
            Assert.False(r.allowed);
            Assert.False(r.allowedNoList);
        }

        [Theory]
        [InlineData("ArtifactKey")]
        [InlineData("PowerCube")]
        [InlineData("PowerPyramid")]
        [InlineData("PowerOrbSphere")]
        [InlineData("MasterBattery")]
        [InlineData("MasterCore")]
        [InlineData("HelpWake")]
        public void ObjectiveByName_SkippedAndNeverDuplicated(string name)
        {
            ItemFilter.Result r = Decide(Item(name), NoList);
            Assert.False(r.allowed);
            Assert.False(r.allowedNoList);
            Assert.True(r.noDuplicatorCopy);
        }

        [Fact]
        public void ObjectiveByTag_Skipped()
        {
            Assert.False(Decide(Item("ModdedKey", questTag: true), NoList).allowedNoList);
        }

        [Fact]
        public void Objective_DistributedWhenSkipIsOff()
        {
            var o = new ItemFilter.Options { skipObjective = false };
            ItemFilter.Result r = Decide(Item("PowerCube"), o);
            Assert.True(r.allowed);
            Assert.True(r.noDuplicatorCopy); // the Duplicator rule doesn't depend on the skip setting
        }

        [Fact]
        public void HexAndWorldUnique_NoDuplicatorCopyButDistributable()
        {
            ItemFilter.Result hex = Decide(Item("HexItem", hex: true), NoList);
            Assert.True(hex.allowed);
            Assert.True(hex.noDuplicatorCopy);
            Assert.True(Decide(Item("Unique", worldUnique: true), NoList).noDuplicatorCopy);
            Assert.False(Decide(Item("Hoof"), NoList).noDuplicatorCopy);
        }

        [Fact]
        public void ListOff_IgnoresList()
        {
            ItemFilter.Result r = Decide(Item("Mushroom", "Bustling Fungus"), NoList, "Bustling Fungus");
            Assert.True(r.allowed);
            Assert.True(r.inList);
        }

        [Fact]
        public void Exclude_ListedByEnglishOrInternalNameStays()
        {
            Assert.False(Decide(Item("Mushroom", "Bustling Fungus"), Exclude, "bustling fungus").allowed);
            Assert.False(Decide(Item("Mushroom", "Bustling Fungus"), Exclude, "MUSHROOM").allowed);
            Assert.True(Decide(Item("Hoof", "Paul's Goat Hoof"), Exclude, "bustling fungus").allowed);
        }

        [Fact]
        public void Exclude_ListedStillAllowedWithoutList() // single player with IgnoreItemListInSingleplayer, command cubes
        {
            ItemFilter.Result r = Decide(Item("Mushroom", "Bustling Fungus"), Exclude, "Bustling Fungus");
            Assert.False(r.allowed);
            Assert.True(r.allowedNoList);
        }

        [Fact]
        public void IncludeOnly_OnlyListed()
        {
            Assert.True(Decide(Item("Hoof", "Paul's Goat Hoof"), IncludeOnly, "Paul's Goat Hoof").allowed);
            Assert.False(Decide(Item("Mushroom", "Bustling Fungus"), IncludeOnly, "Paul's Goat Hoof").allowed);
        }

        [Fact]
        public void IncludeOnly_ListDoesntBringBackSkippedItems()
        {
            Assert.False(Decide(Item("PowerCube", "Prison Matrix"), IncludeOnly, "Prison Matrix").allowed);
            Assert.False(Decide(Item("Secret", "Secret", hidden: true), IncludeOnly, "Secret").allowed);
        }

        [Fact]
        public void InList_EmptyEnglishNameDoesntMatchEmptyEntries()
        {
            // An item without an English name normalizes to "": the list never contains "" (ParseList drops empty parts).
            Assert.False(Decide(Item("Thing", ""), Exclude, "a, , b").inList);
        }
    }
}
