using System.Collections.Generic;
using Xunit;

namespace AutoLootDistribution.Tests
{
    public class RecipientChoiceTests
    {
        private static Candidate Valid(float dist = 0f, bool body = true, int count = 0) =>
            new Candidate { valid = true, hasBody = body, sqrDistance = dist, count = count };

        private static readonly Candidate Invalid = new Candidate();

        // Random.Range(0, n) stand-in: returns the queued answers in order and records each n.
        private sealed class Dice
        {
            private readonly Queue<int> answers;
            public readonly List<int> asked = new List<int>();
            public Dice(params int[] a) { answers = new Queue<int>(a); }
            public int Roll(int n) { asked.Add(n); return answers.Count > 0 ? answers.Dequeue() : 0; }
        }

        private static int Pick(DistributionMode mode, Candidate[] c, ref int rr, Dice dice = null) =>
            RecipientChoice.Pick(mode, c, c.Length, ref rr, (dice ?? new Dice()).Roll);

        private static int Pick(DistributionMode mode, params Candidate[] c)
        {
            int rr = 0;
            return Pick(mode, c, ref rr);
        }

        [Fact]
        public void NoPlayers_NobodyInAnyMode()
        {
            foreach (DistributionMode m in new[] { DistributionMode.Nearest, DistributionMode.Random,
                                                   DistributionMode.RoundRobin, DistributionMode.Balanced })
                Assert.Equal(-1, Pick(m));
        }

        [Fact]
        public void AllInvalid_Nobody()
        {
            foreach (DistributionMode m in new[] { DistributionMode.Nearest, DistributionMode.Random,
                                                   DistributionMode.RoundRobin, DistributionMode.Balanced })
                Assert.Equal(-1, Pick(m, Invalid, Invalid));
        }

        [Fact]
        public void DisabledAndOwner_NotPickedFromTheList() // the caller handles them
        {
            Assert.Equal(-1, Pick(DistributionMode.Disabled, Valid()));
            Assert.Equal(-1, Pick(DistributionMode.Owner, Valid()));
        }

        [Fact]
        public void Nearest_ClosestValidBody()
        {
            Assert.Equal(2, Pick(DistributionMode.Nearest, Valid(9f), Invalid, Valid(4f), Valid(16f)));
        }

        [Fact]
        public void Nearest_InvalidCloserPlayerSkipped()
        {
            var invalidClose = new Candidate { valid = false, hasBody = true, sqrDistance = 0f };
            Assert.Equal(1, Pick(DistributionMode.Nearest, invalidClose, Valid(100f)));
        }

        [Fact]
        public void Nearest_PlayerWithBodyBeatsDeadPlayer()
        {
            Assert.Equal(1, Pick(DistributionMode.Nearest, Valid(body: false), Valid(1e6f)));
        }

        [Fact]
        public void Nearest_OnlyDeadPlayers_FirstDeadOne()
        {
            Assert.Equal(1, Pick(DistributionMode.Nearest, Invalid, Valid(body: false), Valid(body: false)));
        }

        [Fact]
        public void Nearest_EqualDistance_FirstOne()
        {
            Assert.Equal(0, Pick(DistributionMode.Nearest, Valid(5f), Valid(5f)));
        }

        [Fact]
        public void Random_ReservoirAsksOncePerValidPlayer()
        {
            var dice = new Dice(0, 1, 0); // keep 1st, skip 2nd valid, take 3rd valid
            int rr = 0;
            int pick = Pick(DistributionMode.Random, new[] { Valid(), Invalid, Valid(), Valid() }, ref rr, dice);
            Assert.Equal(3, pick);
            Assert.Equal(new[] { 1, 2, 3 }, dice.asked);
        }

        [Fact]
        public void Random_OnlyOneValid_AlwaysThatOne()
        {
            var dice = new Dice(0);
            int rr = 0;
            Assert.Equal(1, Pick(DistributionMode.Random, new[] { Invalid, Valid(), Invalid }, ref rr, dice));
        }

        [Fact]
        public void RoundRobin_CyclesAndSkipsInvalid()
        {
            var c = new[] { Valid(), Invalid, Valid(), Valid() };
            int rr = 0;
            var got = new List<int>();
            for (int i = 0; i < 5; i++) got.Add(Pick(DistributionMode.RoundRobin, c, ref rr));
            Assert.Equal(new[] { 0, 2, 3, 0, 2 }, got);
        }

        [Fact]
        public void RoundRobin_StartBeyondList_Wraps() // a player left: the stored index can exceed the count
        {
            int rr = 7;
            Assert.Equal(1, Pick(DistributionMode.RoundRobin, new[] { Valid(), Valid() }, ref rr));
            Assert.Equal(2, rr);
        }

        [Fact]
        public void RoundRobin_NobodyValid_IndexUnchanged()
        {
            int rr = 1;
            Assert.Equal(-1, Pick(DistributionMode.RoundRobin, new[] { Invalid, Invalid }, ref rr));
            Assert.Equal(1, rr);
        }

        [Fact]
        public void Balanced_FewestItems()
        {
            Assert.Equal(1, Pick(DistributionMode.Balanced, Valid(count: 3), Valid(count: 1), Valid(count: 2)));
        }

        [Fact]
        public void Balanced_InvalidWithFewerSkipped()
        {
            var invalid = new Candidate { valid = false, count = 0 };
            Assert.Equal(1, Pick(DistributionMode.Balanced, invalid, Valid(count: 5)));
        }

        [Fact]
        public void Balanced_TiesDecidedByDice()
        {
            // ties: 2nd player asks Roll(2), 3rd asks Roll(3)
            var dice = new Dice(0, 1);
            int rr = 0;
            int pick = Pick(DistributionMode.Balanced, new[] { Valid(count: 1), Valid(count: 1), Valid(count: 1) }, ref rr, dice);
            Assert.Equal(1, pick);
            Assert.Equal(new[] { 2, 3 }, dice.asked);
        }

        [Fact]
        public void Balanced_NewMinimumResetsTies()
        {
            var dice = new Dice(1, 0);
            int rr = 0;
            // 0 and 1 tie at 2 (Roll(2) = 1 keeps 0), 2 has 1 → new best, 3 ties with it (Roll(2) = 0 → 3)
            int pick = Pick(DistributionMode.Balanced, new[] { Valid(count: 2), Valid(count: 2), Valid(count: 1), Valid(count: 1) }, ref rr, dice);
            Assert.Equal(3, pick);
            Assert.Equal(new[] { 2, 2 }, dice.asked);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(14, 14)]
        [InlineData(15, 15)] // modded tiers share the last slot
        [InlineData(40, 15)]
        [InlineData(-1, 15)] // unexpected negative tier: last slot, never out of range
        public void Slot_FitsTheTable(int tier, int slot)
        {
            Assert.Equal(slot, RecipientChoice.Slot(tier));
            Assert.NotEqual(RecipientChoice.EquipmentSlot, RecipientChoice.Slot(tier));
        }
    }
}
