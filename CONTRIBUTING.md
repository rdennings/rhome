# Contributing to rhome

Thanks for wanting to help! rhome is a small, early-stage project maintained by @rdennings. This page explains how to get involved without stepping on anyone's toes.

## Before you start

- **Read the plan.** [docs/PLAN.md](docs/PLAN.md) says what's being built and in what order; [docs/SCOPE.md](docs/SCOPE.md) describes the final product.
- **Open an issue or discussion first** for anything bigger than a small fix, so we can agree on the approach before you put in the work. Ideas outside the current milestone are welcome too; they'll be slotted into a later one.
- Issues labeled `good first issue` or `help wanted` are good places to start.

## Ways to help

- **Game and emulator knowledge:** save locations, save format quirks, what a game does when you catch or trade a Pokémon (Pokédex flags, ribbons, etc.).
- **Testing:** checking that a moved Pokémon behaves correctly in-game. Use the "Game / emulator support" issue template to report results.
- **Code:** the core (C#), the app UI (Avalonia), and later the Linux port.
- **Docs:** anything that was confusing when you first read it.

## Workflow

1. Fork the repo (or create a branch if you have write access).
2. Name branches after what they do, e.g. `gen4-pokedex-flags` or `fix/emerald-checksum`.
3. Keep pull requests focused on one thing, and link the issue they address.
4. Make sure the build and tests pass: `dotnet test --solution Rhome.slnx`. CI runs the same on Linux for every pull request.
5. A maintainer reviews and merges. PRs are squash-merged, so the PR title becomes the commit message; keep it clear.

## Ground rules for code

- **Keep the core portable.** `Rhome.Core` (the engine) must not use UI or platform-specific APIs; it works on bytes and streams handed to it. See "Portability rules for the core" in [docs/PLAN.md](docs/PLAN.md).
- **Never risk a save.** Anything that writes a save needs a test, and must keep backups and in-place writing intact.
- **Match the game.** When writing a Pokémon into a game, do the bookkeeping the game itself would do. Compare against a Pokémon the game created itself to catch missing fields.
- Follow the formatting in `.editorconfig`.

## What must never be committed

- **ROMs, BIOS files, or extracted game assets** (sprites, music, text dumps). This is a hard rule.
- **Personal save files.** Save files contain trainer names and IDs. Test saves must be created for testing, and approved in review.
- Secrets of any kind (API keys, tokens, service account files).

The `.gitignore` blocks common ROM and save extensions as a safety net, but please check what you're adding.

## Licensing of contributions

rhome is licensed under [GPL-3.0](LICENSE). By submitting a contribution, you agree it is licensed under GPL-3.0 as well. If you port code from another project, say where it came from in the file header and make sure its license is compatible (PKHeX is GPLv3, which is fine).

## Conduct

Be kind and assume good intent. See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
