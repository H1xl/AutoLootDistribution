using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using H1xl.Shared;
using RoR2;
using RoR2.Artifacts;
using UnityEngine;
using UnityEngine.Networking;

namespace AutoLootDistribution
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency(RooPage.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.H1xl.AutoLootDistribution";
        public const string NAME = "AutoLootDistribution";
        public const string VERSION = "1.0.0";

        // Drop context: the source hooks set these around the vanilla method that spawns the droplets, and the
        // droplet hook reads them. Everything runs on the main thread; Scope restores the previous values.
        private static Source ctxSource;
        private static CharacterMaster ctxOwner;

        // Last player who successfully interacted with each interactable (chest, printer, scrapper...).
        private static readonly Dictionary<GameObject, CharacterMaster> interactors = new Dictionary<GameObject, CharacterMaster>();
        private static bool interactionSucceeded;

        private readonly struct Scope : IDisposable
        {
            private readonly Source prevSource;
            private readonly CharacterMaster prevOwner;

            public Scope(Source source, CharacterMaster owner)
            {
                prevSource = ctxSource;
                prevOwner = ctxOwner;
                ctxSource = source;
                ctxOwner = owner;
            }

            public void Dispose()
            {
                ctxSource = prevSource;
                ctxOwner = prevOwner;
            }
        }

        private void Awake()
        {
            Log.Init(Logger);
            Log.IsVerbose = () => Settings.Verbose;
            Settings.Init(Config);
            RoR2Application.onLoad += Settings.RebuildItemTable;
            RoR2Application.onLoad += Distributor.InitIcons;

            // Client side (also on the host): cube icons for flying command cubes.
            CubeIcons.Load(System.IO.Path.GetDirectoryName(Info.Location));
            HookUtil.Add(GameMembers.PickupTakenOrbEffect_OnEnable, typeof(Plugin), nameof(OnPickupOrbEffectEnable));

            string folder = System.IO.Path.GetDirectoryName(Info.Location);
            RooPage.TryRegister(() => RiskOfOptionsCompat.Register(folder));

            // Core: every droplet in the game goes through this overload.
            HookUtil.Add(GameMembers.PickupDropletController_CreatePickupDroplet, typeof(Plugin), nameof(OnCreatePickupDroplet));

            // Who used what.
            HookUtil.Add(GameMembers.Interactor_PerformInteraction, typeof(Plugin), nameof(OnPerformInteraction));
            GlobalEventManager.OnInteractionsGlobal += (_, __, ___) => interactionSucceeded = true;

            // Monster kills.
            HookUtil.Add(GameMembers.SacrificeArtifactManager_OnServerCharacterDeath, typeof(Plugin), nameof(OnSacrificeDeath));
            HookUtil.Add(GameMembers.GlobalDeathRewards_OnCharacterDeathGlobal, typeof(Plugin), nameof(OnGlobalDeathRewards));
            // Sonorous Whispers: a roll on its drop table during a death, followed by the droplet.
            HookUtil.Add(GameMembers.GlobalEventManager_OnCharacterDeath, typeof(Plugin), nameof(OnCharacterDeath));
            HookUtil.Add(GameMembers.PickupDropTable_GeneratePickup, typeof(Plugin), nameof(OnGeneratePickup));

            // Chests, scavenger bags and shrines.
            HookUtil.Add(GameMembers.ChestBehavior_ItemDrop, typeof(Plugin), nameof(OnChestItemDrop));
            HookUtil.Add(GameMembers.RouletteChestController_EjectPickupServer, typeof(Plugin), nameof(OnRouletteEject));
            HookUtil.Add(GameMembers.ScavBackpackBehavior_ItemDrop, typeof(Plugin), nameof(OnScavBackpackDrop));
            HookUtil.Add(GameMembers.ShrineChanceBehavior_AddShrineStack, typeof(Plugin), nameof(OnShrineChance));

            // Printers, multishops, cauldrons, Cleansing Pools, scrappers.
            HookUtil.Add(GameMembers.ShopTerminalBehavior_DropPickup, typeof(Plugin), nameof(OnShopDrop));
            HookUtil.Add(GameMembers.ScrappingToIdle_OnEnter, typeof(Plugin), nameof(OnScrapperDrop));
            HookUtil.Add(GameMembers.DroneScrappingToIdle_DropPickup, typeof(Plugin), nameof(OnDroneScrapperDrop));
            HookUtil.Add(GameMembers.DropItem_OnEnter, typeof(Plugin), nameof(OnSolusVendorDrop));

            // Wandering CHEF: who started the cooking, and the dish it drops a few seconds later.
            HookUtil.Add(GameMembers.MealPrepController_BeginCookingServer, typeof(Plugin), nameof(OnBeginCooking));
            HookUtil.Add(GameMembers.CookingToIdle_DropItem, typeof(Plugin), nameof(OnChefDrop));

            // Repurposed Crater keys dropped on kills (three spawners).
            HookUtil.Add(GameMembers.PowerOrbKeySpawner_SpawnKey, typeof(Plugin), nameof(OnKeySpawner));
            HookUtil.Add(GameMembers.PowerOrbEventIdle_SpawnKey, typeof(Plugin), nameof(OnKeyEventIdle));
            HookUtil.Add(GameMembers.PowerOrbEventSpawnOrb_SpawnKey, typeof(Plugin), nameof(OnKeyEventSpawnOrb));

            // Substandard Duplicator: no copy of Hex and quest/key items.
            HookUtil.Add(GameMembers.ItemDef_AttemptGrant, typeof(Plugin), nameof(OnItemAttemptGrant));

            // Mimic chest reward (the killer), Solus Extractor copies of stolen items (the player it stole from).
            HookUtil.Add(GameMembers.Death_SpawnDropletServer, typeof(Plugin), nameof(OnMimicDrop));
            HookUtil.Add(GameMembers.ItemDuplicatorController_DuplicateItem, typeof(Plugin), nameof(OnExtractorDuplicate));
            HookUtil.Add(GameMembers.ItemDuplicatorController_OnDestroy, typeof(Plugin), nameof(OnExtractorDestroy));

            // Items made by survivors and drones: the Drifter's Salvage and Tinker, the Junk Drone's Surprise!.
            HookUtil.Add(GameMembers.Salvage_DropTempItemServer, typeof(Plugin), nameof(OnDrifterSalvage));
            HookUtil.Add(GameMembers.TinkerProjectile_TransmuteTargetObject, typeof(Plugin), nameof(OnTinkerTransmute));
            HookUtil.Add(GameMembers.Surprise_DropTempItemServer, typeof(Plugin), nameof(OnJunkDroneDrop));
            // The Drifter's Hoard gives back items players put into it: never redistribute them.
            HookUtil.Add(GameMembers.Destroy_FixedUpdate, typeof(Plugin), nameof(OnHoardDestroy));

            // The item chosen from a command cube, Void Potential or other "choose one" orb (the orbs themselves never move).
            HookUtil.Add(GameMembers.PickupPickerController_CreatePickup, typeof(Plugin), nameof(OnPickerCreatePickup));

            // Events without an owner.
            HookUtil.Add(GameMembers.BossGroup_DropRewards, typeof(Plugin), nameof(OnBossRewards));
            HookUtil.Add(GameMembers.WakeController_DropShamanRewards, typeof(Plugin), nameof(OnWakeRewards));
            HookUtil.Add(GameMembers.SpawnAltarZoneController_DropItems, typeof(Plugin), nameof(OnAltarZoneRewards));

            // Per-stage and per-run state.
            Stage.onServerStageBegin += _ => { interactors.Clear(); Distributor.OnStageBegin(); };
            Stage.onServerStageComplete += _ => Distributor.FlushInFlight();
            SceneExitController.onFinishExit += _ => Distributor.FlushInFlight();
            Run.onRunStartGlobal += _ => { interactors.Clear(); Distributor.Reset(); };
            Run.onRunDestroyGlobal += _ => { interactors.Clear(); extractorVictims.Clear(); Distributor.FlushInFlight(); Distributor.Reset(); };

            Log.Info($"{NAME} {VERSION} ready.");
        }

        // Command cubes still flying at the end of the last stage: dropped once their player is on the new stage.
        private void FixedUpdate() => Distributor.SpawnCarriedOver();

        private static void OnSacrificeDeath(Action<DamageReport> orig, DamageReport report)
        {
            using (new Scope(Source.MonsterKill, report?.attackerMaster)) orig(report);
        }

        private static void OnGlobalDeathRewards(Action<GlobalDeathRewards, DamageReport> orig, GlobalDeathRewards self, DamageReport report)
        {
            using (new Scope(Source.MonsterKill, report?.attackerMaster)) orig(self, report);
        }

        // Sonorous Whispers drops in the middle of GlobalEventManager.OnCharacterDeath, which also runs every death handler
        // (Sacrifice, Vengeance, trials...). So the whole method isn't a source: only the droplet right after a roll on the
        // Sonorous Whispers drop table is.
        private static CharacterMaster deathAttacker;
        private static bool inCharacterDeath, sonorousNext;

        private static void OnCharacterDeath(Action<GlobalEventManager, DamageReport> orig, GlobalEventManager self, DamageReport report)
        {
            CharacterMaster prevAttacker = deathAttacker;
            bool prevIn = inCharacterDeath;
            deathAttacker = report?.attackerMaster;
            inCharacterDeath = true;
            try { orig(self, report); }
            finally
            {
                deathAttacker = prevAttacker;
                inCharacterDeath = prevIn;
                sonorousNext = false;
            }
        }

        private static UniquePickup OnGeneratePickup(Func<PickupDropTable, Xoroshiro128Plus, UniquePickup> orig, PickupDropTable self, Xoroshiro128Plus rng)
        {
            UniquePickup result = orig(self, rng);
            if (inCharacterDeath && self && self == GlobalEventManager.CommonAssets.dtSonorousEchoPath) sonorousNext = true;
            return result;
        }

        // Chests, lunar pods, void cradles, lockboxes and scavenger bags all drop through ChestBehavior; the prefab tells them apart.
        private static void OnChestItemDrop(Action<ChestBehavior> orig, ChestBehavior self)
        {
            // Artifact of Delusion, a reset chest with a guess made: a correct guess drops the reward through the PickupIndex
            // overload of CreatePickupDroplet, without the Delusion flag. It belongs to the player who guessed: not distributed.
            if (DelusionChestController.isDelusionEnable && self.TryGetComponent(out DelusionChestController delusion)
                && delusion.SelectedPickupIndex != PickupIndex.none)
            {
                orig(self);
                return;
            }
            using (new Scope(ChestSource(self.gameObject), OwnerOf(self.gameObject))) orig(self);
        }

        private static Source ChestSource(GameObject go)
        {
            string name = PrefabName(go);
            if (name.Contains("backpack")) return Source.ScavengerBag;      // ScavBackpack, ScavLunarBackpack
            if (name.Contains("lunarchest")) return Source.LunarPod;        // LunarChest, Bazaar_LunarChest
            if (name.StartsWith("voidchest")) return Source.VoidCradle;
            if (name == "lockbox") return Source.RustedLockbox;
            if (name == "lockboxvoid") return Source.EncrustedLockbox;
            return Source.Chest;
        }

        // Printers, multishops, cauldrons, Cleansing Pools and the Bazaar shop all drop through ShopTerminalBehavior.
        private static Source ShopSource(GameObject go)
        {
            string name = PrefabName(go);
            if (name.Contains("duplicator")) return Source.Printer;         // Duplicator, DuplicatorLarge, -Military, -Wild
            if (name.Contains("cauldron")) return Source.Cauldron;          // "LunarCauldron, WhiteToGreen", HallowedCauldron
            if (name.Contains("shrinecleanse")) return Source.CleansingPool;
            if (name.StartsWith("lunarshopterminal")) return Source.LunarShop;
            return Source.Multishop;
        }

        /// <summary>Lower-case prefab name without "(Clone)".</summary>
        private static string PrefabName(GameObject go)
        {
            if (!go) return string.Empty;
            string name = go.name;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) name = name.Substring(0, clone);
            return name.Trim().ToLowerInvariant();
        }

        private static void OnRouletteEject(Action<RouletteChestController, UniquePickup> orig, RouletteChestController self, UniquePickup pickup)
        {
            using (new Scope(Source.AdaptiveChest, OwnerOf(self.gameObject))) orig(self, pickup);
        }

        private static void OnScavBackpackDrop(Action<ScavBackpackBehavior> orig, ScavBackpackBehavior self)
        {
            using (new Scope(Source.ScavengerBag, OwnerOf(self.gameObject))) orig(self);
        }

        private static void OnShrineChance(Action<ShrineChanceBehavior, Interactor> orig, ShrineChanceBehavior self, Interactor activator)
        {
            CharacterBody body = activator ? activator.GetComponent<CharacterBody>() : null;
            using (new Scope(Source.ShrineOfChance, body ? body.master : null)) orig(self, activator);
        }

        private static void OnShopDrop(Action<ShopTerminalBehavior, bool> orig, ShopTerminalBehavior self, bool duplicated)
        {
            using (new Scope(ShopSource(self.gameObject), OwnerOf(self.gameObject))) orig(self, duplicated);
        }

        private static void OnScrapperDrop(Action<EntityStates.Scrapper.ScrappingToIdle> orig, EntityStates.Scrapper.ScrappingToIdle self)
        {
            using (new Scope(Source.Scrapper, OwnerOf(self.outer ? self.outer.gameObject : null))) orig(self);
        }

        // Drone Scrapper: one scrap per DropPickup call (FixedUpdate, several states for an upgraded drone).
        // Owner = the interactor the scrapper recorded when the player opened its panel.
        private static readonly FieldInfo droneScrapperInteractor = GameMembers.DroneScrapperController_interactor.Find();

        private static void OnDroneScrapperDrop(Action<EntityStates.DroneScrapper.DroneScrappingToIdle> orig,
                                                EntityStates.DroneScrapper.DroneScrappingToIdle self)
        {
            CharacterMaster owner = null;
            if (NetworkServer.active && droneScrapperInteractor != null)
            {
                var controller = self.outer ? self.outer.GetComponent<DroneScrapperController>() : null;
                var interactor = controller ? droneScrapperInteractor.GetValue(controller) as Interactor : null;
                CharacterBody body = interactor ? interactor.GetComponent<CharacterBody>() : null;
                owner = body ? body.master : null;
            }
            using (new Scope(Source.DroneScrapper, owner)) orig(self);
        }

        private static void OnSolusVendorDrop(Action<EntityStates.SolusVendorShrine.DropItem> orig, EntityStates.SolusVendorShrine.DropItem self)
        {
            using (new Scope(Source.CharityNode, OwnerOf(self.outer ? self.outer.gameObject : null))) orig(self);
        }

        private static void OnBeginCooking(Action<MealPrepController, Interactor, PickupIndex[], PickupIndex, int> orig, MealPrepController self,
                                           Interactor activator, PickupIndex[] itemsToTake, PickupIndex reward, int count)
        {
            // The crafting menu doesn't go through Interactor.PerformInteraction, so record the cook here.
            if (NetworkServer.active && activator && activator.TryGetComponent(out CharacterBody body)) interactors[self.gameObject] = body.master;
            orig(self, activator, itemsToTake, reward, count);
        }

        private static void OnKeySpawner(Action<PowerOrbKeySpawner, DamageReport> orig, PowerOrbKeySpawner self, DamageReport report)
        {
            using (new Scope(Source.CraterKeys, report?.attackerMaster)) orig(self, report);
        }

        private static void OnKeyEventIdle(Action<EntityStates.Missions.RepurposedCrater.PowerOrbEventIdle, DamageReport> orig,
                                           EntityStates.Missions.RepurposedCrater.PowerOrbEventIdle self, DamageReport report)
        {
            using (new Scope(Source.CraterKeys, report?.attackerMaster)) orig(self, report);
        }

        private static void OnKeyEventSpawnOrb(Action<EntityStates.Missions.RepurposedCrater.PowerOrbEventSpawnOrb, DamageReport> orig,
                                               EntityStates.Missions.RepurposedCrater.PowerOrbEventSpawnOrb self, DamageReport report)
        {
            using (new Scope(Source.CraterKeys, report?.attackerMaster)) orig(self, report);
        }

        internal delegate void ItemGrantOrig(ref PickupDef.GrantContext context);

        // The game skips the Duplicator copy when the pickup is a printer's output (controller.Duplicated). For Hex and key
        // items the pickup is marked that way just for this call. Mods that read the flag around GenericPickupController.AttemptGrant
        // (Just Drop Items) see the real value: this runs inside it. Items this mod gives without a controller skip the
        // Duplicator in Distributor.Deliver instead.
        private static void OnItemAttemptGrant(ItemGrantOrig orig, ref PickupDef.GrantContext context)
        {
            GenericPickupController controller = context.controller;
            PickupDef def = PickupCatalog.GetPickupDef(context.pickup.pickupIndex);
            bool mark = NetworkServer.active && controller && !controller.Duplicated && def != null &&
                        Settings.BlocksDuplicatorCopy(def.itemIndex);
            if (mark) controller.NetworkDuplicated = true;
            try { orig(ref context); }
            finally { if (mark && controller) controller.NetworkDuplicated = false; }
        }

        private static void OnMimicDrop(Action<EntityStates.Mimic.Death> orig, EntityStates.Mimic.Death self)
        {
            CharacterMaster killer = null;
            if (self.outer && self.outer.TryGetComponent(out HealthComponent health) && health.lastHitAttacker &&
                health.lastHitAttacker.TryGetComponent(out CharacterBody attacker))
                killer = attacker.master;
            using (new Scope(Source.Mimic, killer)) orig(self);
        }

        // Solus Extractor: the player whose item it copies, recorded when it copies (its stolen items are given back before it drops).
        private static readonly Dictionary<EntityStates.ExtractorUnit.ItemDuplicatorController, CharacterMaster> extractorVictims =
            new Dictionary<EntityStates.ExtractorUnit.ItemDuplicatorController, CharacterMaster>();
        private static readonly FieldInfo duplicatorStealer = GameMembers.ItemDuplicatorController__itemStealController.Find();
        private static readonly FieldInfo stolenInfos = GameMembers.ItemStealController_stolenInventoryInfos.Find();
        private static readonly FieldInfo stolenVictim = GameMembers.StolenInventoryInfo_victimMaster.Find();

        private static void OnExtractorDuplicate(Action<EntityStates.ExtractorUnit.ItemDuplicatorController, ItemIndex> orig,
                                                 EntityStates.ExtractorUnit.ItemDuplicatorController self, ItemIndex item)
        {
            orig(self, item);
            if (!NetworkServer.active) return;
            try
            {
                // It copies the first stolen item, which belongs to the first stolen inventory.
                if (duplicatorStealer?.GetValue(self) is ItemStealController stealer && stolenInfos?.GetValue(stealer) is Array infos &&
                    infos.Length > 0 && infos.GetValue(0) is object info &&
                    stolenVictim?.GetValue(info) is CharacterMaster victim && victim)
                    extractorVictims[self] = victim;
            }
            catch (Exception e)
            {
                Log.Warn("Solus Extractor: couldn't find the robbed player. " + e.Message);
            }
        }

        private static void OnExtractorDestroy(Action<EntityStates.ExtractorUnit.ItemDuplicatorController> orig,
                                               EntityStates.ExtractorUnit.ItemDuplicatorController self)
        {
            extractorVictims.TryGetValue(self, out CharacterMaster victim);
            extractorVictims.Remove(self);
            using (new Scope(Source.SolusExtractor, victim)) orig(self);
        }

        private static void OnDrifterSalvage(Action<EntityStates.Drifter.Salvage> orig, EntityStates.Drifter.Salvage self)
        {
            using (new Scope(Source.Drifter, MasterOf(self))) orig(self);
        }

        /// <summary>The master of the character an entity state runs on.</summary>
        private static CharacterMaster MasterOf(EntityStates.EntityState state) =>
            state.outer && state.outer.TryGetComponent(out CharacterBody body) ? body.master : null;

        private static void OnTinkerTransmute(Action<RoR2.Projectile.TinkerProjectile, GameObject> orig, RoR2.Projectile.TinkerProjectile self, GameObject target)
        {
            CharacterMaster owner = null;
            if (self.TryGetComponent(out RoR2.Projectile.ProjectileController projectile) && projectile.owner &&
                projectile.owner.TryGetComponent(out CharacterBody body))
                owner = body.master;
            using (new Scope(Source.Drifter, owner)) orig(self, target);
        }

        private static void OnJunkDroneDrop(Action<EntityStates.Drone.DroneJunk.Surprise, Vector3> orig, EntityStates.Drone.DroneJunk.Surprise self, Vector3 velocity)
        {
            // The drone's master; Owner walks minion ownership up to the player who owns the drone.
            using (new Scope(Source.JunkDrone, MasterOf(self))) orig(self, velocity);
        }

        private static void OnHoardDestroy(Action<EntityStates.Drifter.Hoard.Destroy> orig, EntityStates.Drifter.Hoard.Destroy self)
        {
            using (new Scope(Source.Ignored, null)) orig(self);
        }

        private static void OnChefDrop(Action<EntityStates.MealPrep.CookingToIdle> orig, EntityStates.MealPrep.CookingToIdle self)
        {
            using (new Scope(Source.WanderingChef, OwnerOf(self.outer ? self.outer.gameObject : null))) orig(self);
        }

        private static void OnBossRewards(Action<BossGroup> orig, BossGroup self)
        {
            using (new Scope(Source.Boss, null)) orig(self);
        }

        private static void OnWakeRewards(Action<WakeController, int> orig, WakeController self, int killed)
        {
            using (new Scope(Source.Wake, null)) orig(self, killed);
        }

        private static void OnAltarZoneRewards(Action<SpawnAltarZoneController> orig, SpawnAltarZoneController self)
        {
            using (new Scope(Source.TotemOfSacrifice, null)) orig(self);
        }

        private static void OnPickerCreatePickup(Action<PickupPickerController, UniquePickup> orig, PickupPickerController self, UniquePickup pickup)
        {
            if (Settings.Enabled && Settings.AutoPickupChoices && NetworkServer.active)
            {
                Distributor.HandedOver = false;
                try
                {
                    var prompt = self.GetComponent<NetworkUIPromptController>();
                    if (prompt && Distributor.GiveChoice(prompt.currentParticipantMaster, pickup, self.transform.position,
                                                     self.chestGeneratedFrom)) return;
                }
                catch (Exception e)
                {
                    Log.Error(e);
                    if (Distributor.HandedOver) return; // the mod already has the item: no vanilla drop
                }
            }
            // The vanilla drop of a chosen item stays where the player chose it, even with UnknownSources on.
            using (new Scope(Source.Ignored, null)) orig(self, pickup);
        }

        private static void OnPickupOrbEffectEnable(Action<RoR2.Orbs.PickupTakenOrbEffect> orig, RoR2.Orbs.PickupTakenOrbEffect self)
        {
            orig(self);
            CubeIconSwapper.Attach(self);
        }

        private static CharacterMaster OwnerOf(GameObject go)
        {
            return go && interactors.TryGetValue(go, out CharacterMaster m) ? m : null;
        }

        private static void OnPerformInteraction(Action<Interactor, GameObject> orig, Interactor self, GameObject target)
        {
            if (!NetworkServer.active || !target)
            {
                orig(self, target);
                return;
            }

            // Record before the call: shops and shrines drop synchronously inside it.
            // If the interaction didn't happen (busy printer, not enough gold), restore the previous user.
            bool hadPrev = interactors.TryGetValue(target, out CharacterMaster prev);
            CharacterBody body = self.GetComponent<CharacterBody>();
            interactors[target] = body ? body.master : null;
            interactionSucceeded = false;

            orig(self, target);

            if (!interactionSucceeded)
            {
                if (hadPrev) interactors[target] = prev;
                else interactors.Remove(target);
            }
        }

        private static void OnCreatePickupDroplet(
            Action<GenericPickupController.CreatePickupInfo, Vector3, Vector3> orig,
            GenericPickupController.CreatePickupInfo info, Vector3 position, Vector3 velocity)
        {
            Source source = ctxSource;
            CharacterMaster owner = ctxOwner;
            if (sonorousNext)
            {
                sonorousNext = false;
                source = Source.SonorousWhispers;
                owner = deathAttacker;
            }
            else if (source == Source.None && Settings.ModeFor(Source.Unknown) != DistributionMode.Disabled)
            {
                source = Source.Unknown; // a drop no source hook claimed
            }

            // Drops outside a handled source cost one field compare.
            if (source != Source.None && !Distributor.SpawningOwnDroplet && Settings.Enabled && NetworkServer.active)
            {
                Distributor.HandedOver = false;
                try
                {
                    if (Distributor.TryDistribute(source, owner, in info, position)) return;
                }
                catch (Exception e)
                {
                    Log.Error(e);
                    // Fall through to the vanilla drop, so the item is never lost, unless the mod already has it.
                    if (Distributor.HandedOver) return;
                }
            }
            orig(info, position, velocity);
        }
    }
}
