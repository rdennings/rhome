# romHome — Project Scope Draft

October 29, 2026

## The pitch

romHome is a Pokémon bank for people who play on emulators and ROM hacks: a private collection where Pokémon from every game you've played live together, so you can pick who comes along on your next adventure.

It reads save files from official Pokémon games (and, later, ROM hacks), lets you move Pokémon out of a game into your bank, and move them back into other compatible games. It targets Android and web first, with apps for Linux, Mac and Windows. Think of it as Pokémon HOME for the emulation crowd.

## Why it exists

Playing on a ROM or a hack loses the feeling of owning a collection. On official hardware, Pokémon HOME lets you keep every Pokémon from past adventures in one place, bring only the ones you want to your next game, and track a running living Pokédex. Emulation has nothing like it, so each save file is a dead end: when the run is over, the team is stuck inside it.

romHome is meant to give that feeling back. The product is the collection; the file handling underneath is just plumbing.

## How transfers work

Loading a save opens a split view, like Pokémon HOME: the game's boxes and party on the left, your bank on the right. You move Pokémon between the two.

- **Move, not copy.** A Pokémon leaves one side to enter the other, so it only ever exists in one place. A deliberate copy action is still an open question.
- **Nothing is final until you press Save.** Every move is staged and shown in a pending-changes summary first.
- **Every Pokémon is tracked individually.** Twenty Pidgey caught in LeafGreen are twenty separate records. If two of them visit Emerald and come back, the app recognizes exactly those two and restores anything the other game couldn't store (moves it doesn't have, ribbons, stat details).
- **Matching is automatic only when unmistakable.** Gen 3 and later games carry strong identifying data. Gen 1 and 2 carry less, so uncertain matches ask you.
- **Gen 1 and 2 are one-way for now.** Pokémon can go into the bank but not back into those games, modeled on how Pokémon HOME is believed to treat them (to be verified).
- **Generation lock.** A Pokémon can't be moved into a game that predates it, so a Luxray can't go to FireRed.
- **Arrivals count as obtained.** A Pokémon moved into a game should look as if it was caught or traded there, so the app also does the game's own bookkeeping, starting with marking it seen and owned in the Pokédex. Placing a Pokémon in a box doesn't do this by itself, and some games keep extra copies of the Pokédex that must all agree, or the game wipes the entry. What counts as "owned" can also depend on where the Pokémon came from: X/Y marks a Pokémon from a Gen 3, 4 or 5 game with its own "obtained from an older game" flag instead of the normal caught flag, so setting the caught flag would claim it was caught in Kalos. Each game's profile needs to list these side effects. (Found in the Platinum-to-Emerald and Platinum-to-Y tests, October 30, 2026.)

## The collection

The bank is built around how you like to collect, with as much flexibility as possible.

- **Living dex.** The default view is the National Dex, with layers you can switch on: alternate forms, shinies, and per-game regional dex progress. Each slot shows your best copy, and tapping it shows every copy you own.
- **Boxes.** You can name and decorate boxes and move Pokémon freely between them. Smart boxes (a saved filter such as "all shinies") are an idea under consideration.
- **Box groups.** Organize your boxes into groups of your own making. Groups aren't tied to any game.
- **Checked out.** When you move a Pokémon from the bank into a game, a placeholder stays behind ("currently in Emerald, Save 1"). The living dex stays complete, and the placeholder holds the full record so anything the game couldn't store can be restored when the Pokémon returns.

## Loading saves

You start by picking the ROM, and the app finds the matching save. If it can't, it asks you to browse for the save and then remembers the pairing.

1. The ROM identifies the game, or the specific ROM hack.
2. That selects a profile describing how the game stores its data.
3. The profile tells the app how to read the save.

- **Pairings are per device and per emulator setup.** RetroArch on Android, Steam, a desktop install and handheld builds such as the TrimUI Brick all keep saves in different places. You can re-point a pairing at any time and the app remembers.
- **Ways to find the save.** Browse by hand, watch an emulator's save folder, or let the app use the known default locations of popular emulators.
- **Save-first fallback.** Browsing your bank never needs a ROM. Where no ROM is available (web, a phone with synced saves), the app can identify a save by inspecting it.
- **ROMs stay on your device.** The app reads a ROM locally to identify it and never uploads or stores it, only a hash and an ID.

## Safety and principles

The app should never make you worry about losing a Pokémon.

- **Nothing is final until Save**, across the whole app: moves, box rearranging and sync decisions alike.
- **Backups before every write.** The bank is snapshotted before each Save (the last several are kept), and the original game save is backed up before the app writes to it.
- **Write saves in place.** On Android, an emulator's save may sit in its private app folder (Android/data). The app must overwrite the existing file's contents rather than delete and recreate it, or the file stops belonging to the emulator and it can no longer write to it; the game then hangs on load. A save can be perfectly valid and still break this way, so each emulator profile should note where its saves live and how to write them. (Found when a correct Sword save loaded to a black screen in Eden, October 31, 2026; overwriting the file in place fixed it.)
- **Structural validation, not legality checking.** The app checks that a Pokémon can load without crashing (valid species and moves for that game, correct checksums) but doesn't judge whether it is "legit". romHome is built for ROMs and emulators.
- **Disclaimer.** Users are cautioned to think before importing data into an official game. It isn't recommended, and romHome isn't responsible for bricked or locked games or any action by Nintendo.

## Accounts and sync

Your bank lives on your device and in the cloud, like Steam Cloud. The app works fully offline and syncs when a connection is available. If the same Pokémon changed on two devices, the app asks which version to keep; changes to different Pokémon merge automatically.

- **Hosting.** Firebase for sign-in and storage. It needs a card on file, includes 5 GB free, then costs roughly $0.10 per GB. A bank is tiny, so cost should stay low, and a donation model is the plan if the app grows.
- **Replaceable backend.** The sync layer is built so another storage service could replace Firebase later.
- **Sign-in.** Google sign-in is planned. Whether to also offer email and password is an open question.

## Platforms, web and sharing

- **Platforms.** Android and web first, then Linux, Mac and Windows.
- **Web.** It starts as a viewer. Editing your bank (organizing boxes, checking Pokémon out) comes second. Loading a save in the browser is a possible later project and would run on your device so saves never leave the browser.
- **Private by default.** Your collection is yours alone. An option lets you publish a shareable showcase of your living dex.

## ROM hack support (later)

Hacks change what a game contains: new Pokémon, altered moves and stats, sometimes a different save layout. The plan is a library of per-hack profiles, recognized by the ROM's hash.

- **Identifying hacks.** Hacks are patched ROMs, so each version has its own hash. A list of known hacks lets the app say which hack and version it is. An unknown ROM falls back to asking which base game to use.
- **Reading custom data.** When you load your own ROM, the app can read custom Pokémon, names and moves from it directly, on your device.
- **Hacker-submitted profile packs (idea).** For devices without the ROM, such as web, hack creators could submit a pack describing their custom Pokémon, moves and abilities, plus the hash of each release. Submitting would be their permission for the app to store that data. A patch alone holds the changed data but not the instructions for reading it, so a profile pack is likely the cleaner route.
- **Fakemon.** Custom Pokémon would be stored in the bank with care taken not to break saves across hack versions.
- **Legal caution.** Creators can only grant permission for what they own, such as original art, and not for sprites taken from official games. This needs legal review before launch.

## Scope

romHome is a vault. It stores, organizes and moves Pokémon; it does not edit stats, moves or IVs.

Not in scope now, but possible later:

- Nickname editing and a built-in move tutor
- Social features: trading and comparing dexes between users
- Managing and syncing save data for a whole ROM library
- Loading saves in the web app

## Questions for you

These are the open items where outside opinions help most.

- Would you use this, and what's the first thing you'd want to do with it?
- Should there be a deliberate copy action so one Pokémon can join several runs, or strictly one Pokémon in one place?
- When a save is loaded for the first time, should the app create a starting box group automatically, or should you place everything yourself?
- Sign-in: Google only, or email and password too?
- Which emulators and handhelds do you use, and where do they keep saves?
- Which ROM hacks matter most to you?
- Is any collecting style missing from the living dex (perfect IVs, one from every game, shiny sets)?
- Is "romHome" a good name?
