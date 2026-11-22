using H1xl.Shared;
using RoR2;
using RoR2.Artifacts;
using UnityEngine;

namespace AutoLootDistribution
{
    /// <summary>
    /// Every game member this mod reaches by name. Checked against the game's dlls by the level-1 test
    /// (_shared/tests/GameMembersTests); a missing one turns off only the feature that uses it.
    /// </summary>
    internal static class GameMembers
    {
        // ---------- hook targets (Plugin.Awake) ----------
        internal static readonly GameMethod PickupTakenOrbEffect_OnEnable =
            new GameMethod(typeof(RoR2.Orbs.PickupTakenOrbEffect), "OnEnable");
        internal static readonly GameMethod PickupDropletController_CreatePickupDroplet =
            new GameMethod(typeof(PickupDropletController), nameof(PickupDropletController.CreatePickupDroplet), typeof(GenericPickupController.CreatePickupInfo), typeof(Vector3), typeof(Vector3));
        internal static readonly GameMethod Interactor_PerformInteraction =
            new GameMethod(typeof(Interactor), "PerformInteraction", typeof(GameObject));
        internal static readonly GameMethod SacrificeArtifactManager_OnServerCharacterDeath =
            new GameMethod(typeof(SacrificeArtifactManager), "OnServerCharacterDeath", typeof(DamageReport));
        internal static readonly GameMethod GlobalDeathRewards_OnCharacterDeathGlobal =
            new GameMethod(typeof(GlobalDeathRewards), "OnCharacterDeathGlobal", typeof(DamageReport));
        internal static readonly GameMethod GlobalEventManager_OnCharacterDeath =
            new GameMethod(typeof(GlobalEventManager), "OnCharacterDeath", typeof(DamageReport));
        internal static readonly GameMethod PickupDropTable_GeneratePickup =
            new GameMethod(typeof(PickupDropTable), nameof(PickupDropTable.GeneratePickup), typeof(Xoroshiro128Plus));
        internal static readonly GameMethod ChestBehavior_ItemDrop =
            new GameMethod(typeof(ChestBehavior), nameof(ChestBehavior.ItemDrop));
        internal static readonly GameMethod RouletteChestController_EjectPickupServer =
            new GameMethod(typeof(RouletteChestController), "EjectPickupServer", typeof(UniquePickup));
        internal static readonly GameMethod ScavBackpackBehavior_ItemDrop =
            new GameMethod(typeof(ScavBackpackBehavior), nameof(ScavBackpackBehavior.ItemDrop));
        internal static readonly GameMethod ShrineChanceBehavior_AddShrineStack =
            new GameMethod(typeof(ShrineChanceBehavior), nameof(ShrineChanceBehavior.AddShrineStack), typeof(Interactor));
        internal static readonly GameMethod ShopTerminalBehavior_DropPickup =
            new GameMethod(typeof(ShopTerminalBehavior), nameof(ShopTerminalBehavior.DropPickup), typeof(bool));
        internal static readonly GameMethod ScrappingToIdle_OnEnter =
            new GameMethod(typeof(EntityStates.Scrapper.ScrappingToIdle), "OnEnter");
        internal static readonly GameMethod DroneScrappingToIdle_DropPickup =
            new GameMethod(typeof(EntityStates.DroneScrapper.DroneScrappingToIdle), "DropPickup");
        internal static readonly GameMethod DropItem_OnEnter =
            new GameMethod(typeof(EntityStates.SolusVendorShrine.DropItem), "OnEnter");
        internal static readonly GameMethod MealPrepController_BeginCookingServer =
            new GameMethod(typeof(MealPrepController), nameof(MealPrepController.BeginCookingServer), typeof(Interactor), typeof(PickupIndex[]), typeof(PickupIndex), typeof(int));
        internal static readonly GameMethod CookingToIdle_DropItem =
            new GameMethod(typeof(EntityStates.MealPrep.CookingToIdle), "DropItem");
        internal static readonly GameMethod PowerOrbKeySpawner_SpawnKey =
            new GameMethod(typeof(PowerOrbKeySpawner), nameof(PowerOrbKeySpawner.SpawnKey), typeof(DamageReport));
        internal static readonly GameMethod PowerOrbEventIdle_SpawnKey =
            new GameMethod(typeof(EntityStates.Missions.RepurposedCrater.PowerOrbEventIdle), "SpawnKey", typeof(DamageReport));
        internal static readonly GameMethod PowerOrbEventSpawnOrb_SpawnKey =
            new GameMethod(typeof(EntityStates.Missions.RepurposedCrater.PowerOrbEventSpawnOrb), "SpawnKey", typeof(DamageReport));
        internal static readonly GameMethod ItemDef_AttemptGrant =
            new GameMethod(typeof(ItemDef), nameof(ItemDef.AttemptGrant), typeof(PickupDef.GrantContext).MakeByRefType());
        internal static readonly GameMethod Death_SpawnDropletServer =
            new GameMethod(typeof(EntityStates.Mimic.Death), "SpawnDropletServer");
        internal static readonly GameMethod ItemDuplicatorController_DuplicateItem =
            new GameMethod(typeof(EntityStates.ExtractorUnit.ItemDuplicatorController), "DuplicateItem", typeof(ItemIndex));
        internal static readonly GameMethod ItemDuplicatorController_OnDestroy =
            new GameMethod(typeof(EntityStates.ExtractorUnit.ItemDuplicatorController), "OnDestroy");
        internal static readonly GameMethod Salvage_DropTempItemServer =
            new GameMethod(typeof(EntityStates.Drifter.Salvage), "DropTempItemServer");
        internal static readonly GameMethod TinkerProjectile_TransmuteTargetObject =
            new GameMethod(typeof(RoR2.Projectile.TinkerProjectile), nameof(RoR2.Projectile.TinkerProjectile.TransmuteTargetObject), typeof(GameObject));
        internal static readonly GameMethod Surprise_DropTempItemServer =
            new GameMethod(typeof(EntityStates.Drone.DroneJunk.Surprise), "DropTempItemServer", typeof(Vector3));
        internal static readonly GameMethod Destroy_FixedUpdate =
            new GameMethod(typeof(EntityStates.Drifter.Hoard.Destroy), "FixedUpdate");
        internal static readonly GameMethod PickupPickerController_CreatePickup =
            new GameMethod(typeof(PickupPickerController), nameof(PickupPickerController.CreatePickup), typeof(UniquePickup));
        internal static readonly GameMethod BossGroup_DropRewards =
            new GameMethod(typeof(BossGroup), "DropRewards");
        internal static readonly GameMethod WakeController_DropShamanRewards =
            new GameMethod(typeof(WakeController), "DropShamanRewards", typeof(int));
        internal static readonly GameMethod SpawnAltarZoneController_DropItems =
            new GameMethod(typeof(SpawnAltarZoneController), "DropItems");

        // ---------- private fields and properties ----------
        // Solus Extractor: the player whose stolen item it copies (Plugin.OnExtractorDuplicate)
        internal static readonly GameField ItemDuplicatorController__itemStealController =
            new GameField(typeof(EntityStates.ExtractorUnit.ItemDuplicatorController), "_itemStealController");
        internal static readonly GameField ItemStealController_stolenInventoryInfos =
            new GameField(typeof(ItemStealController), "stolenInventoryInfos");
        internal static readonly GameField StolenInventoryInfo_victimMaster =
            new GameField(GameMember.Nested(typeof(ItemStealController), "StolenInventoryInfo"), "victimMaster");
        // Drone Scrapper: the player who chose the drone (Plugin.OnDroneScrapperDrop)
        internal static readonly GameField DroneScrapperController_interactor =
            new GameField(typeof(DroneScrapperController), "interactor");
        // Not listed: BepInEx ConfigFile.OrphanedEntries (Settings.MigrateOldSources). BepInEx.dll can't be loaded by
        // the .NET 8 test, and BepInEx 5.4.21 is the last 5.x release, so it doesn't change under us.
    }
}
