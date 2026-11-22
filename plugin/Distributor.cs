using System.Collections.Generic;
using H1xl.Shared;
using RoR2;
using RoR2.Artifacts;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;

namespace AutoLootDistribution
{
    /// <summary>One distributed drop on its way to a player (it flies as an <see cref="ItemFlightOrb"/>).</summary>
    internal sealed class Loot
    {
        public Vector3 origin;
        public ItemIndex itemIndex;
        public UniquePickup pickup;
        public CharacterMaster recipient;
        public int tierSlot;
        /// <summary>Artifact of Command: spawn the command cube at the player's feet instead of granting the item.</summary>
        public bool commandCube;
        /// <summary>The dropped pickup's flags: rerolled by the Recycler, and a printer's output.</summary>
        public bool recycled, duplicated;
        /// <summary>The chest the item came from (Artifact of Delusion remembers the item taken from it).</summary>
        public ChestBehavior chest;
        /// <summary>Where the recipient was when the orb was sent: the item drops there if they leave mid-flight.</summary>
        public Vector3? lastRecipientPosition;
        public GenericPickupController.CreatePickupInfo cubeInfo;
        /// <summary>Counted in the per-player pending items (Balanced) while it flies.</summary>
        public bool counted;
    }

    internal static class Distributor
    {
        /// <summary>True while the mod spawns a droplet itself; the droplet hook lets it through.</summary>
        public static bool SpawningOwnDroplet;

        /// <summary>
        /// Set when <see cref="Send"/> takes the item over. Callers clear it before <see cref="TryDistribute"/> /
        /// <see cref="GiveChoice"/> and don't fall back to the vanilla drop on an exception once it is set.
        /// </summary>
        public static bool HandedOver;

        private static int roundRobin;

        // Icons for flying command cubes, resolved once the catalogs are loaded.
        private static PickupIndex scrapWhite = PickupIndex.none, scrapGreen = PickupIndex.none, scrapRed = PickupIndex.none,
                                   scrapYellow = PickupIndex.none, strangeWhite = PickupIndex.none, strangeGreen = PickupIndex.none,
                                   strangeRed = PickupIndex.none, fuelArray = PickupIndex.none;

        public static void InitIcons()
        {
            scrapWhite = ItemPickup("ScrapWhite");
            scrapGreen = ItemPickup("ScrapGreen");
            scrapRed = ItemPickup("ScrapRed");
            scrapYellow = ItemPickup("ScrapYellow");
            strangeWhite = ItemPickup("ScrapWhiteSuppressed"); // Strange Scrap: the void-styled scrap
            strangeGreen = ItemPickup("ScrapGreenSuppressed");
            strangeRed = ItemPickup("ScrapRedSuppressed");
            fuelArray = PickupCatalog.FindPickupIndex(EquipmentCatalog.FindEquipmentIndex("QuestVolatileBattery"));
            if (!ItemFlight.PickupOrbEffectFound) Log.Warn("Pickup orb effect not found: command cubes will fly with their item's icon.");
        }

        private static PickupIndex ItemPickup(string name) => PickupCatalog.FindPickupIndex(ItemCatalog.FindItemIndex(name));

        /// <summary>
        /// Icon for a flying command cube: Item Scrap of its tier, Strange Scrap for void tiers (there is no yellow one),
        /// Fuel Array for equipment, otherwise the dropped pickup.
        /// </summary>
        private static PickupIndex VanillaCubeIcon(PickupDef def, PickupIndex dropped)
        {
            PickupIndex icon = PickupIndex.none;
            if (def.equipmentIndex != EquipmentIndex.None) icon = fuelArray;
            else switch (def.itemTier)
            {
                case ItemTier.Tier1: icon = scrapWhite; break;
                case ItemTier.Tier2: icon = scrapGreen; break;
                case ItemTier.Tier3: icon = scrapRed; break;
                case ItemTier.Boss: case ItemTier.VoidBoss: icon = scrapYellow; break;
                case ItemTier.VoidTier1: icon = strangeWhite.isValid ? strangeWhite : scrapWhite; break;
                case ItemTier.VoidTier2: icon = strangeGreen.isValid ? strangeGreen : scrapGreen; break;
                case ItemTier.VoidTier3: icon = strangeRed.isValid ? strangeRed : scrapRed; break;
            }
            return icon.isValid ? icon : dropped;
        }
        // Items sent but not yet arrived, per player and tier, so Balanced doesn't send a whole burst to one player.
        private static readonly Dictionary<CharacterMaster, int[]> pending = new Dictionary<CharacterMaster, int[]>();

        // Drops that were still flying when the stage ended (command cubes, items whose recipient is gone): the old scene
        // unloads 0.75 s after the flush, so they are dropped next to the player on the next stage instead.
        // A null recipient means "next to any player".
        private static readonly List<(GenericPickupController.CreatePickupInfo info, CharacterMaster recipient)> carriedOver =
            new List<(GenericPickupController.CreatePickupInfo, CharacterMaster)>();
        private static bool nextStageStarted;

        public static void Reset()
        {
            roundRobin = 0;
            pending.Clear();
            carriedOver.Clear();
            nextStageStarted = false;
        }

        public static void OnStageBegin() => nextStageStarted = true;

        /// <summary>Called every physics frame on the host: drops the carried-over pickups once their player is out of the drop pod.</summary>
        public static void SpawnCarriedOver()
        {
            if (carriedOver.Count == 0 || !nextStageStarted || !NetworkServer.active) return;
            for (int i = carriedOver.Count - 1; i >= 0; i--)
            {
                var (info, recipient) = carriedOver[i];
                PlayerCharacterMasterController pc = recipient ? recipient.playerCharacterMasterController : null;
                bool gone = !recipient || (Settings.IgnoreDisconnected && pc && !pc.isConnected);
                CharacterBody body = gone ? AnyPlayerBody() : recipient.GetBody();
                if (!body || body.currentVehicle) continue;
                carriedOver.RemoveAt(i);
                SpawnDroplet(info, ItemFlight.DropPoint(body, body.footPosition));
            }
        }

        private static CharacterBody AnyPlayerBody()
        {
            foreach (PlayerCharacterMasterController pc in PlayerCharacterMasterController.instances)
            {
                CharacterBody body = pc && pc.master ? pc.master.GetBody() : null;
                if (body && !body.currentVehicle) return body;
            }
            return null;
        }

        /// <summary>Returns true if the pickup was taken over and the droplet must not be spawned.</summary>
        public static bool TryDistribute(Source source, CharacterMaster owner, in GenericPickupController.CreatePickupInfo info, Vector3 position)
        {
            DistributionMode mode = Settings.ModeFor(source);
            if (mode == DistributionMode.Disabled) return false;

            // Void Potentials, Void Fields / Wake / Halcyon orbs, Artifact of Delusion: never touched.
            if (info.pickerOptions != null || info.prefabOverride) return false;
            if ((info.artifactFlag & GenericPickupController.PickupArtifactFlag.Delusion) != 0) return false;

            UniquePickup pickup = info.pickup;
            PickupDef def = PickupCatalog.GetPickupDef(pickup.pickupIndex);
            if (def == null) return false;
            bool isItem = def.itemIndex != ItemIndex.None;

            // Artifact of Command: the droplet would become a command cube. Move the cube to the player or leave it.
            // Equipment is distributed only as a command cube (picking it up directly would swap the player's equipment).
            bool cube = !pickup.ignoreArtifactBasedPickupReplacements &&
                        ((info.artifactFlag & GenericPickupController.PickupArtifactFlag.Command) != 0 ||
                         CubeIcons.CommandEnabled);
            // Wandering CHEF and the Drifter: the player made the item, so the item filter doesn't apply (hidden items excepted).
            // Solus Extractor: copies of the recipient's own stolen items.
            bool chosen = source == Source.WanderingChef || source == Source.Drifter || source == Source.SolusExtractor;
            bool allowed = cube
                ? Settings.CommandToPlayer && (isItem ? chosen || Settings.IsTierAllowed(def.itemIndex) : def.equipmentIndex != EquipmentIndex.None)
                : isItem && (chosen ? !ItemCatalog.GetItemDef(def.itemIndex).hidden : Settings.IsItemAllowed(def.itemIndex));
            if (!allowed) return false;

            ItemTier tier = def.itemTier;
            CharacterMaster player = ResolvePlayer(owner);
            bool equipment = !isItem; // only as a command cube
            CharacterMaster target = Choose(mode, player, position, tier, equipment);
            DistributionMode fallback = Settings.Fallback;
            bool usedFallback = false;
            if (!target && fallback != DistributionMode.Disabled)
            {
                target = Choose(fallback, null, position, tier, equipment);
                usedFallback = true;
            }
            if (!target) return false;

            // Wandering CHEF: a dish the recipient's void item would corrupt stays on the ground for another player.
            if (source == Source.WanderingChef && !cube && Settings.ChefProtect && WouldBeCorrupted(target, def.itemIndex))
            {
                if (Log.Verbose)
                    Log.Info($"{source}: {def.internalName} left on the ground, " +
                             $"{target.playerCharacterMasterController.GetDisplayName()}'s void item would corrupt it");
                return false;
            }

            Send(target, def.itemIndex, tier, pickup, position, cube, in info);
            if (Log.Verbose)
            {
                string ownerName = player ? player.playerCharacterMasterController.GetDisplayName() : "none";
                Log.Info($"{source}: {def.internalName} -> {target.playerCharacterMasterController.GetDisplayName()} " +
                         $"(mode {mode}{(usedFallback ? ", fallback " + fallback : "")}, owner {ownerName}{(cube ? ", command cube" : "")})");
            }
            return true;
        }

        /// <summary>
        /// Gives the item chosen from a command cube or a "choose one" orb to the player who chose it.
        /// Returns false (vanilla drop) for equipment, key items, or when that player can't receive items.
        /// </summary>
        public static bool GiveChoice(CharacterMaster chooser, UniquePickup pickup, Vector3 origin, ChestBehavior chest)
        {
            PickupDef def = PickupCatalog.GetPickupDef(pickup.pickupIndex);
            if (def == null || def.itemIndex == ItemIndex.None) return false;
            CharacterMaster player = ResolvePlayer(chooser);
            if (!IsValid(player)) return false;
            // Already chosen: if its recipient leaves mid-flight, the dropped item must not turn into a command cube again
            // (vanilla spawns the chosen pickup directly, without a droplet). Only droplets read this flag.
            pickup.ignoreArtifactBasedPickupReplacements = true;
            // The chest a command cube came from, as the vanilla PickupPickerController.CreatePickup passes it on.
            Send(player, def.itemIndex, def.itemTier, pickup, origin, cube: false,
                 new GenericPickupController.CreatePickupInfo { chest = chest });
            if (Log.Verbose)
                Log.Info($"Choice: {def.internalName} -> {player.playerCharacterMasterController.GetDisplayName()}");
            return true;
        }

        /// <summary>True if the player has the void item that turns this item into itself on pickup.</summary>
        private static bool WouldBeCorrupted(CharacterMaster player, ItemIndex item)
        {
            ItemIndex voidItem = RoR2.Items.ContagiousItemManager.GetTransformedItemIndex(item);
            return voidItem != ItemIndex.None && player.inventory && player.inventory.GetItemCountEffective(voidItem) > 0;
        }

        /// <summary>Walks minion ownership (turrets, drones, Devotion lemurians) up to a player.</summary>
        private static CharacterMaster ResolvePlayer(CharacterMaster master)
        {
            for (int i = 0; master && i < 8; i++)
            {
                if (master.playerCharacterMasterController) return master;
                CharacterMaster next = master.minionOwnership ? master.minionOwnership.ownerMaster : null;
                if (next == master) break;
                master = next;
            }
            return null;
        }

        private static bool IsValid(CharacterMaster m)
        {
            if (!m || !m.inventory) return false;
            PlayerCharacterMasterController pc = m.playerCharacterMasterController;
            if (!pc) return false;
            if (Settings.IgnoreDisconnected && !pc.isConnected) return false;
            if (Settings.AllowDead) return true;
            CharacterBody body = m.GetBody();
            return body && body.healthComponent && body.healthComponent.alive;
        }

        // Reused per drop: the players as RecipientChoice sees them.
        private static Candidate[] candidates = new Candidate[4];
        private static readonly System.Func<int, int> randomBelow = n => Random.Range(0, n);

        private static CharacterMaster Choose(DistributionMode mode, CharacterMaster owner, Vector3 pos, ItemTier tier, bool equipment)
        {
            if (mode == DistributionMode.Owner) return IsValid(owner) ? owner : null;
            if (mode == DistributionMode.Disabled) return null;

            var players = PlayerCharacterMasterController.instances;
            int n = players.Count;
            if (candidates.Length < n) candidates = new Candidate[n];
            int slot = equipment ? EquipmentSlot : Slot(tier);
            for (int i = 0; i < n; i++)
            {
                CharacterMaster m = players[i] ? players[i].master : null;
                var c = new Candidate { valid = IsValid(m) };
                if (c.valid && mode == DistributionMode.Nearest)
                {
                    CharacterBody b = m.GetBody();
                    c.hasBody = b;
                    if (b) c.sqrDistance = (b.corePosition - pos).sqrMagnitude;
                }
                else if (c.valid && mode == DistributionMode.Balanced)
                {
                    // Equipment has no tier (its PickupDef says NoTier): balance by "holds an equipment" instead.
                    c.count = equipment
                        ? (m.inventory.currentEquipmentIndex != EquipmentIndex.None ? 1 : 0)
                        : m.inventory.GetTotalItemCountOfTier(tier);
                    if (pending.TryGetValue(m, out int[] p)) c.count += p[slot];
                }
                candidates[i] = c;
            }
            int pick = RecipientChoice.Pick(mode, candidates, n, ref roundRobin, randomBelow);
            return pick >= 0 ? players[pick].master : null;
        }

        private const int TierSlots = RecipientChoice.TierSlots, EquipmentSlot = RecipientChoice.EquipmentSlot;

        private static int Slot(ItemTier tier) => RecipientChoice.Slot((int)tier);

        private static void Send(CharacterMaster target, ItemIndex item, ItemTier tier, UniquePickup pickup, Vector3 origin,
                                 bool cube, in GenericPickupController.CreatePickupInfo info)
        {
            var loot = new Loot
            {
                origin = origin,
                itemIndex = item,
                pickup = pickup,
                recipient = target,
                tierSlot = item == ItemIndex.None ? EquipmentSlot : Slot(tier),
                commandCube = cube,
                recycled = info.recycled,
                duplicated = info.duplicated,
                chest = info.chest,
            };
            var orb = new ItemFlightOrb { origin = origin, itemIndex = item, stack = 1 };
            if (cube)
            {
                loot.cubeInfo = info;
                PickupDef def = PickupCatalog.GetPickupDef(pickup.pickupIndex);
                // Scrap of the cube's tier (Fuel Array for equipment); players with the mod see the rendered cube icon.
                orb.iconPickup = VanillaCubeIcon(def, pickup.pickupIndex);
                CubeIcon cubeIcon = CubeIcons.For(def);
                if (cubeIcon != CubeIcon.None) orb.decorate = data => CubeIcons.Mark(data, cubeIcon);
            }

            // From here on the item belongs to the mod: the caller must not spawn the vanilla droplet, even on an exception.
            HandedOver = true;

            if (Settings.Animation && OrbManager.instance)
            {
                CharacterBody body = target.GetBody();
                if (body) loot.lastRecipientPosition = ItemFlight.DropPoint(body, body.footPosition);
                if (!pending.TryGetValue(target, out int[] p)) pending[target] = p = new int[TierSlots + 1];
                p[loot.tierSlot]++;
                loot.counted = true;
            }
            // Without a body (dead recipient) the orb has no target: no visual, but it still arrives after the travel time.
            ItemFlight.Launch(orb, target, Settings.TravelTime, Settings.Animation, stageEnding => Complete(loot, stageEnding));
        }

        private static void Complete(Loot loot, bool stageEnding)
        {
            if (loot.counted && pending.TryGetValue(loot.recipient, out int[] p) && p[loot.tierSlot] > 0) p[loot.tierSlot]--;
            SafeDeliver(loot, stageEnding);
        }

        /// <summary>Delivers everything still flying when the stage ends, so nothing is lost with the old scene.</summary>
        public static void FlushInFlight()
        {
            nextStageStarted = false; // the old scene still exists for 0.75 s: carried-over drops wait for the next stage
            ItemFlight.FlushAll();
            pending.Clear();
        }

        /// <summary>
        /// Deliver that never throws: it runs inside OrbManager.FixedUpdate (a throw stalls every other orb) and inside the
        /// stage change (a throw stops the scene from changing).
        /// </summary>
        private static void SafeDeliver(Loot orb, bool stageEnding)
        {
            try { Deliver(orb, stageEnding); }
            catch (System.Exception e)
            {
                Log.Error($"Delivering {PickupCatalog.GetPickupDef(orb.pickup.pickupIndex)?.internalName ?? "a pickup"} failed: {e}");
            }
        }

        private static void Deliver(Loot orb, bool stageEnding)
        {
            CharacterMaster master = orb.recipient;
            CharacterBody body = master ? master.GetBody() : null;

            if (orb.commandCube)
            {
                // At stage end: dropped next to the player on the next stage, so the cube (item or equipment) keeps
                // its choice instead of being lost with the old scene.
                if (stageEnding)
                {
                    carriedOver.Add((orb.cubeInfo, master));
                    return;
                }
                // Dropped just above the feet: it lands at once and the game turns it into the cube with all
                // vanilla Command logic. Dead recipient: where it dropped.
                SpawnDroplet(orb.cubeInfo, ItemFlight.DropPoint(body, orb.origin));
                return;
            }

            // The game keeps a disconnected player's master and inventory, so check the connection too.
            PlayerCharacterMasterController pc = master ? master.playerCharacterMasterController : null;
            if (!master || !master.inventory || (Settings.IgnoreDisconnected && pc && !pc.isConnected))
            {
                // The recipient left while the item was flying: drop it on the ground where they were last seen
                // (at stage end: on the next stage, next to any player).
                var drop = new GenericPickupController.CreatePickupInfo
                {
                    pickup = orb.pickup, rotation = Quaternion.identity, recycled = orb.recycled, duplicated = orb.duplicated,
                    chest = orb.chest,
                };
                if (stageEnding) carriedOver.Add((drop, null));
                else SpawnDroplet(drop, ItemFlight.DropPoint(body, orb.lastRecipientPosition ?? orb.origin));
                return;
            }

            // Only items get here: equipment is distributed only as a command cube.
            UniquePickup pickup = orb.pickup;

            // Alive: run the vanilla pickup logic (Substandard Duplicator, mutually exclusive items, notification).
            PickupDef def = PickupCatalog.GetPickupDef(pickup.pickupIndex);
            if (Settings.Notify && body && def != null && def.attemptGrant != null)
            {
                if (orb.duplicated || Settings.BlocksDuplicatorCopy(orb.itemIndex))
                {
                    // A printer's output. The game's grant code adds a Substandard Duplicator copy whenever there is no
                    // pickup controller marked Duplicated, so printed items are given here, as the game would for them:
                    // mutually exclusive items first, then the item, without the Duplicator copy.
                    DropMutuallyExclusive(master.inventory, orb.itemIndex);
                    GiveToInventory(master.inventory, orb.itemIndex, pickup);
                    body.OnPickup(pickup.isTempItem ? CharacterBody.PickupClass.TempItem : CharacterBody.PickupClass.Item);
                    GenericPickupController.SendPickupMessage(master, pickup);
                }
                else
                {
                    var ctx = new PickupDef.GrantContext
                    {
                        body = body,
                        position = orb.origin,
                        rotation = Quaternion.identity,
                        uniquePickup = pickup,
                    };
                    def.attemptGrant(ref ctx);
                    body.OnPickup(pickup.isTempItem ? CharacterBody.PickupClass.TempItem : CharacterBody.PickupClass.Item);
                    // Not ctx.pickup: for mutually exclusive items the game puts the removed item there. The message is
                    // about the item that was given, as in GenericPickupController.AttemptGrant.
                    if (ctx.shouldNotify) GenericPickupController.SendPickupMessage(master, pickup);
                }
                JustDropItemsCompat.Notify(master, orb.itemIndex, orb.recycled, orb.duplicated);
                SetDelusionPickup(orb);
                return;
            }

            // Dead recipient or notifications off: straight into the inventory.
            GiveToInventory(master.inventory, orb.itemIndex, pickup);
            if (Settings.Notify) GenericPickupController.SendPickupMessage(master, pickup);
            JustDropItemsCompat.Notify(master, orb.itemIndex, orb.recycled, orb.duplicated);
            SetDelusionPickup(orb);
        }

        /// <summary>
        /// Artifact of Delusion: the chest remembers the item taken from it, so after the reset it can offer the guess.
        /// The game does it in GenericPickupController.AttemptGrant, which items given by the mod don't go through.
        /// </summary>
        private static void SetDelusionPickup(Loot orb)
        {
            if (orb.chest && RunArtifactManager.instance && DelusionChestController.isDelusionEnable)
                orb.chest.CallRpcSetDelusionPickupIndex(orb.pickup.pickupIndex);
        }

        private static void GiveToInventory(Inventory inv, ItemIndex item, in UniquePickup pickup)
        {
            if (pickup.isRental) inv.GiveItemRented(item, 1);
            else if (pickup.isTempItem) inv.GiveItemTemp(item, pickup.decayValue);
            else inv.GiveItemPermanent(item, 1);
        }

        /// <summary>DLC4 item groups where only one item can be carried: the other one is removed, as in ItemDef.AttemptGrant.</summary>
        private static void DropMutuallyExclusive(Inventory inv, ItemIndex item)
        {
            ItemDef itemDef = ItemCatalog.GetItemDef(item);
            if (itemDef && ItemCatalog.IsPartOfGroup(itemDef, DLC4Content.ItemGroupTypes.MutualExclusivity, out var group) &&
                group.IsCarryingItemInGroup(inv, out ItemDef carried, itemDef) && carried && carried.itemIndex != item)
                inv.ResetItemPermanent(carried.itemIndex);
        }

        private static void SpawnDroplet(in GenericPickupController.CreatePickupInfo info, Vector3 at)
        {
            SpawningOwnDroplet = true;
            try { PickupDropletController.CreatePickupDroplet(info, at, Vector3.zero); }
            finally { SpawningOwnDroplet = false; }
        }
    }
}
