# Changelog

## 1.0.0 — First release
- Host-side: dropped items fly to the player who earned them, with the item-transfer animation of Mithrix and the Hex
  of Greed, and are picked up automatically. Only the host needs the mod.
- Every source has its own setting: monster kills (Artifact of Sacrifice; kills by drones, turrets and other minions count
  for their owner), chests, Lunar Pods, Void Cradles, lockboxes, Adaptive Chests, Scavenger's Sacks, Shrines of Chance,
  3D Printers, Scrappers, Drone Scrappers, Multishop Terminals, Cauldrons, the Bazaar lunar shop, Cleansing Pools, Charity Nodes, the
  Wandering CHEF, Sonorous Whispers, Mimics, Solus Extractors, the Drifter, Junk Drones, Repurposed Crater keys, boss
  rewards, The Wake, the Totem of Sacrifice and unknown sources.
- Methods: `Owner`, `Nearest`, `Random`, `RoundRobin` (players take turns), `Balanced` (the player with the fewest items
  of that tier) or `Disabled`, with one shared `Fallback`.
- Artifact of Command: the command cube moves to the recipient's feet, and players with the mod see it as a cube in its
  tier colour.
- The item chosen from a command cube, Void Potential or other "choose one" orb flies to the player who chose it.
- Item filter by tier and an item list (blacklist or whitelist) for situational items; quest and key items can be left on the ground.
- A Wandering CHEF dish that the recipient's void item would corrupt stays on the ground for another player.
- No item is lost: items still flying when the stage ends are given at once; if the recipient leaves, the item drops.
- Works with [JustDropItems](https://thunderstore.io/c/riskofrain2/p/H1xl/JustDropItems/): printed and rerolled items
  keep their marks.
- Every setting can be changed in game without a restart with Risk Of Options (optional), or in the config file.
