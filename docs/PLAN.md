# romHome — Project Plan (DRAFT v1)

November 4, 2026 · status: **v1, reviewed** (all section 8 decisions settled)

This turns the product scope ([SCOPE.md](SCOPE.md)) into something buildable: an MVP, a milestone path to the full product, the technical approach, and the risks. The scope doc still describes the *final* product; this doc decides the *order*.

Decisions are recorded in section 8. Rodney (@rdennings) is the maintainer and makes the final call on scope; suggestions are welcome as issues or discussions.

---

## 1. Where we are

The prototype work in [`prototypes/`](../prototypes/) (Python, ~15k lines incl. data tables) has already proven the hard part is doable:

| Game | Read | Write into it | Verified in-game (Ayn Thor) |
|---|---|---|---|
| Emerald (Gen 3) | ✅ | ✅ from Platinum (downgrade) | ✅ incl. Pokédex fix |
| Platinum (Gen 4) | ✅ | — | source only |
| Black / White, B2 / W2 (Gen 5) | — | — | not prototyped yet (no test save) |
| X / Y (Gen 6) | ✅ | ✅ from Platinum | ✅ incl. "foreign" dex flag |
| Sword / Shield (Gen 8) | ✅ | ✅ from Platinum | ✅ after ribbon + in-place-write fixes |
| Scarlet / Violet (Gen 9) | ✅ | ✅ from Platinum | ✅ after ribbon fix |

Lessons already learned that the real product must bake in:

1. **Injection = the game's own bookkeeping too.** Pokédex seen/owned (all copies), origin-dependent flags (X/Y "foreign" owned), constructor defaults like Gen 8/9 affixed ribbon = `0xFF`.
2. **Write saves in place on Android.** Overwrite the existing file's bytes; never delete/recreate it, or the emulator loses ownership and the game hangs.
3. **Diff against a game-native Pokémon** to catch zero-vs-default bugs.
4. Most of the format knowledge came from **PKHeX** (GPLv3). That shapes the tech decision below.

What doesn't exist yet: a real codebase, a bank, any UI, git.

---

## 2. MVP

### The MVP in one sentence

> On the Ayn Thor, Rodney can pull Pokémon out of his GBA and DS saves into a personal bank, organize them in boxes, send them into another GBA/DS game, and bring them back later **without losing anything** — and the game treats them as if they were caught or traded there.

That is the smallest thing that delivers the "collection" feeling, the reason the project exists. Everything else builds on top of it.

### In the MVP

**Games (Tier 1)** — every GBA and DS game, the core of emulation play:
- Gen 3 (GBA): Ruby, Sapphire, Emerald, FireRed, LeafGreen
- Gen 4 (DS): Diamond, Pearl, Platinum, HeartGold, SoulSilver
- Gen 5 (DS): Black, White, Black 2, White 2

**Platform:** Android only (the Thor is the reference device). Linux is the next platform (v1.2), ahead of Windows and Mac. The core is built to be portable from day one (see §4).

**Loading saves**
- **First-run setup**, three steps (Rodney, 2026-11-06):
  1. **Find emulators.** One button checks which emulators from rhome's list of known emulators are installed.
  2. **Give access.** For each one found, one tap opens the system folder picker already inside that emulator's save folder; the user taps *Use this folder*, then *Allow*. This includes melonDS's ROM folder, because melonDS saves next to the ROM once it can write there.
  3. **First scan.** One button lists the granted folders and finds the Pokémon saves. The app identifies the game from the save itself (save-first).
- The setup can be run again from settings, for example after installing another emulator.
- **Known emulators list:** for each emulator, its Android package, default save folders, save file types and which copy it loads when there are two. It's data in the core, not code, so adding an emulator doesn't need new logic. MVP: melonDS and RetroArch (`.srm`), the two on the Thor. Linux paths join the same list in v1.2.
- Found saves go in a **"My saves"** list (game, trainer, play time, where it is) so opening one is one tap.
- **Every launch checks only the saves it already knows** (has each one changed or gone?), without listing folders: about 150 ms on the Thor, even with 2,800 ROMs in the ROM folder. New saves show up with a **Scan now** button. (Spike B: listing the ROM folder on every launch cost 0.4–2 s.)
- **One game, two saves:** when a game has a save in more than one place, show both and say which one the emulator loads, so nobody edits the copy the game ignores.
- Fallback: pick a save file by hand, for an emulator that isn't on the list.

**Transfers**
- Split view: game boxes + party on the left, bank on the right. **Move**, not copy.
- **Generation lock** (no Luxray into Emerald) and **structural validation** (species/moves/items that exist in the target game, valid checksums).
- **Pending changes → Save.** Nothing touches the bank or the game save until Save. Summary of what will happen.
- **Arrival bookkeeping** for every Tier 1 game (Pokédex seen/owned with all copies, Unown/Spinda first-seen data, anything else the per-game checklist finds).
- **Forward conversion Gen 3 → 4 → 5** (PKHeX, the same route as Pal Park and Poké Transfer).
- **Downgrade conversion Gen 5 → 4 → 3** (our own code; PKHeX only converts forward). Gen 5 adds hidden abilities, which Gen 3/4 can't store: the Pokémon gets a valid ability for the older game, and the hidden ability is kept in the bank and restored on return.

**The bank**
- Named boxes (30 slots), move Pokémon freely between them. One box group ("Bank") only.
- **Checked out placeholders** ("currently in Emerald — Eve") holding the full record.
- **Identity matching for Gen 3–5** (PID + OT TID/SID + origin): a returning Pokémon re-links to its placeholder automatically when unmistakable, asks when not.
- **Data retention**: anything the destination game can't hold (newer moves, held items, ribbons, hidden ability, Sinnoh/Unova met data) is kept in the bank and restored when the Pokémon returns to a game that can hold it.
- Simple **living dex** view: National Dex grid, owned/not owned, tap to see every copy.

**Safety**
- Automatic backup of the game save before every write (kept per save, last N).
- Bank snapshot before every Save (last N).
- **Export / import the whole bank** as a single plain JSON file — your data is yours, and it's the stopgap "sync" between the Thor and a computer until real sync exists.
- Disclaimer on first launch.

### Not in the MVP (and where it goes)

| Deferred | Milestone |
|---|---|
| 3DS (X/Y, ORAS, S/M, US/UM, Let's Go) + Switch (SwSh, BDSP, PLA, S/V, Z-A) | v1.1 |
| Gen 1/2 one-way import | v1.1 |
| Watched folders (new saves appear without tapping Scan now) | v1.2 |
| Box groups, decorations, smart boxes, dex layers (forms, shinies, regional) | v1.2 |
| Linux release (desktop + Steam Deck-style handhelds) | v1.2 |
| Windows and Mac releases | after Linux |
| Accounts + cloud sync | v1.3 |
| Web viewer, shareable showcase | v1.4 |
| ROM-first loading, ROM hack profiles, fakemon | v2 |
| Copy action, nickname editing, move tutor, social, save library sync | later / maybe |

---

## 3. Milestones

Sizes are relative effort (S / M / L / XL), not dates — this is a solo project and the pace is Rodney's.

### M0 — Foundations (M)
Settle the stack and de-risk the scariest unknowns before writing product code.
- `git init`, repo layout, GPLv3 license file, CI that builds and runs the core tests **on Linux** on every push, so the core stays portable from the first commit.
- **Spike A: stack.** Load every test save with PKHeX.Core and confirm it reports the same party/boxes as the Python readers. Build a "hello world" Avalonia app that runs on the Thor.
- **Spike B: Android save access (R1).** Prove the app can read *and write in place* a melonDS save and a RetroArch save on the Thor. Try in order: emulator setting that keeps saves next to ROMs → file picker / folder grant → Shizuku.
- Set up the golden test-save set in `fixtures/`. Rodney's real saves stay out of this public repo; use saves made for testing (no personal trainer data), or keep real ones in a private location the tests can point to.
- Get a Black/White or B2/W2 save from the Thor (melonDS) so Gen 5 has a fixture like the others.
- **Exit:** stack confirmed or swapped; a known, working way to read/write saves on the Thor.

### M1 — Core engine, headless (L)
The bank and transfer logic with no UI, driven by tests and a small CLI.
- Bank data model + local JSON storage (see §5).
- Import a Pokémon from a save → bank record + snapshot. Withdraw a record → target game format.
- Downgrade converters Gen 4 → 3 (port + harden `transfer_pt_to_em.py`) and Gen 5 → 4 (new, incl. hidden-ability handling).
- Per-game **arrival side-effects** module, with a checklist per game.
- Identity matching + merge for returning Pokémon.
- Changeset (pending changes) applied atomically; save backups; bank snapshots; in-place writing.
- Round-trip test harness (see §6).
- **Exit:** CLI can do Platinum → bank → Emerald → bank → Platinum, and Black → bank → Platinum → bank → Black, and each final Pokémon equals the original plus the progress it made in the other game; all Tier 1 fixtures round-trip byte-identical when nothing changed.

### M2 — Android app: transfer loop (L)
- First-run setup (find emulators → give access → first scan), the known emulators list, and the launch check + Scan now (see "Loading saves" in §2).
- "My saves" list, open a save, split view, drag/tap to move, pending-changes sheet, Save.
- Bank boxes: create, rename, reorder, move Pokémon between boxes.
- Pokémon detail view (stats, moves, ribbons, origin, "visited games").
- Sprites/icons pipeline (see R5).
- **Two-screen devices** (the Thor): use the bottom screen when there is one (Rodney, 2026-11-06). Spike B showed how: an Android `Presentation` with a second Avalonia view, taken down whenever the app is in the background so emulators get the screen back. What goes there is still open; the split view (game boxes on one screen, bank on the other) is the obvious candidate, and a debug log is useful during development.
- **Exit:** Rodney does the MVP sentence on the Thor with his real Platinum and Emerald saves.

### M3 — Collection + MVP hardening (L)
- Checked-out placeholders in the UI, match prompts for uncertain returns, clear-stale-placeholder.
- Living dex grid.
- Bank export/import, first-launch disclaimer, settings (backup count).
- Fill out Tier 1: R/S, FR/LG, D/P, HG/SS, B/W, B2/W2 each verified in-game on the Thor.
- README with build + sideload steps so friends with repo access can run it themselves.
- **Exit = MVP done:** every Tier 1 game passes the in-game checklist; a week of real use without data loss. No store release: the MVP is for Rodney, and friends run it from source.

### v1.1 — Modern games (L)
3DS and Switch games, with full two-way movement: no "one-way like HOME" limit. Forward conversion is PKHeX's, and prototypes already exist for X/Y, SwSh and S/V. The big job is the return trip down to Gen 3–5: abilities and mint natures, forms older games lack, moves removed in later games, Gigantamax/Tera data kept in the bank. Also Gen 1/2 one-way import with user-confirmed matching. Comes right after the MVP because the prototypes give it a head start (Rodney, 2026-11-04).

### v1.2 — Comfort (M)
Watched folders (new saves appear on their own); box groups, decorations, smart boxes; dex layers; **Linux release** (desktop, Steam Deck and similar handhelds; emulator-aware paths for RetroArch, melonDS etc. on Linux). Windows and Mac follow once Linux is solid.

### v1.3 — Accounts & sync (L)
Sign-in, Steam-Cloud-style sync with per-Pokémon merge and conflict prompts reusing the pending-changes UI. Re-check the Firebase decision against the chosen stack first (see R4).

### v1.4 — Web (M)
Read-only viewer of your bank, then the opt-in shareable showcase / living dex page. Bank editing on web after that.

### v2 — ROM hacks (XL)
ROM-first loading, ROM hash identification, per-hack profiles, reading custom data from the user's own ROM, fakemon section, hacker profile packs (needs legal review).

---

## 4. Tech approach

### Stack: C# / .NET + PKHeX.Core + Avalonia UI (final once M0 Spike A passes)

- **PKHeX.Core** already reads and writes every mainline save (Gen 1–9), handles checksums and Switch encryption, converts Pokémon forward between formats, and has tooling for Pokédex updates on import. The prototypes have essentially been re-porting it piece by piece; using it directly turns ~40 games of format work into a library call and lets us spend our time on what's unique: the bank, matching, retention, downgrades and the UX.
- **Avalonia** is one C# UI codebase for Android, Linux, Windows, Mac, and the browser (WebAssembly) — exactly the platform list. On Linux it runs as a normal desktop app, and it can also draw straight to the screen on devices without a desktop environment.
- **Portability:** .NET runs on Android, Linux (x64 and ARM64), Windows, Mac, iOS and the browser, so the core goes wherever there are emulators. If a platform ever needs a different UI toolkit, or a non-.NET host app, the core can be compiled to a native library with a plain C interface (.NET NativeAOT) and called from anything.
- **Plain JSON files** for the local bank (same format as the export). A personal bank is a few MB at most, so it loads fully into memory; writes go to a temp file and then replace the old one, and a bank snapshot is just a file copy. Storage sits behind one interface, so moving to SQLite later (e.g. for sync in v1.3) touches only that layer.

What it costs us:
- **License: GPLv3 (decided).** Rodney preferred MIT, but PKHeX.Core is GPLv3, and an app that ships with it has to be GPLv3 as a whole. The prototypes also contain code ported from PKHeX, which carries its license. GPLv3 still means free and open source; it just requires anyone who distributes a modified version to share their source too. Not legal advice, but this is the standard reading.
- **Downgrades are on us.** PKHeX converts forward only (Gen 3 → 4 → 5…). Backward moves (Platinum → Emerald, Switch → DS) are romHome's own converters.
- **Firebase isn't native to .NET clients** (no official desktop/mobile .NET SDK; REST works). Not an MVP problem — sync is v1.3 — but it may reopen the backend choice then.
- **Web bundle size**: PKHeX.Core is large for WASM. Acceptable for a viewer, and save importing on web is already "maybe later."

Alternatives considered: **Kotlin Multiplatform** or a **Rust core**. Both are portable too, but with either one we'd port PKHeX's format logic ourselves for every game, which is the slowest and riskiest path, and we'd have to keep up with PKHeX's fixes by hand.

### Architecture (layers)

```
 UI (Avalonia: Android / desktop / later web)
   │
 App services ─ changeset (pending changes) · backups · save locator / pairing
   │
 Rhome.Core  ─ bank model · identity matching · merge/retention rules
   │            · downgrade converters · arrival side-effects per game
   │
 PKHeX.Core  ─ save formats · PKM formats · checksums/crypto · forward conversion
   │
 Platform I/O ─ Android file access (SAF / Shizuku), in-place writes, desktop FS
```

**Portability rules for the core** (these keep it movable to any platform):
- `Rhome.Core` references only PKHeX.Core and the standard .NET library: no UI, no Android APIs, no file paths. A test (`PortabilityTests`) fails if anything else gets added.
- It reads and writes saves as **bytes/streams** handed to it. Finding, opening and writing files in place is the platform layer's job (Android file access today; Linux file system next).
- Anything platform-specific (save locations, file permissions, backups folder) sits behind a small interface with one implementation per platform.
- CI runs the core tests on Linux, so a platform dependency sneaking in breaks the build immediately.

---

## 5. Data model (sketch)

The idea: **store every format a Pokémon has lived in, plus a normalized summary.** That makes "nothing is lost" simple — we never have to cram everything into one superset struct.

- **Pokémon** — bank identity (UUID); status `in_bank` or `checked_out(save, slot)`; box + slot; **fingerprint** (PID, OT TID/SID, OT name, origin game, encryption constant for Gen 6+); summary fields for the UI (species, form, nickname, level, shiny, ball, OT, gender…).
- **Snapshot** — raw Pokémon bytes (base64 in the JSON) in one format (PK3, PK4, …), when captured, from which save. One per format the Pokémon has visited (history optional).
- **Save** — game, trainer, display name; device-local location (never synced); last seen hash; backups.
- **Box**, **BoxGroup** (MVP: one group).
- **Changeset** — staged operations; applied all-or-nothing on Save.

**Withdraw** to a game with format F: start from the newest snapshot, convert to F (forward via PKHeX, backward via our converters), then restore F-specific fields from the F snapshot if one exists. **Return** from a game: match to a placeholder, store the new snapshot, update the summary.

**Merge policy (decided):** when a Pokémon that leveled up in Emerald goes back to Platinum:
- **Progress follows the newest game:** level, EXP, EVs, friendship, nickname.
- **Format-only fields restore from the Platinum snapshot:** held item, ribbons, Gen 4 met data.
- **Moves default to the newest moveset, and the user can switch.** If the moveset it had last time in this game differs from its newest one, the withdraw step shows both and the user picks with one tap. No prompt when they're the same. A newest-moveset move that doesn't exist in the target game is replaced by that slot's move from the target-game snapshot if there is one, otherwise dropped, and the pending-changes summary says so.
- Mixing individual moves from the Pokémon's history is the "move tutor" idea, so it stays in the later list.

---

## 6. Testing & verification

1. **Golden fixtures** — every real save from the Thor, in `fixtures/`. Read tests assert known party/box contents.
2. **No-op round trip** — read + write with no changes must be byte-identical (catches checksum/layout bugs).
3. **Transfer round trips** — automated A → bank → B → bank → A for every Tier 1 pair; assert no data loss.
4. **Native diff** — compare an injected Pokémon to a game-native one; flag fields that are zero on ours but set on theirs.
5. **In-game checklist per game** (manual, on the Thor) — appears in box, summary screen correct, no bad ribbons/titles, Pokédex registered, save + reload survives, can trade/evolve/battle. Each game gets a row; a game isn't "supported" until it passes.
6. **Fuzz/robustness** — corrupted or truncated saves must be refused, never written.

---

## 7. Risks

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | **Android 11+ blocks apps from `Android/data/`**, where melonDS (and Eden) keep saves on the Thor. The file picker can't grant that folder on Android 13. | Could block the core loop for DS games. | **Solved on the Thor by Spike B** ([`spikes/spike-b`](../spikes/spike-b/README.md)): the system folder picker, opened right at the emulator's folder, grants `Android/data/<app>` on Android 13, and writes in place load in game. Left: a system update or Google's updatable file picker could block that grant; fallback is Shizuku for melonDS (RetroArch, Eden and Dolphin share their folders with the picker anyway). |
| R2 | Silent save corruption | Losing someone's run = trust gone | Backups before every write, validation, no-op round-trip tests, refuse unknown/odd saves. |
| R3 | Incomplete arrival side-effects | Pokémon "works" but the game disagrees (dex, ribbons, titles) | Per-game checklist + native diffing; nothing marked supported until it passes in-game. |
| R4 | Firebase ↔ .NET friction | Sync milestone gets harder | Sync is v1.3; keep backend pluggable; re-decide then. |
| R5 | Sprites/art are Nintendo's | Legal exposure, esp. for web showcase | Ship no extracted art in the open repo at first (load from a community source at runtime, or use text/silhouettes); revisit before public launch. |
| R6 | Name / trademark ("Home") | Forced rename | Treat "romHome" as a working name; decide before any public release. |
| R7 | Scope creep (hacks, social, editor) | MVP never ships | This doc. Anything new goes in a milestone, not the current one. |
| R8 | Core accidentally tied to Android | Linux port turns into a rewrite | Portability rules in §4; core tests run on Linux in CI from M0. |
| R9 | .NET too heavy for low-end Linux handhelds | Can't run on small devices | Trimmed, self-contained (NativeAOT) builds; test on a real handheld when Linux work starts. |

---

## 8. Decisions needed in review

1. ~~**MVP games**~~ — **DECIDED:** all GBA + DS games, Gen 3–5 (Rodney: Gen 5 is DS, so it's in). 3DS/Switch stay out of the MVP and become v1.1, right after it, with full two-way movement.
2. **Stack:** C# + PKHeX.Core + Avalonia (see §4). Rodney is open to any stack as long as the core can move to every platform with emulators, Linux especially. This one meets that, so it's the plan unless M0 Spike A shows a problem on the Thor.
3. ~~**License**~~ — **DECIDED:** GPLv3 (MIT isn't possible with PKHeX.Core; see §4).
4. ~~**Merge policy for moves**~~ — **DECIDED:** newest moveset by default, user can switch to the one it had in that game (see §5).
5. ~~**MVP platform**~~ — **DECIDED:** Android only.
6. ~~**Bank format**~~ — **DECIDED:** plain JSON, for both the export file and the app's own storage.
7. ~~**Who's the MVP for**~~ — **DECIDED:** just Rodney. Friends with repo access build and run it from source.

Still open from the scope doc, not blocking the MVP: copy action, auto-created box group on first load, login methods, web "editor" meaning.
