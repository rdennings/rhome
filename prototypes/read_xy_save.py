#!/usr/bin/env python3
"""Read a Pokemon X or Y save and print the party and boxes as text.

Usage:
    python3 read_xy_save.py path/to/main   (the 'main' file from the 3DS save data folder)

Format notes (Gen 6, X/Y), following PKHeX (github.com/kwsch/PKHeX):
  - Emulators store the save decrypted: one 0x65600-byte file split into 55 blocks at fixed offsets.
  - A table at 0x65400 holds each block's CRC16-CCITT (at 0x65400 + 0x14 + id*8 + 6).
  - Pokemon are 232 bytes in boxes, 260 in the party; 4 shuffled 56-byte blocks,
    encrypted with the same PRNG as Gen 4, seeded by the encryption constant.
"""
import struct
import sys

from gen4_data import EXP_TABLE
from gen6_data import GROWTH_RATE, MOVES, SPECIES
from read_pt_save import BLOCK_ORDERS, crc16_ccitt, prng_xor, u16, u32

SAVE_SIZE = 0x65600
BLOCK_TABLE = 0x65400

# (offset, length) of each block, by block ID
BLOCKS = [
    (0x00000, 0x002C8), (0x00400, 0x00B88), (0x01000, 0x0002C), (0x01200, 0x00038), (0x01400, 0x00150),
    (0x01600, 0x00004), (0x01800, 0x00008), (0x01A00, 0x001C0), (0x01C00, 0x000BE), (0x01E00, 0x00024),
    (0x02000, 0x02100), (0x04200, 0x00140), (0x04400, 0x00440), (0x04A00, 0x00574), (0x05000, 0x04E28),
    (0x0A000, 0x04E28), (0x0F000, 0x04E28), (0x14000, 0x00170), (0x14200, 0x0061C), (0x14A00, 0x00504),
    (0x15000, 0x006A0), (0x15800, 0x00644), (0x16000, 0x00104), (0x16200, 0x00004), (0x16400, 0x00420),
    (0x16A00, 0x00064), (0x16C00, 0x003F0), (0x17000, 0x0070C), (0x17800, 0x00180), (0x17A00, 0x00004),
    (0x17C00, 0x0000C), (0x17E00, 0x00048), (0x18000, 0x00054), (0x18200, 0x00644), (0x18A00, 0x005C8),
    (0x19000, 0x002F8), (0x19400, 0x01B40), (0x1B000, 0x001F4), (0x1B200, 0x001F0), (0x1B400, 0x00216),
    (0x1B800, 0x00390), (0x1BC00, 0x01A90), (0x1D800, 0x00308), (0x1DC00, 0x00618), (0x1E400, 0x0025C),
    (0x1E800, 0x00834), (0x1F200, 0x00318), (0x1F600, 0x007D0), (0x1FE00, 0x00C48), (0x20C00, 0x00078),
    (0x20E00, 0x00200), (0x21000, 0x00C84), (0x21E00, 0x00628), (0x22600, 0x34AD0), (0x57200, 0x0E058),
]
BLOCK_BOX_LAYOUT = 12
BLOCK_TRAINER = 17
BLOCK_PARTY = 18
BLOCK_DEX = 20
BLOCK_BOXES = 53

PLAY_TIME = 0x01800             # u16 hours, u8 minutes, u8 seconds

# Trainer (MyStatus) block
TRAINER = 0x14000
TRAINER_TID = TRAINER + 0x00
TRAINER_SID = TRAINER + 0x02
TRAINER_GENDER = TRAINER + 0x05
TRAINER_REGION = TRAINER + 0x26
TRAINER_COUNTRY = TRAINER + 0x27
TRAINER_CONSOLE_REGION = TRAINER + 0x2C
TRAINER_LANGUAGE = TRAINER + 0x2D
TRAINER_NAME = TRAINER + 0x48

PARTY_START = 0x14200
PARTY_COUNT = PARTY_START + 6 * 0x104

BOX_START = 0x22600
BOX_COUNT = 31
BOX_SLOTS = 30
BOX_NAMES = 0x04400
BOX_NAME_SIZE = 0x22

# Pokedex (Zukan) block
DEX = 0x15000
DEX_PACKED = DEX + 0x04         # bit 0 = National Dex unlocked
DEX_CAUGHT = DEX + 0x08
DEX_SEEN = DEX + 0x68           # 4 regions: male, female, male shiny, female shiny
DEX_DISPLAYED = DEX + 0x1E8     # same 4 regions; which picture the dex shows
DEX_LANGUAGE = DEX + 0x3C8      # 7 bits per species
DEX_SPINDA = DEX + 0x648
DEX_FOREIGN = DEX + 0x64C       # "obtained from Gen 3/4/5" flags, species 1-649
DEX_REGION_SIZE = 0x60
DEX_MAX_SPECIES = 721
DEX_MAX_FOREIGN = 649

PK_SIZE = 0xE8
PARTY_PK_SIZE = 0x104

CHAR_FIXES = {0xE08E: "♂", 0xE08F: "♀"}


# ---------------------------------------------------------------- helpers

def decode_string(b, o, max_chars):
    """Decode a Gen 6 string (UTF-16LE, 0x0000 terminated)."""
    out = []
    for i in range(max_chars):
        c = u16(b, o + i * 2)
        if c == 0:
            break
        out.append(CHAR_FIXES.get(c, chr(c)))
    return "".join(out)


def level_from_exp(species, exp):
    table = EXP_TABLE[GROWTH_RATE[species]]
    level = 1
    while level < 100 and exp >= table[level]:
        level += 1
    return level


def flag(data, base, bit):
    return bool(data[base + (bit >> 3)] & (1 << (bit & 7)))


# ---------------------------------------------------------------- save

def load_save(path):
    with open(path, "rb") as f:
        data = f.read()
    if len(data) != SAVE_SIZE:
        sys.exit(f"File is {len(data)} bytes; expected {SAVE_SIZE} for a decrypted X/Y 'main' save.")
    return data


def block_checksum(data, block_id):
    off, length = BLOCKS[block_id]
    return crc16_ccitt(data[off:off + length])


def stored_checksum_offset(block_id):
    return BLOCK_TABLE + 0x14 + block_id * 8 + 6


def bad_blocks(data):
    return [i for i in range(len(BLOCKS)) if block_checksum(data, i) != u16(data, stored_checksum_offset(i))]


# ---------------------------------------------------------------- pokemon

def decrypt_pokemon(raw):
    """Decrypt and unshuffle a 232- or 260-byte Pokemon. Returns (bytes, checksum_ok)."""
    ec = u32(raw, 0)
    pk = prng_xor(raw, 0x08, PK_SIZE, ec)
    order = BLOCK_ORDERS[((ec >> 13) & 31) % 24]
    stored = [pk[0x08 + i * 56:0x08 + (i + 1) * 56] for i in range(4)]
    blocks = {letter: stored[i] for i, letter in enumerate(order)}
    pk[0x08:PK_SIZE] = blocks["A"] + blocks["B"] + blocks["C"] + blocks["D"]
    if len(raw) > PK_SIZE:
        pk = prng_xor(pk, PK_SIZE, len(raw), ec)
    calc = sum(u16(pk, o) for o in range(0x08, PK_SIZE, 2)) & 0xFFFF
    return bytes(pk), calc == u16(raw, 0x06)


def move_list(pk):
    names = []
    for i in range(4):
        move = u16(pk, 0x5A + i * 2)
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
    pid = u32(pk, 0x18)
    tid, sid = u16(pk, 0x0C), u16(pk, 0x0E)
    ivs = u32(pk, 0x74)
    if ivs & (1 << 30):
        return f"Egg ({name})"

    gender = {0: " ♂", 1: " ♀"}.get((pk[0x1D] >> 1) & 3, "")
    form = pk[0x1D] >> 3
    level = pk[0xEC] if len(pk) > PK_SIZE else level_from_exp(species, u32(pk, 0x10))
    shiny = (tid ^ sid ^ (pid >> 16) ^ (pid & 0xFFFF)) < 16

    line = f"{name}{gender}  Lv {level}"
    if ivs & (1 << 31):
        line += f'  "{decode_string(pk, 0x40, 12)}"'
    if form:
        line += f"  (form {form})"
    if shiny:
        line += "  ★ shiny"
    return f"{line}\n{indent}{move_list(pk)}"


def dex_counts(data):
    seen = owned = 0
    for species in range(1, DEX_MAX_SPECIES + 1):
        bit = species - 1
        if any(flag(data, DEX_SEEN + r * DEX_REGION_SIZE, bit) for r in range(4)):
            seen += 1
        if flag(data, DEX_CAUGHT, bit) or (species <= DEX_MAX_FOREIGN and flag(data, DEX_FOREIGN, bit)):
            owned += 1
    return seen, owned


# ---------------------------------------------------------------- main

def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    data = load_save(sys.argv[1])

    print(f"File: {sys.argv[1]}")
    bad = bad_blocks(data)
    print(f"Block checksums: {len(BLOCKS) - len(bad)}/{len(BLOCKS)} ok" + (f" (bad: {bad})" if bad else ""))
    print()
    trainer = decode_string(data, TRAINER_NAME, 13)
    print(f"Trainer: {trainer}  TID {u16(data, TRAINER_TID):05d}  SID {u16(data, TRAINER_SID):05d}")
    print(f"Play time: {u16(data, PLAY_TIME)}:{data[PLAY_TIME + 2]:02d}:{data[PLAY_TIME + 3]:02d}")
    seen, owned = dex_counts(data)
    national = "unlocked" if data[DEX_PACKED] & 1 else "not unlocked"
    print(f"Pokédex: seen {seen}  owned {owned}  (National Dex {national})")
    print()

    count = min(data[PARTY_COUNT], 6)
    print(f"PARTY ({count}/6)")
    for i in range(count):
        o = PARTY_START + i * PARTY_PK_SIZE
        line = describe_pokemon(data[o:o + PARTY_PK_SIZE], " " * 5)
        print(f"  {i + 1}. {line or '(empty?)'}")
    print()

    for box in range(BOX_COUNT):
        box_name = decode_string(data, BOX_NAMES + box * BOX_NAME_SIZE, 16) or f"Box {box + 1}"
        lines = []
        for slot in range(BOX_SLOTS):
            o = BOX_START + (box * BOX_SLOTS + slot) * PK_SIZE
            line = describe_pokemon(data[o:o + PK_SIZE], " " * 6)
            if line:
                lines.append(f"  {slot + 1:2d}. {line}")
        print(f"BOX {box + 1}: {box_name} ({len(lines)}/30)")
        print("\n".join(lines) if lines else "  (empty)")
        print()


if __name__ == "__main__":
    main()
