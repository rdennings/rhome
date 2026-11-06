# Spike B: reach emulator saves on Android

Issue #2 · [PLAN.md §3, M0, risk R1](../../docs/PLAN.md). Question: can rhome read **and write in place** the saves that emulators keep in `Android/data/` (and `Android/media/`) on the Ayn Thor (Android 13), and find them on its own at launch, so the user gets a tappable list instead of browsing every time?

**Result: yes, using the normal system folder picker, no Shizuku.** Reading, scanning and writing in place all work on the Thor for melonDS, RetroArch and Eden. A written Emerald save loads in RetroArch with the change. The melonDS in-game check is still open, and it turned up a bigger issue: **melonDS may load a save next to the ROM instead of the one in `Android/data`**, so rhome must scan ROM folders too and handle two saves for one game (see "Finding" below). This folder is throwaway spike code, not the product's structure.

## How it works

1. rhome knows where each supported emulator keeps its saves by default (`SpikeB.Core/EmulatorProfile.cs`).
2. For each installed emulator, **Give access** opens the system folder picker (`ACTION_OPEN_DOCUMENT_TREE`) already inside that folder, using `EXTRA_INITIAL_URI`. The user taps **Use this folder**, then **Allow**. That's the whole setup, once per emulator.
3. rhome keeps the grant (`takePersistableUriPermission`). It survives app restarts and app updates.
4. On every launch rhome lists the granted folders, reads only files that are new or changed since last time, and identifies Pokémon saves with PKHeX.Core. New and changed saves go to the top of the list.
5. Writes open the existing file with mode `"rw"` and overwrite it from the start. They never delete, rename or truncate first, so the emulator still owns the same file. A backup is made before every write.

## Results on the Thor (2026-11-06)

Android 13, security patch 2024-01-01, built-in file picker `com.android.documentsui` 13 (the plain AOSP version, not Google's self-updating one).

### Folder access

| Emulator | Folder | Picker opens there? | Grant kept? |
|---|---|---|---|
| melonDS 2.0.1 | `Android/data/me.magnum.melonds/files/saves` | yes | yes |
| RetroArch 1.22.2 | `Android/media/com.retroarch/RetroArch/saves` | yes | yes |
| Eden | `Android/data/dev.eden.eden_emulator/files/nand/user/save` | yes | yes |

Android 13 refuses a grant for `Android/data` itself, but on this device it allows a grant for one app's folder **inside** it. Each grant reported exactly the folder we asked for (`primary:Android/data/me.magnum.melonds/files/saves`, and so on).

### Scanning

| | Files listed | Save files | Pokémon saves | Time |
|---|---|---|---|---|
| First scan after the melonDS grant (reads 14 files) | 22 | 14 | 3 | 483 ms |
| All three folders, first time for Eden (reads 4 files) | 45 | 29 | 10 | 319 ms |
| **Every later launch** (nothing changed, reads 0 files) | 45 | 29 | 10 | **112–120 ms** |

The scan found: Platinum ×2 and White (melonDS); Emerald, Blue and the Unbound hack, reported as FireRed (RetroArch); Sword, Scarlet, Legends: Arceus and Legends: Z-A (Eden). Listing the folders takes ~50 ms; the rest is reading and identifying new or changed files.

### Writing in place

- **Mac (local copies of the Thor's saves):** passes. "Write back unchanged" and "money +1" on Platinum (Europe) and Emerald: money went up by exactly 1 after reloading with PKHeX.Core, and the file kept the same inode and size. The backup matched the original byte for byte.
- **Thor:** passes. Both tests on Platinum (Europe) in melonDS and Emerald in RetroArch, through the granted folders:

  | Save | Write | Result | File after (`ls -lni`) |
  |---|---|---|---|
  | Platinum (Europe), melonDS | unchanged | same SHA-1 | inode 83872, uid 10114, gid 1078, `rw-rw----`: unchanged |
  | | money +1 | 3000 → 3001 | unchanged |
  | Emerald, RetroArch | unchanged | same SHA-1 | inode 159734, uid 10109, gid 1023, `rw-rw----`: unchanged |
  | | money +1 | 3556 → 3557 | unchanged |

  Each write's backup in the app's `backups/` folder matches the original byte for byte, and the results on the Thor match the Mac exactly (same SHA-1s).
- **In game:** Emerald loads in RetroArch (Continue screen: Eve, 0:47) and the trainer card shows **₽3557**. RetroArch then wrote the file itself (new timestamp, same contents and inode), so the emulator can still write a file rhome wrote.
- **melonDS in game: not confirmed, and it found something bigger (below).** The Thor has no Platinum (Europe) ROM, so the check used **White**: money +1 wrote correctly (2176 → 2177, inode 216034, uid 10114, `rw-rw----` unchanged, backup matches, PKHeX.Core reads 70/70 valid checksums). But launched from Cocoon, White showed **New Game** with no Continue, and it did the same with the **original** save restored. melonDS wasn't reading that file at all.

### Finding: melonDS doesn't always use `Android/data`

melonDS saves **next to the ROM** once it has write access to the ROM's folder, and only falls back to its own `Android/data/.../saves` folder otherwise. On the Thor:

- Cocoon launches melonDS the same way every time (`me.magnum.melonds.LAUNCH_ROM` with the ROM's `content://` link; see `/storage/12A8-6020/Cocoon/launch_debug.log`).
- melonDS got a persisted grant to `12A8-6020:ROMS/nds` on **2026-11-01 18:01** (`dumpsys activity permissions`).
- Before that grant: Platinum (10-27) and White (11-01, 16:49 to 20:22) saved to `Android/data`. After it: White's next launch (11-03 19:52) created an **empty** `ROMS/nds/…White….sav` next to the ROM, and the game now loads that one.
- So the same game can have **two saves** in two places, and the emulator decides which is live. Platinum (USA) has 15:22 in `Android/data` (the real run) and 0:04 next to the ROM (an old start from 10-09).

**Confirmed 2026-11-06:** after copying the `Android/data` saves next to the ROMs (old ones backed up first), White launched from Cocoon shows Continue (Eve, Striaton City, 5:01), and Platinum (USA) continues the 15:22 run (its journal shows the Oct. 27 session). melonDS loads the file next to the ROM.

What rhome needs because of this:

1. **Scan ROM folders too**, not just emulator folders. The SD card ROM folder is normal shared storage, so a plain folder grant covers it.
2. **Group saves by game.** When one game has more than one save, show all of them and say which one the emulator will load, or at least warn. If rhome edits the copy the emulator ignores, the change looks lost.
3. Prefer the **newest non-empty** file as the likely live one, but never decide silently.

### Testing note

melonDS ignores key events from `adb shell input`. Writing to the Thor's gamepad device works (the shell user is in the `input` group): `sendevent /dev/input/event9 1 <code> 1|0` plus `0 0 0`, holding about 0.4 s. A = `304` (0x130), Start = `315` (0x13b). The title screen accepted A, not Start.

## Things this spike found

- **PKHeX.Core changes the byte array you give it.** The first version backed up the save *after* loading it, so the "backup" already held the change. Back up before parsing, and give PKHeX.Core its own copy. The real app should test that backups match the original file.
- **Backups need unique names.** Two writes within the same second overwrote each other's backup; names now include milliseconds.
- The Unbound hack is detected as FireRed, and Pokémon Blue as "RB". Fine for now; ROM hacks are v2.

## Risks and fallback

- **The `Android/data/<app>` grant might stop working** if a system update blocks it (newer Android versions and Google's updatable file picker block it). The Thor has the plain built-in picker and an old security patch, so this needs an AYN update to change. If it does break:
  1. **Eden, RetroArch and Dolphin** share their own folders with the picker (they have a `DocumentsProvider`), so they'd still work.
  2. **melonDS** doesn't. Its fallback is the emulator setting that saves next to the ROMs, or **Shizuku**.
- If the user picks a different folder than the one we opened, the grant still works but the log says `NOT the expected …`. The real app should warn about that.
- Not tested yet: a full reboot (persistable grants are designed to survive one), and a phone with Google's updatable file picker.

## What's here

| Project | What it is |
|---|---|
| `SpikeB.Core` | Plain .NET: emulator profiles, `ISaveFolder`, the scanner (with "new / changed" tracking), and the in-place writer with backups. No Android code. |
| `SpikeB.Cli` | `save-scan <melonds\|retroarch\|eden> <folder> [state.json]` runs the same scan on a normal folder (Mac or Linux). |
| `SpikeB.App` | Avalonia UI: emulators with **Give access**, the "My saves" list, and the write test buttons. |
| `SpikeB.Android` | Android head (`io.github.rdennings.rhome.spikeb`): `SafFolderAccess.cs` holds all the picker and file-descriptor code. |

## Running it yourself

Same setup as Spike A (.NET 10 SDK, `android` workload, Android SDK).

```bash
dotnet run --project SpikeB.Cli -- melonds path/to/saves
dotnet publish SpikeB.Android -c Release -f net10.0-android -p:AndroidSdkDirectory=$HOME/Library/Android/sdk
adb install -r SpikeB.Android/bin/Release/net10.0-android/publish/io.github.rdennings.rhome.spikeb-Signed.apk
```

The app writes its log, last scan and backups to its own folder, so adb can pull them:

```bash
F=/sdcard/Android/data/io.github.rdennings.rhome.spikeb/files
adb shell cat $F/log.txt $F/scan.txt
adb shell ls $F/backups
```
