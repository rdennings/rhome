#!/usr/bin/env python3
"""Read a Pokemon Scarlet or Violet save and print the party and boxes as text.

Usage:
    python3 read_sv_save.py path/to/main   (the 'main' file from the Switch save folder)

Format notes (Gen 9, Scarlet/Violet), following PKHeX (github.com/kwsch/PKHeX):
  - Same SwishCrypto encryption as Sword/Shield (see swish_crypto.py), ~6000 keyed blocks.
  - Pokemon use the same 0x158-byte encrypted layout as Sword/Shield (party format in boxes too),
    but many fields moved, and species 917+ are stored under S/V's own internal numbers.
  - Base-game saves keep the Pokedex in the "Paldea" block; saves from game version 2.0.1+
    use a newer "Kitakami" block instead (not supported yet).
"""
import struct
import sys

from gen9_data import FIRST_UNALIGNED, INTERNAL_TO_NATIONAL_SHIFT, NATIONAL_TO_INTERNAL_SHIFT, MOVES, SPECIES
from read_pt_save import u16, u32
from read_swsh_save import decode_string, decrypt_pokemon, load_blocks

# Block keys
KEY_BOX = 0x0D66012C
KEY_PARTY = 0x3AA1A9AD
KEY_MY_STATUS = 0xE3E89BD1
KEY_PLAY_TIME = 0xEDAFF794
KEY_BOX_LAYOUT = 0x19722C89
KEY_ZUKAN_PALDEA = 0x0DEAAEBD
KEY_ZUKAN_KITAKAMI = 0xF5D7C0E2

# MyStatus block
TRAINER_ID32 = 0x00
TRAINER_GENDER = 0x05
TRAINER_LANGUAGE = 0x07
TRAINER_NAME = 0x10

BOX_COUNT = 32
BOX_SLOTS = 30
BOX_NAME_SIZE = 0x22

PK_SIZE = 0x158
PARTY_COUNT = 6 * PK_SIZE

DEX_ENTRY_SIZE = 0x18   # Paldea block: one entry per internal species number


def to_national(internal):
    shift = internal - FIRST_UNALIGNED
    return internal + INTERNAL_TO_NATIONAL_SHIFT[shift] if 0 <= shift < len(INTERNAL_TO_NATIONAL_SHIFT) else internal


def to_internal(national):
    shift = national - FIRST_UNALIGNED
    return national + NATIONAL_TO_INTERNAL_SHIFT[shift] if 0 <= shift < len(NATIONAL_TO_INTERNAL_SHIFT) else national


def move_list(pk):
    names = []
    for i in range(4):
        move = u16(pk, 0x72 + i * 2)
        if move:
            names.append(MOVES[move] if move < len(MOVES) else f"[move #{move}]")
    return ", ".join(names) or "(no moves)"


def describe_pokemon(raw, indent):
    """Text for a Pokemon (summary line, then its moves on a line starting with indent), or None if empty."""
    pk, ok = decrypt_pokemon(raw)
    species = to_national(u16(pk, 0x08))
    if not ok:
        return "[corrupt data: checksum mismatch]"
    if species == 0:
        return None
    if species >= len(SPECIES):
        return f"[unknown species #{species}]"

    name = SPECIES[species]
    pid = u32(pk, 0x1C)
    tid, sid = u16(pk, 0x0C), u16(pk, 0x0E)
    ivs = u32(pk, 0x8C)
    if ivs & (1 << 30):
        return f"Egg ({name})"

    gender = {0: " ♂", 1: " ♀"}.get((pk[0x22] >> 1) & 3, "")
    form = pk[0x24]
    shiny = (tid ^ sid ^ (pid >> 16) ^ (pid & 0xFFFF)) < 16

    line = f"{name}{gender}  Lv {pk[0x148]}"
    if ivs & (1 << 31):
        line += f'  "{decode_string(pk, 0x58, 12)}"'
    if form:
        line += f"  (form {form})"
    if shiny:
        line += "  ★ shiny"
    return f"{line}\n{indent}{move_list(pk)}"


def dex_counts(blocks):
    """(seen, caught) in the Paldea Pokedex block, or None if the save uses the newer layout."""
    if KEY_ZUKAN_KITAKAMI in blocks and blocks[KEY_ZUKAN_KITAKAMI].data:
        return None
    dex = blocks[KEY_ZUKAN_PALDEA].data
    states = [u32(dex, i * DEX_ENTRY_SIZE) for i in range(len(dex) // DEX_ENTRY_SIZE)]
    return sum(s >= 2 for s in states), sum(s >= 3 for s in states)


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    blocks = load_blocks(sys.argv[1])
    status = blocks[KEY_MY_STATUS].data
    played = struct.unpack_from("<3i", blocks[KEY_PLAY_TIME].data)
    party = blocks[KEY_PARTY].data
    boxes = blocks[KEY_BOX].data
    layout = blocks[KEY_BOX_LAYOUT].data

    print(f"File: {sys.argv[1]}")
    print(f"Save hash ok, {len(blocks)} blocks")
    print()
    print(f"Trainer: {decode_string(status, TRAINER_NAME, 12)}  ID No. {u32(status, TRAINER_ID32) % 1000000:06d}")
    print(f"Play time: {played[0]}:{played[1]:02d}:{played[2]:02d}")
    counts = dex_counts(blocks)
    print(f"Pokédex: seen {counts[0]}  caught {counts[1]}" if counts else "Pokédex: newer (Kitakami) layout, not read yet")
    print()

    count = min(party[PARTY_COUNT], 6)
    print(f"PARTY ({count}/6)")
    for i in range(count):
        line = describe_pokemon(party[i * PK_SIZE:(i + 1) * PK_SIZE], " " * 5)
        print(f"  {i + 1}. {line or '(empty?)'}")
    print()

    for box in range(BOX_COUNT):
        box_name = decode_string(layout, box * BOX_NAME_SIZE, 16) or f"Box {box + 1}"
        lines = []
        for slot in range(BOX_SLOTS):
            o = (box * BOX_SLOTS + slot) * PK_SIZE
            line = describe_pokemon(boxes[o:o + PK_SIZE], " " * 6)
            if line:
                lines.append(f"  {slot + 1:2d}. {line}")
        print(f"BOX {box + 1}: {box_name} ({len(lines)}/30)")
        print("\n".join(lines) if lines else "  (empty)")
        print()


if __name__ == "__main__":
    main()
