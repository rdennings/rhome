#!/usr/bin/env python3
"""Read a Pokemon Platinum (US/EU) save and print the party and boxes as text.

Usage:
    python3 read_pt_save.py path/to/save.sav   (or a DraStic/DeSmuME .dsv)

Format notes (Gen 4, Platinum):
  - The 512 KB save holds two copies ("halves") at 0x00000 and 0x40000.
  - Each half has a general block (trainer, party) and a storage block (boxes).
    The game writes them alternately, so the newest general block and the
    newest storage block can live in different halves; each is picked on its own.
  - Each block ends in a 0x14-byte footer: save counters at +0x00/+0x04,
    CRC16-CCITT of the block (minus footer) in the last 2 bytes.
  - Pokemon are 136 bytes in boxes, 236 in the party, shuffled and encrypted.
"""
import struct
import sys

from gen4_data import EXP_TABLE, GROWTH_RATE, MOVES, SPECIES

SAVE_SIZE = 0x80000
HALF_SIZE = 0x40000
FOOTER_SIZE = 0x14

GENERAL_SIZE = 0xCF2C
STORAGE_START = 0xCF2C
STORAGE_SIZE = 0x121E4

# Offsets inside the general block
TRAINER_NAME = 0x68
TRAINER_TID = 0x78
TRAINER_SID = 0x7A
MONEY = 0x7C
PLAY_HOURS = 0x8A
PLAY_MINUTES = 0x8C
PLAY_SECONDS = 0x8D
PARTY_COUNT = 0x9C
PARTY_START = 0xA0

# Offsets inside the storage block
BOX_START = 0x04
BOX_COUNT = 18
BOX_SLOTS = 30
BOX_NAMES = BOX_START + BOX_COUNT * BOX_SLOTS * 136
BOX_NAME_SIZE = 0x28

PK_SIZE = 136
PARTY_PK_SIZE = 236

# Block order for each shuffle value: position i in the stored data holds this block.
BLOCK_ORDERS = [
    "ABCD", "ABDC", "ACBD", "ACDB", "ADBC", "ADCB",
    "BACD", "BADC", "BCAD", "BCDA", "BDAC", "BDCA",
    "CABD", "CADB", "CBAD", "CBDA", "CDAB", "CDBA",
    "DABC", "DACB", "DBAC", "DBCA", "DCAB", "DCBA",
]


# ---------------------------------------------------------------- helpers

def u16(b, o):
    return struct.unpack_from("<H", b, o)[0]


def u32(b, o):
    return struct.unpack_from("<I", b, o)[0]


def crc16_ccitt(data):
    crc = 0xFFFF
    for byte in data:
        crc ^= byte << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) if crc & 0x8000 else (crc << 1)
            crc &= 0xFFFF
    return crc


def _build_charmap():
    m = {0x01DE: " "}
    for i in range(10):
        m[0x0121 + i] = chr(ord("0") + i)
    for i in range(26):
        m[0x012B + i] = chr(ord("A") + i)
        m[0x0145 + i] = chr(ord("a") + i)
    punct = "!?,.…·/‘’“”„«»()♂♀+-*#=&~:;"
    for i, ch in enumerate(punct):
        m[0x01AB + i] = ch
    return m


CHARMAP = _build_charmap()


def decode_string(b, o, max_chars):
    """Decode a Gen 4 string (u16 per char, 0xFFFF terminated)."""
    out = []
    for i in range(max_chars):
        c = u16(b, o + i * 2)
        if c == 0xFFFF:
            break
        out.append(CHARMAP.get(c, "?"))
    return "".join(out)


def level_from_exp(species, exp):
    table = EXP_TABLE[GROWTH_RATE[species]]
    level = 1
    while level < 100 and exp >= table[level]:
        level += 1
    return level


# ---------------------------------------------------------------- save blocks

def load_save(path):
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < SAVE_SIZE:
        sys.exit(f"File is {len(data)} bytes; expected at least {SAVE_SIZE} (512 KB). Not a DS Pokemon save?")
    # .dsv files (DraStic / DeSmuME) are the raw save plus a small footer; drop it.
    return data[:SAVE_SIZE]


def block_info(data, half, start, size):
    """Return (valid, counters) for one copy of a block."""
    base = half * HALF_SIZE + start
    block = data[base:base + size]
    footer = size - FOOTER_SIZE
    stored = u16(block, size - 2)
    valid = crc16_ccitt(block[:footer]) == stored
    counters = (u32(block, footer), u32(block, footer + 4))
    if counters[0] == 0xFFFFFFFF:
        valid = False
    return valid, counters


def pick_block(data, start, size, label):
    """Pick the newest valid copy of a block; returns (bytes, description)."""
    infos = [block_info(data, h, start, size) for h in (0, 1)]
    candidates = [h for h in (0, 1) if infos[h][0]]
    if not candidates:
        sys.exit(f"Neither copy of the {label} block has a valid checksum. Not a Platinum save?")
    best = max(candidates, key=lambda h: infos[h][1])
    desc = ", ".join(
        f"{'AB'[h]}: {'ok' if infos[h][0] else 'bad'} counter={infos[h][1][0]}/{infos[h][1][1]}"
        for h in (0, 1)
    )
    base = best * HALF_SIZE + start
    return data[base:base + size], f"{label} block -> using {'AB'[best]} ({desc})"


# ---------------------------------------------------------------- pokemon

def prng_xor(data, start, end, seed):
    out = bytearray(data)
    for o in range(start, end, 2):
        seed = (seed * 0x41C64E6D + 0x6073) & 0xFFFFFFFF
        struct.pack_into("<H", out, o, u16(out, o) ^ (seed >> 16))
    return out


def decrypt_pokemon(raw):
    """Decrypt and unshuffle a 136- or 236-byte Pokemon. Returns (bytes, checksum_ok)."""
    pid = u32(raw, 0)
    checksum = u16(raw, 6)
    pk = prng_xor(raw, 0x08, 0x88, checksum)
    order = BLOCK_ORDERS[((pid & 0x3E000) >> 13) % 24]
    stored = [pk[0x08 + i * 32:0x08 + (i + 1) * 32] for i in range(4)]
    blocks = {letter: stored[i] for i, letter in enumerate(order)}
    pk[0x08:0x88] = blocks["A"] + blocks["B"] + blocks["C"] + blocks["D"]
    if len(raw) > PK_SIZE:
        pk = prng_xor(pk, 0x88, len(raw), pid)
    calc = sum(u16(pk, o) for o in range(0x08, 0x88, 2)) & 0xFFFF
    return bytes(pk), calc == checksum


def move_list(pk):
    names = []
    for i in range(4):
        move = u16(pk, 0x28 + i * 2)
        if move:
            names.append(MOVES[move] if move < len(MOVES) else f"[move #{move}]")
    return ", ".join(names) or "(no moves)"


def describe_pokemon(raw, indent):
    """Text for a Pokemon (summary line, then its moves on a line starting with indent), or None if empty."""
    if not any(raw[:PK_SIZE]):
        return None
    pk, ok = decrypt_pokemon(raw)
    species = u16(pk, 0x08)
    if not ok:
        return "[corrupt data: checksum mismatch]"
    if species == 0:
        return None
    if species > 493:
        return f"[unknown species #{species}]"

    name = SPECIES[species]
    pid = u32(pk, 0)
    tid, sid = u16(pk, 0x0C), u16(pk, 0x0E)
    exp = u32(pk, 0x10)
    ivs = u32(pk, 0x38)
    is_egg = bool(ivs & (1 << 30))
    nicknamed = bool(ivs & (1 << 31))
    flags = pk[0x40]
    form = flags >> 3
    nickname = decode_string(pk, 0x48, 11)

    if is_egg:
        return f"Egg ({name})"

    if flags & 0x04:
        gender = ""
    elif flags & 0x02:
        gender = " ♀"
    else:
        gender = " ♂"
    level = pk[0x8C] if len(pk) > PK_SIZE else level_from_exp(species, exp)
    shiny = ((tid ^ sid ^ (pid >> 16) ^ (pid & 0xFFFF)) < 8)

    line = f"{name}{gender}  Lv {level}"
    if nicknamed:
        line += f'  "{nickname}"'
    if form:
        line += f"  (form {form})"
    if shiny:
        line += "  ★ shiny"
    return f"{line}\n{indent}{move_list(pk)}"


# ---------------------------------------------------------------- main

def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    data = load_save(sys.argv[1])

    general, gen_desc = pick_block(data, 0, GENERAL_SIZE, "general")
    storage, sto_desc = pick_block(data, STORAGE_START, STORAGE_SIZE, "storage")

    print(f"File: {sys.argv[1]}")
    print(gen_desc)
    print(sto_desc)
    print()
    trainer = decode_string(general, TRAINER_NAME, 8)
    print(f"Trainer: {trainer}  TID {u16(general, TRAINER_TID):05d}  SID {u16(general, TRAINER_SID):05d}")
    print(f"Play time: {u16(general, PLAY_HOURS)}:{general[PLAY_MINUTES]:02d}:{general[PLAY_SECONDS]:02d}"
          f"  Money: {u32(general, MONEY)}")
    print()

    count = min(general[PARTY_COUNT], 6)
    print(f"PARTY ({count}/6)")
    for i in range(count):
        o = PARTY_START + i * PARTY_PK_SIZE
        line = describe_pokemon(general[o:o + PARTY_PK_SIZE], " " * 5)
        print(f"  {i + 1}. {line or '(empty?)'}")
    print()

    for box in range(BOX_COUNT):
        box_name = decode_string(storage, BOX_NAMES + box * BOX_NAME_SIZE, 20) or f"Box {box + 1}"
        lines = []
        for slot in range(BOX_SLOTS):
            o = BOX_START + (box * BOX_SLOTS + slot) * PK_SIZE
            line = describe_pokemon(storage[o:o + PK_SIZE], " " * 6)
            if line:
                lines.append(f"  {slot + 1:2d}. {line}")
        print(f"BOX {box + 1}: {box_name} ({len(lines)}/30)")
        print("\n".join(lines) if lines else "  (empty)")
        print()


if __name__ == "__main__":
    main()
