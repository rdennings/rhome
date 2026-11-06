# Test fixtures

Real game saves for automated tests (PLAN.md §6). Each `<name>.sav` has a `<name>.json` manifest next to it listing what it holds: game, trainer, party, and every Pokémon in the boxes. `tests/Rhome.Core.Tests/FixtureTests.cs` checks each save against its manifest and checks that writing it back without changes gives the same bytes.

## What's here

| File | Game | Emulator (Ayn Thor) | Contents |
|---|---|---|---|
| `emerald.sav` | Emerald (US) | RetroArch mGBA | 1 in party; a Kirlia in box 1 that was moved from Platinum by `prototypes/transfer_pt_to_em.py` (its OT is the Platinum trainer) |
| `platinum-us.sav` | Platinum (US, Rev 1) | melonDS | 6 in party, 5 in box 1, 15 hours in |
| `platinum-eu.sav` | Platinum (Europe, English) | melonDS | A new game saved once: no Pokémon, and the backup half is still blank |
| `white.sav` | White (US/Europe) | melonDS | 6 in party, 4 in box 1 |

Not covered yet: Ruby/Sapphire, FireRed/LeafGreen, Diamond/Pearl, HeartGold/SoulSilver, and Black 2/White 2.

The Emerald and Platinum manifests match what the Python readers in `prototypes/` report for the same files. There's no Python reader for Gen 5, so the White manifest comes from PKHeX alone.

## These are scrubbed personal saves

The saves were played normally, then run through `tools/Rhome.FixtureScrubber`, which removes what identifies the player:

- The trainer name becomes `TEST`, and a rival the player named (Platinum, FireRed/LeafGreen) becomes `RIVAL`.
- Trainer IDs are replaced with random ones. Each old ID maps to the same new one across all saves scrubbed together, so a Pokémon moved between two of the player's games still matches. TID xor SID stays the same, so no Pokémon changes shininess.
- Every Pokémon whose OT is the player gets the new name and ID. Pokémon from other trainers are kept as they are, and the tool lists them so you can check them.
- The game's backup copy, which PKHeX reads but doesn't rewrite, is overwritten with the scrubbed data. Gen 3 has two save slots, Gen 4 two copies of each block, and Black/White a mirror at `0x24000`.
- Anything PKHeX doesn't cover is found in the raw bytes and replaced. In Emerald, that was a TV show record holding the player's name and trainer ID.

Afterwards the tool checks that the old name and IDs appear nowhere in the file (backups included). It also checks that every Pokémon is unchanged apart from its OT, that checksums are valid, and that the backups match. If any check fails, it writes nothing.

Left as they were: nicknames, play time, money, items, story progress and the Pokédex.

## Adding a fixture

1. Make sure no Pokémon is in the day care or (Black/White) the Battle Box; the tool refuses those saves.
2. Scrub every save from the same player in one run (`<name>` becomes `<name>.sav` and `<name>.json`):

   ```bash
   dotnet run --project tools/Rhome.FixtureScrubber -- fixtures emerald=/path/to/emerald.srm platinum-us=/path/to/platinum.sav
   ```

3. Read the tool's report. Kept nicknames and Pokémon from other trainers may still carry real names.
4. Check the manifest against the game itself, or against a reader in `prototypes/`.
5. `.gitignore` blocks save files, so add approved fixtures explicitly: `git add -f fixtures/<name>.sav`.

The tool handles Gen 3, Gen 4, and Black/White. Black 2/White 2 keep their backup somewhere else, and the tool refuses them until that's checked on a real save.
