# Development log

History of AutoLootDistribution before its first public release. During development the builds were numbered
1.0.0–1.0.5; none of them was published, so they are listed here as development stages `dev-N`.
Published versions are in [package/CHANGELOG.md](../package/CHANGELOG.md).

## dev-8 — 2026-11-21 (built as 1.0.0)
- New source `DroneScrappers` (`Owner`): the scrap a Drone Scrapper drops goes to the player who scrapped the drone.
- New defaults: `Tiers` includes `Boss`, `SkipObjectiveItems` is off, `VerboseLogging` is on.
- `VerboseLogging` moved from `1. General` to `7. Debug` (an old line is dropped; the new default applies).
- Internal (2026-11-21, optimization pass): no cube-icon coroutine on pickup orbs when the Artifact of Command or `CommandIcons` is off; the Just Drop Items API is called through a cached delegate; drop point helper `ItemFlight.DropPoint` (`_shared`).

## dev-7 — 2026-11-17 (built as 1.0.0)
Fixes from a code review:
- Fixed: an error while giving an item (for example in another mod's code) could stop the stage from changing after the teleporter, or hold up every flying orb in the game. It is now logged and the other items are still given.
- Fixed: after such an error the item could also drop on the ground, so it was duplicated.
- Fixed: with the Artifact of Delusion, a chest whose item was sent to a player offered no guess after the reset (also for items chosen from a command cube).
- Fixed: command cubes still flying when the stage ended were lost (equipment) or gave their item without the choice (items). They now drop next to the player on the next stage, as do items whose recipient is gone.
- Fixed: an item flying to a player who disconnected went into their unused inventory. It now drops on the ground where they were.
- Fixed: `Balanced` gave equipment command cubes by the count of internal items instead of by who holds an equipment.
- Fixed: an item chosen from a command cube turned back into a cube if its recipient left before it arrived.
- Fixed: with the Artifact of Delusion, the reward for a correct guess in a reset chest is no longer distributed: it drops for the player who guessed, as in the vanilla game.
- Internal: built through the code shared by H1xl's mods (logging, hooks, the Risk Of Options page, the item
  flight); a "ready." line in the log at startup.
- Internal (2026-11-18): game members reached by name are listed in `plugin/GameMembers.cs` and checked by a test
  against the game's assemblies after each game update.
- Internal (2026-11-18): the recipient choice and the item filter decisions moved to `plugin/Logic/` (no game calls) and are covered by unit tests (`tests/unit`, `dotnet test`).
- Internal (2026-11-18): automated in-game scenarios (not in this repository): 41 cases over every source that works in single player, the item filter, delivery settings, choices and the Artifact of Command. No mod change.
- Internal (2026-11-21): player docs rewritten to the docs guide (one table per config section, game names checked
  against the language files, Reporting problems).

## dev-6 — 2026-11-16 (built as 1.0.5)
- New setting `WanderingChefProtectFromCorruption` (on): a Wandering CHEF dish that the recipient's void item would corrupt
  stays on the ground, so another player can take it.
- New source `Mimics`: the chest reward of a killed Mimic (Hex of Mimicry) goes to the killer. Items a Mimic stole still go back
  to their owners on their own.
- New source `SolusExtractors`: temporary copies of stolen items dropped by a killed Solus Extractor go to the player it stole from.
- New source `Drifter`: temporary items the Drifter makes with Salvage and Tinker go to that Drifter.
  Items given back by the Drifter's Hoard are never moved.
- New source `JunkDrone`: temporary items from a Junk Drone's Surprise! go to the drone's owner.
- New source `CraterKeys`: Sentry Keys, Prison Matrices and Ocular Batteries dropped by monsters in the Repurposed Crater events go to the killer (only with `SkipObjectiveItems` off). Before, they always stayed on the ground.
- New section `7. Debug` with the setting `DuplicatorSkipsHexAndKeyItems` (off by default): when on, Substandard Duplicator no longer copies Hex items and quest/key items, which it does in the game.
- Fixed: Substandard Duplicator added a temporary copy to items from 3D printers. Printed items now get no copy, as when picked up from the ground.
- Fixed: with DLC4 items that can't be carried together, the pickup message named the removed item instead of the new one.
- Items from 3D printers and items rerolled by the Recycler keep these marks: a recipient who leaves mid-flight drops the item with them, and [JustDropItems](https://thunderstore.io/c/riskofrain2/p/H1xl/JustDropItems/) is told about them, so a dropped copy behaves as in the game (Drifter's Tinker, the Recycler).

## dev-5 (built as 1.0.4)
- New source `UnknownSources` (off by default): any other dropped item, such as other events or items dropped by other mods.
- The separate fallback of each source is replaced by one `Fallback` setting for all sources, at the end of the config.
- New defaults: lunar pods, void cradles, Encrusted Key lockboxes and the Bazaar lunar shop are off (their items stay on the ground).
- New default `ItemList`: Bustling Fungus, Rejuvenation Rack, Spare Drone Parts, Box of Dynamite, Lysate Cell, Shatterspleen,
  Rose Buckler, Ignition Tank, H3AD-5T v2, Purity, Collector's Compulsion.

## dev-4 — 2026-11-13 (built as 1.0.3)
- Every source now has its own setting, grouped in sub-sections: Monsters, Chests, Shrines, Shops and crafting, Events.
  `PrintersScrappersShops` is split into `Printers`, `Scrappers`, `Multishops`, `Cauldrons`, `BazaarLunarShop`, `CleansingPools`
  and `CharityNodes`; `Chests` into `Chests`, `LunarPods`, `VoidCradles`, `RustedLockboxes`, `EncrustedLockboxes` and
  `AdaptiveChests`; `OtherEventRewards` into `TheWake` and `TotemOfSacrifice`.
- New source `SonorousWhispers`: items from Sonorous Whispers go to the killer. Before, they always stayed on the ground.
- New source `WanderingChef`: dishes cooked by the Wandering CHEF go to the player who cooked them. Before, they stayed on the ground.
- Fixed: scavenger bags followed the `Chests` setting, so their items were distributed even with `ScavengerBags = Disabled`.

## dev-3 — 2026-11-13 (built as 1.0.2)
- Artifact of Command: players who also have the mod see a flying command cube as a cube in its tier colour (white, green, red,
  yellow, lunar, equipment, Hallowed, Hex, Page, and void with its sub-tier on the rim). Players without the mod keep the Scrap icons.
- New setting `CommandCubeIcons`: turns the cube icons on or off.
- New setting `AutoPickupChoices`: the item chosen from a command cube, Void Potential or other "choose one" orb flies to the
  player who chose it instead of dropping on the ground. Equipment still drops. The orbs themselves are never moved.

## dev-2 — 2026-11-12 (built as 1.0.1)
- Artifact of Command: a flying command cube shows Item Scrap of its tier instead of a random item of that tier, Strange Scrap
  for void tiers and a Fuel Array for equipment.
- Artifact of Command: equipment command cubes are moved to the player's feet too.

## dev-1 — 2026-11-12 (built as 1.0.0)
First complete build. Host-side: only the host needs the mod.
- Dropped items fly to a player with the item-transfer animation Mithrix and the Hex of Greed use, and are picked up automatically.
- Sources and their default recipients:
  - Monster kills (Artifact of Sacrifice): the killer. Kills by drones, turrets and other minions count for their owner.
  - Chests, lunar pods, void cradles and Adaptive Chests: the player who opened it.
  - Shrines of Chance: the player who used it.
  - 3D printers, scrappers, multishops, cauldrons and Cleansing Pools: the player who used it.
  - Scavenger bags, boss rewards and other event rewards (The Wake and others): off, they drop as usual.
- Methods for each source: `Owner`, `Nearest`, `Random`, `RoundRobin` (players take turns), `Balanced` (the player with the fewest
  items of that tier) or `Disabled`. A fallback method per source for when the first one finds nobody.
- Artifact of Command: the command cube moves to the recipient's feet, so the player still chooses the item.
- Item filter: by tier (white, green and red by default), quest and key items stay on the ground, and an item list of situational
  items that stay on the ground (blacklist or whitelist; not used in single player).
- Delivery settings: the animation and its duration, the pickup popup and chat line, items for dead players, nothing for players
  who left.
- No item is lost: items still flying when the stage ends are given at once; if the recipient leaves mid-flight, the item drops
  where it came from.
- Never touched: Void Potentials and other "choose one" orbs, the Artifact of Delusion, equipment and lunar coins.
- Every setting can be changed in game without a restart with Risk Of Options (optional), or in the config file.
