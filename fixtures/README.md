# Test fixtures

Real game saves for automated tests (PLAN.md §6). Each `<name>.sav` has a `<name>.json` manifest next to it listing what it holds: game, trainer, party, and every Pokémon in the boxes. `tests/Rhome.Core.Tests/FixtureTests.cs` checks each save against its manifest and checks that writing it back without changes gives the same bytes.

## What's here

| File | Game | Emulator (Ayn Thor) | Contents |
|---|---|---|---|
| `emerald.sav` | Emerald (US) | RetroArch mGBA | 1 in party; a Kirlia in box 1 that was moved from Platinum by `prototypes/transfer_pt_to_em.py` (its OT is the Platinum trainer) |
| `platinum-us.sav` | Platinum (US, Rev 1) | melonDS | 6 in party, 5 in box 1, 15 hours in |
| `platinum-eu.sav` | Platinum (Europe, English) | melonDS | A new game saved once: no Pokémon, and the backup half is still blank |
| `white.sav` | White (US/Europe) | melonDS | 6 in party, 4 in box 1 |
| `sapphire.sav` | Sapphire (US, v1.0) | RetroArch mGBA | A Mudkip in the party, 3 hours in |
| `leafgreen.sav` | LeafGreen (US, v1.0) | RetroArch mGBA | A Bulbasaur in the party, 1.5 hours in |
| `diamond.sav` | Diamond (US, Rev 5) | melonDS | A Monferno in the party, 1 hour 20 in |
| `soulsilver.sav` | SoulSilver (US/Australia) | melonDS | A Cyndaquil in the party, 45 minutes in |
| `white2.sav` | White 2 (US/Europe) | melonDS | An Oshawott in the party, 15 minutes in |

Every Tier 1 game has a fixture: one game from each pair that shares a save layout. The five newest hold one Pokémon each and empty boxes.

**A manifest's `game` is what the save itself can tell.** Paired games share a layout, so a Sapphire save only says `RS`, LeafGreen `FRLG` and Diamond `DP`. (PKHeX reports a FireRed/LeafGreen save as FireRed until told otherwise, so `SaveDetector` says `FRLG` instead.) The player's Pokémon do know their origin game: the Sapphire Mudkip was caught in Sapphire, and so on.

The Emerald, Platinum and Sapphire manifests match what the Python readers in `prototypes/` report for the same files. The readers only know the Emerald and Platinum layouts, so the LeafGreen, Diamond, SoulSilver, White and White 2 manifests come from PKHeX alone.

## These are scrubbed personal saves

The saves were played normally, then run through `tools/Rhome.FixtureScrubber`, which removes what identifies the player:

- The trainer name becomes `TEST`, and a rival the player named (Platinum, FireRed/LeafGreen) becomes `RIVAL`.
- Trainer IDs are replaced with random ones. Each old ID maps to the same new one across all saves scrubbed together, so a Pokémon moved between two of the player's games still matches. TID xor SID stays the same, so no Pokémon changes shininess.
- Every Pokémon whose OT is the player gets the new name and ID. Pokémon from other trainers are kept as they are, and the tool lists them so you can check them.
- The game's backup copy, which PKHeX reads but doesn't rewrite, is overwritten with the scrubbed data. Gen 3 has two save slots, Gen 4 two copies of each block, and Gen 5 a mirror right after the main data (`0x24000` for Black/White, `0x26000` for Black 2/White 2).
- Anything PKHeX doesn't cover is found in the raw bytes and replaced. In Emerald, that was a TV show record holding the player's name and trainer ID.

Afterwards the tool checks that the old name and IDs appear nowhere in the file (backups included). It also checks that every Pokémon is unchanged apart from its OT, that checksums are valid, and that the backups match. If any check fails, it writes nothing.

Left as they were: nicknames, play time, money, items, story progress and the Pokédex.

## Adding a fixture

1. Make sure no Pokémon is in the day care or (Gen 5) the Battle Box; the tool refuses those saves.
2. Scrub every save from the same player in one run (`<name>` becomes `<name>.sav` and `<name>.json`):

   ```bash
   dotnet run --project tools/Rhome.FixtureScrubber -- fixtures emerald=/path/to/emerald.srm platinum-us=/path/to/platinum.sav
   ```

3. Read the tool's report. Kept nicknames and Pokémon from other trainers may still carry real names.
4. Check the manifest against the game itself, or against a reader in `prototypes/`.
5. `.gitignore` blocks save files, so add approved fixtures explicitly: `git add -f fixtures/<name>.sav`.

The tool handles every Gen 3, 4 and 5 game. The Black 2/White 2 backup location was checked on a real White 2 save, where the first `0x26000` bytes and the copy after them were identical.
