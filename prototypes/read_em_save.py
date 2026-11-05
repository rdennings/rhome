#!/usr/bin/env python3
"""Read a Pokemon Emerald (US/EU) save and print the party and boxes as text.

Usage:
    python3 read_em_save.py path/to/save.sav   (or a RetroArch .srm)

Format notes (Gen 3, Emerald):
  - The 128 KB flash save holds two save slots of 14 sections at 0x0000 and 0xE000.
    The slot with the higher save index is the newest.
  - Each 4 KB section is stored in a rotated position; its footer gives the
    section ID (0xFF4), a checksum (0xFF6), a signature (0xFF8) and the save index (0xFFC).
  - Section 0 = trainer, section 1 = party, sections 5-13 joined = PC boxes.
  - Pokemon are 80 bytes in boxes, 100 in the party. 48 bytes in the middle are split
    into 4 shuffled substructures, XOR-encrypted with (PID ^ OT ID).
"""
import struct
import sys

from gen3_data import GENDER_RATE, HOENN_INDEX_TO_NATIONAL
from gen4_data import EXP_TABLE, GROWTH_RATE, MOVES, SPECIES

SAVE_SIZE = 0x20000
SLOT_SIZE = 0xE000
SECTION_SIZE = 0x1000
SECTION_COUNT = 14
SIGNATURE = 0x08012025

# Bytes of real data per section ID (the checksum covers only these)
SECTION_DATA_SIZE = [0xF2C, 0xF80, 0xF80, 0xF80, 0xF08] + [0xF80] * 8 + [0x7D0]

# Section 0 (trainer)
TRAINER_NAME = 0x00
TRAINER_TID = 0x0A
TRAINER_SID = 0x0C
PLAY_HOURS = 0x0E
PLAY_MINUTES = 0x10
PLAY_SECONDS = 0x11
SECURITY_KEY = 0xAC
DEX_UNOWN_PID = 0x1C
DEX_SPINDA_PID = 0x20
DEX_OWNED = 0x28
DEX_SEEN = 0x5C
DEX_FLAG_BYTES = 52

# Emerald keeps two more copies of the seen flags in SaveBlock1 (sections 1-4 joined,
# 0xF80 bytes each); if any copy disagrees, the game wipes that entry as tampered.
DEX_SEEN_COPIES = [(1, 0x988), (4, 0xCA4)]   # (section, offset) = SaveBlock1 0x988 and 0x3B24

# Section 1 (party, money)
PARTY_COUNT = 0x234
PARTY_START = 0x238
MONEY = 0x490

# PC storage (sections 5-13 joined)
BOX_START = 0x04
BOX_COUNT = 14
BOX_SLOTS = 30
BOX_NAMES = BOX_START + BOX_COUNT * BOX_SLOTS * 80
BOX_NAME_SIZE = 9

PK_SIZE = 80
PARTY_PK_SIZE = 100

# Substructure order for PID % 24: position i in the stored data holds this substructure
# (G = growth, A = attacks, E = EVs/condition, M = misc).
SUB_ORDERS = [
    "GAEM", "GAME", "GEAM", "GEMA", "GMAE", "GMEA",
    "AGEM", "AGME", "AEGM", "AEMG", "AMGE", "AMEG",
    "EGAM", "EGMA", "EAGM", "EAMG", "EMGA", "EMAG",
    "MGAE", "MGEA", "MAGE", "MAEG", "MEGA", "MEAG",
]


# ---------------------------------------------------------------- helpers

def u16(b, o):
    return struct.unpack_from("<H", b, o)[0]


def u32(b, o):
    return struct.unpack_from("<I", b, o)[0]


def _build_charmap():
    m = {0x00: " "}
    for i in range(10):
        m[0xA1 + i] = chr(ord("0") + i)
    for i in range(26):
        m[0xBB + i] = chr(ord("A") + i)
        m[0xD5 + i] = chr(ord("a") + i)
    m.update({0xAB: "!", 0xAC: "?", 0xAD: ".", 0xAE: "-", 0xB0: "…", 0xB1: "“", 0xB2: "”",
              0xB3: "‘", 0xB4: "’", 0xB5: "♂", 0xB6: "♀", 0xB8: ",", 0xBA: "/"})
    return m


CHARMAP = _build_charmap()


def decode_string(b, o, max_chars):
    """Decode a Gen 3 string (1 byte per char, 0xFF terminated)."""
    out = []
    for c in b[o:o + max_chars]:
        if c == 0xFF:
            break
        out.append(CHARMAP.get(c, "?"))
    return "".join(out)


def section_checksum(data):
    total = sum(struct.unpack_from(f"<{len(data) // 4}I", data)) & 0xFFFFFFFF
    return ((total >> 16) + total) & 0xFFFF


def level_from_exp(species, exp):
    table = EXP_TABLE[GROWTH_RATE[species]]
    level = 1
    while level < 100 and exp >= table[level]:
        level += 1
    return level


def national_dex(index):
    """Gen 3 internal species index -> national dex number (0 if not a real species)."""
    if 1 <= index <= 251:
        return index
    return HOENN_INDEX_TO_NATIONAL.get(index, 0)


# ---------------------------------------------------------------- save slots

def load_save(path):
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < SAVE_SIZE:
        sys.exit(f"File is {len(data)} bytes; expected at least {SAVE_SIZE} (128 KB). Not a Gen 3 save?")
    return data[:SAVE_SIZE]


def read_slot(data, slot):
    """Return (save_index, {section_id: data}, problems) for one save slot."""
    sections, problems, index = {}, [], None
    for i in range(SECTION_COUNT):
        base = slot * SLOT_SIZE + i * SECTION_SIZE
        sec = data[base:base + SECTION_SIZE]
        sid = u16(sec, 0xFF4)
        if u32(sec, 0xFF8) != SIGNATURE or sid >= SECTION_COUNT:
            problems.append(f"section at position {i} is unused/invalid")
            continue
        body = sec[:SECTION_DATA_SIZE[sid]]
        if section_checksum(body) != u16(sec, 0xFF6):
            problems.append(f"section {sid} checksum mismatch")
        sections[sid] = body
        index = u32(sec, 0xFFC)
    return index, sections, problems


def pick_slot(data):
    slots = [read_slot(data, s) for s in (0, 1)]
    valid = [s for s in (0, 1) if len(slots[s][1]) == SECTION_COUNT and not slots[s][2]]
    if not valid:
        details = "; ".join(f"slot {'AB'[s]}: {', '.join(slots[s][2]) or 'incomplete'}" for s in (0, 1))
        sys.exit(f"No complete, valid save slot found ({details}). Not an Emerald save?")
    best = max(valid, key=lambda s: slots[s][0])
    desc = ", ".join(
        f"{'AB'[s]}: {'ok' if s in valid else 'bad'} index={slots[s][0]}" for s in (0, 1)
    )
    return slots[best][1], f"save slot -> using {'AB'[best]} ({desc})"


# ---------------------------------------------------------------- pokemon

def decrypt_pokemon(raw):
    """Decrypt and unshuffle an 80- or 100-byte Pokemon. Returns (bytes, checksum_ok)."""
    pid, otid = u32(raw, 0), u32(raw, 4)
    key = pid ^ otid
    enc = raw[0x20:0x50]
    dec = b"".join(struct.pack("<I", u32(enc, o) ^ key) for o in range(0, 48, 4))
    order = SUB_ORDERS[pid % 24]
    subs = {letter: dec[i * 12:(i + 1) * 12] for i, letter in enumerate(order)}
    pk = bytearray(raw)
    pk[0x20:0x50] = subs["G"] + subs["A"] + subs["E"] + subs["M"]
    calc = sum(struct.unpack_from("<24H", pk, 0x20)) & 0xFFFF
    return bytes(pk), calc == u16(raw, 0x1C)


def gender_symbol(species, pid):
    rate = GENDER_RATE[species]
    if rate < 0:
        return ""
    if rate == 0:
        return " ♂"
    if rate == 8:
        return " ♀"
    return " ♀" if (pid & 0xFF) < rate * 32 - 1 else " ♂"


def move_list(pk):
    names = []
    for i in range(4):
        move = u16(pk, 0x2C + i * 2)
        if move:
            names.append(MOVES[move] if move < len(MOVES) else f"[move #{move}]")
    return ", ".join(names) or "(no moves)"


def describe_pokemon(raw, indent):
    """Text for a Pokemon (summary line, then its moves on a line starting with indent), or None if empty."""
    if not any(raw[:PK_SIZE]):
        return None
    if raw[0x13] & 0x01:
        return "Bad Egg"
    pk, ok = decrypt_pokemon(raw)
    if not ok:
        return "[corrupt data: checksum mismatch]"
    index = u16(pk, 0x20)
    if index == 0:
        return None
    species = national_dex(index)
    if not species:
        return f"[unknown species index {index}]"

    name = SPECIES[species]
    pid = u32(pk, 0)
    tid, sid = u16(pk, 4), u16(pk, 6)
    exp = u32(pk, 0x24)
    ivs = u32(pk, 0x48)
    nickname = decode_string(pk, 0x08, 10)

    if ivs & (1 << 30):
        return f"Egg ({name})"

    level = pk[0x54] if len(pk) > PK_SIZE else level_from_exp(species, exp)
    shiny = ((tid ^ sid ^ (pid >> 16) ^ (pid & 0xFFFF)) < 8)

    line = f"{name}{gender_symbol(species, pid)}  Lv {level}"
    # Gen 3 has no "nicknamed" flag; an unnicknamed Pokemon has its species name in caps.
    if nickname != name.upper():
        line += f'  "{nickname}"'
    if shiny:
        line += "  ★ shiny"
    return f"{line}\n{indent}{move_list(pk)}"


def dex_flag_regions(kind):
    """(section, offset) of every copy of the 'seen' or 'owned' flags."""
    if kind == "owned":
        return [(0, DEX_OWNED)]
    return [(0, DEX_SEEN)] + DEX_SEEN_COPIES


def dex_count(sections, kind):
    """Species counted the way the game does: a seen flag only counts if all copies agree."""
    count = 0
    for i in range(386):
        byte, bit = i >> 3, 1 << (i & 7)
        flags = [sections[s][o + byte] & bit for s, o in dex_flag_regions("seen")]
        if kind == "owned":
            flags.append(sections[0][DEX_OWNED + byte] & bit)
        if all(flags):
            count += 1
    return count


# ---------------------------------------------------------------- main

def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    data = load_save(sys.argv[1])
    sections, slot_desc = pick_slot(data)
    trainer_sec, party_sec = sections[0], sections[1]
    storage = b"".join(sections[i] for i in range(5, 14))

    print(f"File: {sys.argv[1]}")
    print(slot_desc)
    print()
    trainer = decode_string(trainer_sec, TRAINER_NAME, 7)
    money = u32(party_sec, MONEY) ^ u32(trainer_sec, SECURITY_KEY)
    print(f"Trainer: {trainer}  TID {u16(trainer_sec, TRAINER_TID):05d}  SID {u16(trainer_sec, TRAINER_SID):05d}")
    print(f"Play time: {u16(trainer_sec, PLAY_HOURS)}:{trainer_sec[PLAY_MINUTES]:02d}:{trainer_sec[PLAY_SECONDS]:02d}"
          f"  Money: {money}")
    print(f"Pokédex: seen {dex_count(sections, 'seen')}  owned {dex_count(sections, 'owned')}")
    print()

    count = min(u32(party_sec, PARTY_COUNT), 6)
    print(f"PARTY ({count}/6)")
    for i in range(count):
        o = PARTY_START + i * PARTY_PK_SIZE
        line = describe_pokemon(party_sec[o:o + PARTY_PK_SIZE], " " * 5)
        print(f"  {i + 1}. {line or '(empty?)'}")
    print()

    for box in range(BOX_COUNT):
        box_name = decode_string(storage, BOX_NAMES + box * BOX_NAME_SIZE, 8) or f"Box {box + 1}"
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
