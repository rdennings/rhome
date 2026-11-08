# Arrival checklist

What a game changes when a Pokémon is caught or traded into it, and what rhome does about each item (PLAN.md §1 lesson 1, §2 "Arrival bookkeeping"). The code is [`Arrival.cs`](Arrival.cs); one section per game family. A game isn't "supported" until its row in the in-game checks at the bottom passes on the Thor (PLAN.md §6.5).

Legend: ✅ done by rhome · 🧩 done by PKHeX code rhome calls · ➖ deliberately not done (why given) · ❓ not known yet

## Gen 3: Ruby, Sapphire, Emerald, FireRed, LeafGreen

| What the game changes | rhome |
|---|---|
| Pokédex **owned** flag (one copy) | 🧩 `SetCaught` |
| Pokédex **seen** flag, in all **three** copies (the game only counts a species as seen when they agree; the prototype's Emerald fix) | 🧩 `SetSeen` writes all three: Emerald `0x988`/`0x3B24`, Ruby/Sapphire `0x938`/`0x3A8C`, FireRed/LeafGreen `0x5F8`/`0x3A18` in the large block |
| First **Unown** seen: its PID, so the Pokédex shows its letter | ✅ only if Unown wasn't seen before |
| First **Spinda** seen: its PID, so the Pokédex shows its spots | ✅ only if Spinda wasn't seen before |
| Species outside the regional Pokédex | Nothing to do: the game shows it once the National Pokédex is unlocked, the same as after a real trade |
| Game stats: trades completed | ➖ rhome moves Pokémon between your own games; it isn't a trade, and the counter only feeds a stats screen |
| Obedience of a Pokémon from another trainer | Nothing to do: the game applies its badge rule by itself, as after a trade |
| Mail | ➖ the letter lives in the old save; the downgrade drops mail items (#16) |

## Gen 4: Diamond, Pearl, Platinum, HeartGold, SoulSilver

| What the game changes | rhome |
|---|---|
| Pokédex **owned** and **seen** | 🧩 `Zukan4.SetDex` |
| **Genders seen** (first and second; the Pokédex shows both once both are seen) | 🧩 |
| **Forms seen**, in the order first seen: Unown letters, Deoxys, Burmy/Wormadam, Rotom, Pichu in HeartGold/SoulSilver | 🧩 |
| Forms seen of **Shellos, Gastrodon, Shaymin, Giratina** (one bit per form; the first form seen goes into both bits) | ✅ PKHeX's Zukan4 writes its "no form" marker into these bits, which reads back as no form or both, so rhome follows the game's own `UpdateForms_TwoForms` (pret/pokeplatinum `src/pokedex.c`) |
| **Languages seen** (the Pokédex's foreign entries) | 🧩 |
| First **Spinda** seen: its PID | ✅ PKHeX's `Zukan4.SetDex` skips it, so rhome sets it |
| An **egg** from another trainer: marked as received in a link trade | 🧩 `UpdateHandler`, before the egg is written |
| Records: Pokémon traded | ➖ as in Gen 3 |
| Pal Park records for a Pokémon from Gen 3 | ➖ the Pal Park minigame's own scores; rhome isn't Pal Park |

## Gen 5: Black, White, Black 2, White 2

| What the game changes | rhome |
|---|---|
| Pokédex **owned**, and **seen** per gender and shininess | 🧩 `Zukan5.SetDex` |
| Which gender/shininess the Pokédex **displays** (the first one seen) | 🧩 |
| **Forms** seen and displayed | 🧩 |
| **Languages seen** | 🧩 |
| First **Spinda** seen: its spot pattern | 🧩 |
| An **egg** from another trainer: marked as received in a link trade | 🧩 `UpdateHandler`, before the egg is written |
| Records: Pokémon traded; Black 2/White 2 Medal Rally trade medals | ➖ as in Gen 3; medals are awarded from counters when you visit the Medal Rally |

## Everything else stays as it was

The tests check that arrival changes nothing in a save but the Pokédex block (`ArrivalTests`), on top of the withdraw tests that check the Pokémon itself.

## In-game checks on the Thor

| Game | Pokédex shows it as owned | Seen count right | Unown/Spinda | Save + reload | Checked |
|---|---|---|---|---|---|
| Emerald | ✅ owned and seen flags set and kept by the game's own save; Togepi isn't in the Hoenn Pokédex, so the count waits for the National Pokédex, as after a trade | ✅ (prototype) | ❓ | ✅ | 2026-11-08, rhome CLI: Togepi from Platinum. It battled and reached Lv 2. Found and fixed: it showed as level 0 and met at Lv 5 ([`Hatching`](../Conversion/Hatching.cs)) |
| Platinum | ✅ Togepi (back from Emerald), Riolu (from White 2) | ✅ Riolu was new: seen 70 → 71, owned 14 → 15 | ❓ | ✅ | 2026-11-08, rhome CLI: Togepi back from Emerald at Lv 2 with its own met and egg data; Riolu from White 2, Jolly kept with a new PID, met in a faraway place, battled to Lv 9 |
| White 2 | ✅ (Riolu, back from Platinum) | ❓ not exercised: Riolu was already owned | ❓ | ✅ | 2026-11-08, rhome CLI: Riolu back from Platinum at Lv 9 with its original PID and Floccesy Ranch met data; PKHeX rates it legal. It battled |
| White | ❓ | ❓ | ❓ | ❓ | waiting: White has no pre-Unova species before the Elite Four, so a test Pokémon can't start there yet |
| Ruby/Sapphire, FireRed/LeafGreen, Diamond/Pearl, HeartGold/SoulSilver, Black 2 | ❓ | ❓ | ❓ | ❓ | M3 |
