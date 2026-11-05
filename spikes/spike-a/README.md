# Spike A: confirm the stack

Issue #1 · [PLAN.md §3, M0](../../docs/PLAN.md). Question: does **C# + PKHeX.Core + Avalonia** work for rhome, on the Ayn Thor (Android 13) in particular?

**Result: yes, the stack is confirmed.** Details below. This folder is throwaway spike code, not the product's structure (that's issue #3).

## What's here

| Project | What it is |
|---|---|
| `SpikeA.Report` | Plain .NET library: loads a save with PKHeX.Core and prints trainer, party and boxes in the same layout as the Python readers in `prototypes/`. Bytes in, text out; no UI, no Android, no file paths. |
| `SpikeA.Cli` | `save-report <save>...` — the report on the command line. |
| `SpikeA.App` | Shared Avalonia UI: shows startup time, reports every file in a saves folder, and has an **Open save…** button (system file picker). |
| `SpikeA.Android` | Android head (`io.github.rdennings.rhome.spikea`, arm64). |
| `SpikeA.Desktop` | Desktop head (Mac/Linux/Windows). |
| `compare.sh` | Runs the Python reader and `save-report` on each save and diffs them. |

## Results (2026-11-04)

### 1. PKHeX.Core reads the test saves exactly like the Python readers

`./compare.sh` on all Gen 3/4 test saves: **10/10 identical** (trainer, TID/SID, play time, money, Emerald Pokédex counts, every party and box slot with species, gender, level, nickname and moves).

| Save | Game | Result |
|---|---|---|
| 6 × Emerald (`.srm`, RetroArch/mGBA), incl. before/after the Kirlia + dex-fix transfer | E | identical |
| 4 × Platinum (`.sav`, melonDS), USA Rev 1 (old + current) and Europe | Pt | identical |
| Pokémon White (`.sav`, melonDS) | W | read OK (Snivy Lv 14 party of 6, 4 in Box 1, dex 15/10). No Python reader to diff against; needs an in-game check. |

### 2. Avalonia + PKHeX.Core run on the Thor

- Release APK installed with `adb install` and running on Android 13 (AYN Thor). It reads all 6 test saves on the device, and its report is **byte-identical to the Mac's** (312 lines) — no trimming or Android-only problems with PKHeX.Core.
- Installed over adb even though "apps from unverified developers" is switched off on the Thor: that check doesn't apply to adb installs.

### 3. Size and speed

| | Thor (Release, arm64) |
|---|---|
| APK size | **31 MB** (arm64 only, no trimming tweaks) |
| Installed size | ~96 MB |
| Cold start, tap → first frame (`am start -W`) | **590–620 ms** (first-ever launch 723 ms) |
| Process start → first Avalonia frame (in-app) | 517–550 ms |
| Load + parse one save (PKHeX.Core) | first save 3 ms, then < 1 ms each |
| Memory (PSS after loading 6 saves) | ~247 MB |

On the Mac, the desktop head (Release, JIT) takes ~1.3 s to its first frame and reads the same saves.

## Notes and follow-ups

- **Size/startup are fine for the MVP.** If we ever want them smaller: trimming PKHeX.Core's unused resources, and ReadyToRun/AOT profiling. Not worth doing now.
- **Memory** (~250 MB) is fine on the Thor. Keep an eye on it for small Linux handhelds (PLAN R9).
- **Gen 5** needs a reference: check the White report above in-game (party/box), since there's no Python reader for it. Covers part of issue #5.
- The **Open save…** button uses the Android system file picker. Whether it can reach `Android/data/` (melonDS) and write in place is Spike B (#2), not tested here.
- The report font falls back to a proportional font on Android (no "Menlo/Consolas/monospace" match); cosmetic, spike only.

## Running it yourself

Needs the .NET 10 SDK. Android builds also need the `android` workload and an Android SDK (`dotnet build SpikeA.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory=$HOME/Library/Android/sdk -p:AcceptAndroidSDKLicenses=True`).

```bash
./compare.sh path/to/saves/*.srm path/to/saves/*.sav
dotnet run --project SpikeA.Cli -- "path/to/save.sav"
dotnet publish SpikeA.Android -c Release -f net10.0-android -p:AndroidSdkDirectory=$HOME/Library/Android/sdk
adb install -r SpikeA.Android/bin/Release/net10.0-android/publish/io.github.rdennings.rhome.spikea-Signed.apk
```

To make the Android app read saves at startup, copy them into its own folder. Use `cat` rather than `adb push` straight into `Android/data` (see the in-place-write lesson in `prototypes/README.md`):

```bash
F=/sdcard/Android/data/io.github.rdennings.rhome.spikea/files
adb shell mkdir -p $F/saves
adb push save.sav /data/local/tmp/ && adb shell "cat /data/local/tmp/save.sav > $F/saves/save.sav"
adb pull $F/report.txt
```
