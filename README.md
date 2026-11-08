# rhome

[![Join the rhome Discord](https://img.shields.io/badge/Discord-Join%20the%20community-5865F2?logo=discord&logoColor=white&style=for-the-badge)](https://discord.gg/Uq87sx6szh)

**A Pokémon bank for emulator and ROM-hack players.** Keep the Pokémon from every game you play in one personal collection, then bring the ones you want into your next adventure.

> **Status: planning / early development.** There's no app to download yet. The plan is in [docs/PLAN.md](docs/PLAN.md), and work is starting on milestone M0 (foundations). "rhome" is a working name; the planning docs still call it romHome.

## What it does (planned)

- **Reads your save files** from the official Pokémon games you play on emulators.
- **Moves Pokémon** between a game and your bank in a split view, like Pokémon HOME. Nothing changes until you press Save, and your game save is backed up before every write.
- **Never loses data.** When a Pokémon visits a game that can't hold everything it has (newer moves, ribbons, hidden abilities), the bank keeps it and restores it when the Pokémon comes back.
- **Treats arrivals properly.** A Pokémon moved into a game is registered in the Pokédex and set up as if it was caught or traded there.
- **Living dex and boxes.** Name and organize boxes; see your whole collection as a National Dex.

See [docs/SCOPE.md](docs/SCOPE.md) for the full product vision.

## Roadmap

| Milestone | What |
|---|---|
| **MVP** (M0–M3) | Android. All GBA and DS games: Gen 3 (Ruby, Sapphire, Emerald, FireRed, LeafGreen), Gen 4 (Diamond, Pearl, Platinum, HeartGold, SoulSilver), Gen 5 (Black, White, Black 2, White 2). |
| v1.1 | 3DS and Switch games; Gen 1/2 one-way import |
| v1.2 | Linux release, watched save folders, box groups, dex layers |
| v1.3 | Accounts and cloud sync |
| v1.4 | Web viewer and shareable showcase |
| v2 | ROM hacks |

Windows and Mac follow the Linux release. Details and reasoning in [docs/PLAN.md](docs/PLAN.md).

## Tech

- **C# / .NET** with [PKHeX.Core](https://github.com/kwsch/PKHeX) for save and Pokémon formats
- **Avalonia UI** for Android now, Linux and other platforms later
- A UI-free, platform-free core so the app can follow emulation to any platform

The [`prototypes/`](prototypes/) folder holds the Python scripts that proved save reading and transfers work, including in-game tests on real emulators. They're reference material, not the app.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). From the repo root:

```bash
dotnet test --solution Rhome.slnx
```

That builds the core (`src/Rhome.Core`) and runs its tests (`tests/`). The Android app isn't in the solution yet; the experiments that proved it works are in [`spikes/`](spikes/).

### Command line

`tools/Rhome.Cli` drives the core without a UI. For now it only reads: it lists what a GBA or DS save holds and never changes the file.

```bash
dotnet run --project tools/Rhome.Cli -- save show fixtures/platinum-us.sav
```

Bank, import and withdraw commands join it as the M1 core pieces land.

## Community

Come say hi, ask questions or share ideas on the [rhome Discord](https://discord.gg/Uq87sx6szh). Activity from this repo is posted there too.

## Contributing

Contributions and ideas are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) first; the short version is to open an issue or discussion before starting on anything big.

## Legal

rhome is a fan project. It is **not affiliated with, endorsed by, or sponsored by** Nintendo, Game Freak, Creatures Inc., or The Pokémon Company. Pokémon and all related names are trademarks of their respective owners.

rhome does **not** include or distribute ROMs, BIOS files, or game assets, and never uploads your ROMs. Use it with games you own.

Writing data into a save always carries risk. rhome is built for emulators; importing data into games on official hardware is not recommended, and the authors are not responsible for lost or corrupted saves or any action taken against your account.

## License

[GPL-3.0](LICENSE). rhome builds on PKHeX.Core, which is GPLv3, and some prototype code is ported from PKHeX.
