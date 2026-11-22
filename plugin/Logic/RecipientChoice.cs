using System;

namespace AutoLootDistribution
{
    public enum DistributionMode { Disabled, Owner, Nearest, Random, RoundRobin, Balanced }

    /// <summary>One player as the recipient choice sees them. The caller fills only what the mode reads.</summary>
    internal struct Candidate
    {
        /// <summary>Can receive items now (connected, has an inventory, alive unless dead players may receive).</summary>
        public bool valid;
        /// <summary>Has a body on the map (Nearest: a valid player without one is used only when nobody has a body).</summary>
        public bool hasBody;
        /// <summary>Nearest: squared distance from the drop to the body.</summary>
        public float sqrDistance;
        /// <summary>Balanced: items of the tier held plus items of the tier still flying to them.</summary>
        public int count;
    }

    /// <summary>
    /// Picks the recipient among the players (pure logic, level-2 tested). Owner mode is decided by the caller: it needs
    /// no list. <paramref name="randomBelow"/>(n) returns 0..n-1 (the game's Random.Range(0, n)); the number and order
    /// of its calls is part of the behaviour.
    /// </summary>
    internal static class RecipientChoice
    {
        /// <summary>Index of the chosen candidate, or -1. <paramref name="roundRobin"/> is the next start index for RoundRobin.</summary>
        public static int Pick(DistributionMode mode, Candidate[] players, int n, ref int roundRobin, Func<int, int> randomBelow)
        {
            switch (mode)
            {
                case DistributionMode.Nearest:
                {
                    int best = -1, anyDead = -1;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (!players[i].valid) continue;
                        if (!players[i].hasBody) { if (anyDead < 0) anyDead = i; continue; }
                        if (players[i].sqrDistance < bestDist) { bestDist = players[i].sqrDistance; best = i; }
                    }
                    return best >= 0 ? best : anyDead;
                }

                case DistributionMode.Random:
                {
                    // Reservoir sampling: one pass, no list.
                    int pick = -1, seen = 0;
                    for (int i = 0; i < n; i++)
                        if (players[i].valid && randomBelow(++seen) == 0) pick = i;
                    return pick;
                }

                case DistributionMode.RoundRobin:
                    for (int k = 0; k < n; k++)
                    {
                        int idx = (roundRobin + k) % n;
                        if (!players[idx].valid) continue;
                        roundRobin = idx + 1;
                        return idx;
                    }
                    return -1;

                case DistributionMode.Balanced:
                {
                    int pick = -1, best = int.MaxValue, ties = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (!players[i].valid) continue;
                        int count = players[i].count;
                        if (count < best) { best = count; pick = i; ties = 1; }
                        else if (count == best && randomBelow(++ties) == 0) pick = i;
                    }
                    return pick;
                }
            }
            return -1;
        }

        public const int TierSlots = 16; // vanilla tiers fit; modded tiers share the last slot
        public const int EquipmentSlot = TierSlots; // equipment command cubes, after the tier slots

        /// <summary>The pending-items slot of a tier (<c>(int)ItemTier</c>).</summary>
        public static int Slot(int tier) => (uint)tier < TierSlots - 1 ? tier : TierSlots - 1;
    }
}
