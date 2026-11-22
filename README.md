# AutoLootDistribution

Ends the "whose item is this?" question in multiplayer, especially with the **Artifact of Sacrifice**.
A dropped item flies to the player who earned it, with the same animation as items taken by Mithrix or the Hex of Greed,
and is picked up automatically.

**Host-side**: only the host needs the mod.

A more detailed description of the mod, with screenshots and all settings, is on its [Thunderstore page](https://thunderstore.io/c/riskofrain2/p/H1xl/AutoLootDistribution/).

- **Download:** [Thunderstore](https://thunderstore.io/c/riskofrain2/p/H1xl/AutoLootDistribution/)
- **Features and settings:** [package/README.md](package/README.md)
- **Changes:** [CHANGELOG](package/CHANGELOG.md) (released versions) · [DEVLOG](docs/DEVLOG.md) (development history)
- **Bugs and ideas:** [Issues](https://github.com/H1xl/AutoLootDistribution/issues)

## Building
Requirements: .NET SDK 8 or newer, Risk of Rain 2, and a mod-manager profile (r2modman or Thunderstore Mod Manager)
with BepInEx and [Risk Of Options](https://thunderstore.io/c/riskofrain2/p/Rune580/Risk_Of_Options/) installed
(the plugin references it; for players it stays optional).

1. Copy `paths.local.props.example` to `paths.local.props` and set `GameDir` (the game folder), `ProfilesDir` (the
   mod manager's profiles folder) and `RefProfile` (the profile name).
2. `dotnet build plugin -c Release` → `plugin/bin/Release/AutoLootDistribution.dll`.
3. Copy the dll into `<profile>/BepInEx/plugins/AutoLootDistribution/`, together with the Artifact of Command cube
   icons `assets/command_cube_icons/cube_*.png` (except `cube_void.png`, unused). Without them the mod works, and
   flying command cubes keep the vanilla icons.

## Tests
`dotnet test tests/unit` — the mod's pure logic (`plugin/Logic/`: recipient choice, item filter), no game needed.

## Repository layout
| Folder | Contents |
|---|---|
| `plugin/` | BepInEx plugin source (`netstandard2.1`) |
| `shared/` | code shared by H1xl's mods, compiled into the plugin |
| `package/` | Thunderstore files: manifest, README, CHANGELOG, icon |
| `tests/unit/` | unit tests |
| `assets/` | Artifact of Command cube icons shown on flying cubes |
| `docs/` | development log, README images |

## License
[MIT](LICENSE): the code. Not covered: the mod icon ("Mine." cat picture, via Lol Damn at cheezburger.com); the cube
icons (renders of the Artifact of Command cube from Risk of Rain 2 © Hopoo Games / Gearbox).

## Credits
- Mod icon: "Mine." cat picture, via **Lol Damn** at [cheezburger.com](https://cheezburger.com)

This is a free fan mod and is not affiliated with Hopoo Games / Gearbox.
