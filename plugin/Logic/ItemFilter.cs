using System.Collections.Generic;
using System.Text;

namespace AutoLootDistribution
{
    /// <summary>
    /// The item filter's decisions (pure logic, level-2 tested). <see cref="Settings.RebuildItemTable"/> reads the item
    /// catalog into <see cref="Item"/> values and stores the results in per-item tables.
    /// </summary>
    internal static class ItemFilter
    {
        // Quest and key items, by internal name: none of them has the ObjectiveRelated tag in the game.
        // The ObjectiveRelated and PowerShape tags are checked too, for future patches and modded items.
        private static readonly HashSet<string> ObjectiveItems = new HashSet<string>
        {
            "artifactkey",     // Artifact Key
            "powercube",       // Prison Matrix
            "powerpyramid",    // Sentry Key
            "powerorbsphere",  // Ocular Battery
            "masterbattery",   // Encrypted Cerebellum
            "mastercore",      // Exposed Cerebellum
            "helpwake",        // Peace Flower
        };

        /// <summary>One item as the filter sees it. Names are already <see cref="Normalize"/>d.</summary>
        internal struct Item
        {
            public string internalName, englishName;
            /// <summary>Its tier is in the Tiers setting (<see cref="TierAllowed"/>).</summary>
            public bool tierOk;
            public bool hidden;
            /// <summary>Has the ObjectiveRelated or PowerShape tag.</summary>
            public bool questTag;
            public bool worldUnique;
            /// <summary>Its tier is Hex.</summary>
            public bool hex;
        }

        internal struct Options
        {
            public bool useList, includeOnly, skipObjective;
        }

        internal struct Result
        {
            /// <summary>May be distributed.</summary>
            public bool allowed;
            /// <summary>May be distributed, ignoring the item list (single player, command cubes).</summary>
            public bool allowedNoList;
            /// <summary>Substandard Duplicator must not copy it (with DuplicatorSkipsHexAndKeyItems).</summary>
            public bool noDuplicatorCopy;
            /// <summary>One of its names is in the item list.</summary>
            public bool inList;
        }

        /// <summary>Lower case letters and digits only: case, spaces and punctuation are ignored in names.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>A comma-separated setting as a set of normalized names (empty parts dropped).</summary>
        public static HashSet<string> ParseList(string value)
        {
            var set = new HashSet<string>();
            foreach (string part in value.Split(','))
            {
                string n = Normalize(part);
                if (n.Length > 0) set.Add(n);
            }
            return set;
        }

        /// <summary>The tier matches by its enum name (Tier1) or its ItemTierDef name (tier1def, modded tiers); def name may be null.</summary>
        public static bool TierAllowed(HashSet<string> tierNames, string tierEnumName, string tierDefName) =>
            tierNames.Contains(Normalize(tierEnumName)) || (tierDefName != null && tierNames.Contains(Normalize(tierDefName)));

        public static Result Decide(in Item item, in Options o, HashSet<string> listed)
        {
            var r = new Result { inList = listed.Contains(item.internalName) || listed.Contains(item.englishName) };
            bool objective = ObjectiveItems.Contains(item.internalName) || item.questTag;
            bool ok = item.tierOk && !item.hidden;
            if (ok && o.skipObjective && objective) ok = false;
            r.noDuplicatorCopy = item.hex || objective || item.worldUnique;
            r.allowedNoList = ok;
            if (ok && o.useList) ok = o.includeOnly ? r.inList : !r.inList;
            r.allowed = ok;
            return r;
        }
    }
}
