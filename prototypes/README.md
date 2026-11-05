# Prototypes (Python reference scripts)

These scripts were written before the real app to prove the core ideas work: reading Pokémon save files and moving a Pokémon from one game into another. Every transfer here was tested in-game on emulators (melonDS, RetroArch/mGBA, Azahar, Eden) on an Android handheld.

They are **reference material, not part of the app.** The app is being built in C# on top of PKHeX.Core (see [docs/PLAN.md](../docs/PLAN.md)). Use these to check the app's output against known-good results and to see the format notes at the top of each file.

## What's here

| Script | What it does |
|---|---|
| `read_em_save.py` | Read an Emerald save (Gen 3) |
| `read_pt_save.py` | Read a Platinum save (Gen 4) |
| `read_xy_save.py` | Read an X/Y save (Gen 6, decrypted emulator save) |
| `read_swsh_save.py` | Read a Sword/Shield save (Gen 8) |
| `read_sv_save.py` | Read a Scarlet/Violet save (Gen 9) |
| `swish_crypto.py` | Switch save encryption, used by the Gen 8/9 readers |
| `transfer_pt_to_em.py` | Platinum → Emerald (a downgrade; our own conversion) |
| `transfer_pt_to_xy.py` | Platinum → X/Y, the way Poké Transfer + Poké Transporter would |
| `transfer_pt_to_swsh.py` | Platinum → Sword/Shield, via the HOME route |
| `transfer_pt_to_sv.py` | Platinum → Scarlet/Violet, via the HOME route |
| `fix_affixed_ribbon.py` | Repair tool for an early bug (see lessons below) |
| `gen*_data.py` | Lookup tables (species, moves, stats) from PokeAPI and PKHeX |

## Running

Python 3, no dependencies. Run from this folder, for example:

```bash
python3 read_pt_save.py "path/to/Pokemon - Platinum Version (USA).sav"
```

Transfer scripts never modify the source save and always write the target to a new file. **Save files are not included in this repo**; bring your own, and back them up first.

## Lessons these scripts taught us

- A Pokémon placed into a box isn't enough: the game's own bookkeeping (Pokédex seen/owned flags, including duplicate copies some games keep) must be done too.
- What counts as "owned" can depend on origin: X/Y uses a separate flag for Pokémon from Gen 3–5 games.
- Copy PKHeX's constructor defaults when building a Pokémon from zeroed bytes: Gen 8/9's "affixed ribbon" must be `0xFF` (none), or the game shows a Kalos Champion Ribbon title.
- On Android, overwrite an emulator's save **in place**. Deleting and recreating the file can leave the emulator unable to write it, and the game hangs on load.

## License

GPL-3.0, like the rest of the repo. Several scripts port logic and constants from [PKHeX](https://github.com/kwsch/PKHeX) (GPLv3); the file headers say where.
