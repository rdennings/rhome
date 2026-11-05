#!/usr/bin/env python3
"""Read a Pokemon Sword or Shield save and print the party and boxes as text.

Usage:
    python3 read_swsh_save.py path/to/main   (the 'main' file from the Switch save folder)

Format notes (Gen 8, Sword/Shield), following PKHeX (github.com/kwsch/PKHeX):
  - The save is encrypted with SwishCrypto (see swish_crypto.py) and holds ~2400 keyed blocks.
  - Pokemon are 0x158 bytes in the party AND the boxes (boxes use party format),
    4 shuffled 0x50-byte blocks, encrypted with the Gen 4 PRNG seeded by the encryption constant.
"""
import struct
import sys

import swish_crypto
from gen8_data import MOVES, SPECIES
from read_pt_save import BLOCK_ORDERS, prng_xor, u16, u32

# Block keys
KEY_BOX = 0x0D66012C
KEY_PARTY = 0x2985FE5D
KEY_MY_STATUS = 0xF25C070E
KEY_PLAY_TIME = 0x8CBBFD90
KEY_BOX_LAYOUT = 0x19722C89

# MyStatus block
TRAINER_TID = 0xA0
TRAINER_SID = 0xA2
TRAINER_GENDER = 0xA5
TRAINER_LANGUAGE = 0xA7
TRAINER_NAME = 0xB0

BOX_COUNT = 32
BOX_SLOTS = 30
BOX_NAME_SIZE = 0x22

PK_STORED = 0x148
PK_SIZE = 0x158          # used for both party and box slots
PARTY_COUNT = 6 * PK_SIZE


def decode_string(b, o, max_chars):
    """Decode a Gen 8 string (UTF-16LE, 0x0000 terminated)."""
    out = []
    for i in range(max_chars):
        c = u16(b, o + i * 2)
        if c == 0:
            break
        out.append(chr(c))
    return "".join(out)


def load_blocks(path):
    with open(path, "rb") as f:
        raw = f.read()
    if not swish_crypto.hash_valid(raw):
        sys.exit("Save hash doesn't match; not a Sword/Shield save, or it's damaged.")
    return {b.key: b for b in swish_crypto.decrypt(raw)}


def decrypt_pokemon(raw):
    """Decrypt and unshuffle a 0x148/0x158-byte Pokemon. Returns (bytes, checksum_ok)."""
    ec = u32(raw, 0)
    pk = prng_xor(raw, 0x08, PK_STORED, ec)
    order = BLOCK_ORDERS[((ec >> 13) & 31) % 24]
    stored = [pk[0x08 + i * 0x50:0x08 + (i + 1) * 0x50] for i in range(4)]
    blocks = {letter: stored[i] for i, letter in enumerate(order)}
    pk[0x08:PK_STORED] = blocks["A"] + blocks["B"] + blocks["C"] + blocks["D"]
    if len(raw) > PK_STORED:
        pk = prng_xor(pk, PK_STORED, len(raw), ec)
    calc = sum(u16(pk, o) for o in range(0x08, PK_STORED, 2)) & 0xFFFF
    return bytes(pk), calc == u16(raw, 0x06)


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
    species = u16(pk, 0x08)
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

    gender = {0: " ♂", 1: " ♀"}.get((pk[0x22] >> 2) & 3, "")
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


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    blocks = load_blocks(sys.argv[1])
    status = blocks[KEY_MY_STATUS].data
    played = blocks[KEY_PLAY_TIME].data
    party = blocks[KEY_PARTY].data
    boxes = blocks[KEY_BOX].data
    layout = blocks[KEY_BOX_LAYOUT].data

    print(f"File: {sys.argv[1]}")
    print(f"Save hash ok, {len(blocks)} blocks")
    print()
    id32 = u32(status, TRAINER_TID)
    print(f"Trainer: {decode_string(status, TRAINER_NAME, 12)}  ID No. {id32 % 1000000:06d}"
          f"  (TID {u16(status, TRAINER_TID):05d}  SID {u16(status, TRAINER_SID):05d})")
    print(f"Play time: {u16(played, 0)}:{played[2]:02d}:{played[3]:02d}")
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
