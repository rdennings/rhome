#!/usr/bin/env python3
"""Copy one Pokemon from a Platinum save into an X/Y save, the way Poke Transfer + Poke Transporter would.

Usage:
    python3 transfer_pt_to_xy.py PLATINUM.sav PARTY_SLOT XY_IN_main XY_OUT_main party|box [BOX]

  PARTY_SLOT  -> Platinum party slot (1-6) to copy
  party       -> add to the end of the X/Y party (needs a free slot)
  box [BOX]   -> first empty slot of X/Y box BOX (default 1)
The Platinum save is never modified; XY_OUT_main is a new file.

The conversion follows PKHeX's PK4.ConvertToPK5 and PK5.ConvertToPK6 (github.com/kwsch/PKHeX):
the two hops a Platinum Pokemon took to reach X/Y (Platinum -> Black/White 2 -> Bank -> X/Y).
"""
import datetime
import random
import struct
import sys

import read_pt_save as pt
import read_xy_save as xy
from gen4_data import EXP_TABLE
from gen6_data import ABILITIES, BASE_FRIENDSHIP, BASE_STATS, FORM_COUNT, GROWTH_RATE, MOVE_PP, SPECIES

TRANSFER_LOCATION = 30001       # "Poke Transfer Lab"
TRANSFER_LOCATION_CROWN = 30010  # Raikou/Entei/Suicune event
TRANSFER_LOCATION_CELEBI = 30011
GAME_X = 24
DEX_LANGUAGE_BITS = 7
# Poke Transfer refuses Pokemon that know HMs (DPPt + HGSS HMs)
HM_MOVES = {15, 19, 57, 70, 127, 249, 250, 431, 432}


# ---------------------------------------------------------------- helpers

def encode_utf16(text, size):
    out = bytearray(size)
    data = text.encode("utf-16-le")[:size - 2]
    out[:len(data)] = data
    return bytes(out)


def level_from_exp(species, exp):
    table = EXP_TABLE[GROWTH_RATE[species]]
    level = 1
    while level < 100 and exp >= table[level]:
        level += 1
    return level


def calc_stats(species, level, iv32, evs, nature):
    """Gen 3+ stat formula. evs/ivs/result are in (HP, Atk, Def, Spe, SpA, SpD) order."""
    ivs = [(iv32 >> (5 * i)) & 31 for i in range(6)]
    base = BASE_STATS[species]
    stats = [((2 * base[0] + ivs[0] + evs[0] // 4) * level) // 100 + level + 10]
    # Nature raises stat (nature // 5) and lowers stat (nature % 5), counting Atk, Def, Spe, SpA, SpD
    up, down = nature // 5, nature % 5
    for i in range(1, 6):
        value = ((2 * base[i] + ivs[i] + evs[i] // 4) * level) // 100 + 5
        if up != down and i - 1 == up:
            value = value * 110 // 100
        elif up != down and i - 1 == down:
            value = value * 90 // 100
        stats.append(value)
    return stats


def transfer_pid(pid, otid):
    """Gen 6 doubled the shiny odds; Transporter flips the top PID bit if a Pokemon would newly become shiny."""
    tmp = pid ^ otid
    xor = (tmp ^ (tmp >> 16)) & 0xFFFF
    return pid ^ 0x80000000 if (xor & 0xFFF8) == 8 else pid


def encrypt_pk6(pk):
    ec = pt.u32(pk, 0)
    order = pt.BLOCK_ORDERS[((ec >> 13) & 31) % 24]
    blocks = {letter: pk[0x08 + i * 56:0x08 + (i + 1) * 56] for i, letter in enumerate("ABCD")}
    out = bytearray(pk)
    out[0x08:xy.PK_SIZE] = b"".join(blocks[letter] for letter in order)
    out = pt.prng_xor(out, 0x08, xy.PK_SIZE, ec)
    if len(out) > xy.PK_SIZE:
        out = pt.prng_xor(out, xy.PK_SIZE, len(out), ec)
    return bytes(out)


# ---------------------------------------------------------------- conversion

def convert_pk4_to_pk6(pk4, save, party):
    """Decrypted Gen 4 Pokemon -> encrypted Gen 6 Pokemon. Returns (bytes, notes)."""
    notes = []
    species = pt.u16(pk4, 0x08)
    pid = pt.u32(pk4, 0x00)
    tid, sid = pt.u16(pk4, 0x0C), pt.u16(pk4, 0x0E)
    iv32 = pt.u32(pk4, 0x38)
    if iv32 & (1 << 30):
        sys.exit("Eggs can't go through Poke Transfer.")
    if FORM_COUNT[species] > 1:
        sys.exit(f"{SPECIES[species]} has alternate forms; form handling isn't supported yet.")
    moves4 = [pt.u16(pk4, 0x28 + i * 2) for i in range(4)]
    if HM_MOVES & set(moves4):
        sys.exit("This Pokemon knows an HM move; Poke Transfer refuses those. Delete the HM first.")
    if pt.u16(pk4, 0x0A):
        notes.append(f"held item #{pt.u16(pk4, 0x0A)} removed (Transporter doesn't carry items)")
    if any(pk4[0x24:0x28]) or any(pk4[0x3C:0x40]) or any(pk4[0x60:0x68]):
        notes.append("ribbons dropped (ribbon mapping not supported yet)")

    flags4 = pk4[0x40]
    fateful = flags4 & 1
    gender = 2 if flags4 & 0x04 else (1 if flags4 & 0x02 else 0)
    exp = pt.u32(pk4, 0x10)
    level = level_from_exp(species, exp)
    nature = pid % 25

    # Ability: keep the slot the Gen 4 ability occupies; otherwise fall back to PID bit 0
    abilities = ABILITIES[species]
    ability_id = pk4[0x15]
    slot = 0 if ability_id == abilities[0] else 1 if ability_id == abilities[1] else pid & 1

    # Moves: compact, refill PP (both hops heal PP)
    moves, ppups = [], []
    for i, move in enumerate(moves4):
        if move:
            moves.append(move)
            ppups.append(pk4[0x34 + i])
    moves += [0] * (4 - len(moves))
    ppups += [0] * (4 - len(ppups))
    pps = [MOVE_PP[m] * (5 + u) // 5 if m else 0 for m, u in zip(moves, ppups)]

    if fateful and species in (243, 244, 245):
        met_location = TRANSFER_LOCATION_CROWN
    elif fateful and species == 251:
        met_location = TRANSFER_LOCATION_CELEBI
    else:
        met_location = TRANSFER_LOCATION

    friendship = BASE_FRIENDSHIP[species]
    nicknamed = bool(iv32 & (1 << 31))
    nickname = pt.decode_string(pk4, 0x48, 11) if nicknamed else SPECIES[species]
    ot_name = pt.decode_string(pk4, 0x68, 8).replace("'", "’")
    today = datetime.date.today()
    feeling = random.randrange(10)

    pk = bytearray(xy.PARTY_PK_SIZE if party else xy.PK_SIZE)
    struct.pack_into("<I", pk, 0x00, pid)                       # encryption constant = old PID
    struct.pack_into("<HHHHI", pk, 0x08, species, 0, tid, sid, exp)
    pk[0x14] = abilities[slot]
    pk[0x15] = 1 << slot
    struct.pack_into("<I", pk, 0x18, transfer_pid(pid, tid | (sid << 16)))
    pk[0x1C] = nature
    pk[0x1D] = fateful | (gender << 1)
    evs = [min(v, 252) for v in pk4[0x18:0x1E]]
    pk[0x1E:0x24] = bytes(evs)
    pk[0x24:0x2A] = pk4[0x1E:0x24]                              # contest stats
    pk[0x2A] = pk4[0x16]                                        # markings
    pk[0x2B] = pk4[0x82]                                        # Pokerus
    pk[0x40:0x5A] = encode_utf16(nickname, 0x1A)
    struct.pack_into("<4H4B4B", pk, 0x5A, *moves, *pps, *ppups)
    struct.pack_into("<I", pk, 0x74, iv32)
    pk[0x78:0x92] = encode_utf16(xy.decode_string(save, xy.TRAINER_NAME, 13), 0x1A)
    pk[0x92] = save[xy.TRAINER_GENDER]                          # handling trainer = this X/Y player
    pk[0x93] = 1                                                # currently with the handling trainer
    pk[0x94] = save[xy.TRAINER_REGION]                          # first geolocation
    pk[0x95] = save[xy.TRAINER_COUNTRY]
    pk[0xA2] = friendship
    pk[0xA4], pk[0xA5], pk[0xA6] = 1, 4, feeling                # Transporter memory (intensity, memory, feeling)
    pk[0xB0:0xCA] = encode_utf16(ot_name, 0x1A)
    pk[0xCA] = friendship
    pk[0xD1:0xD4] = pk4[0x78:0x7B]                              # egg date
    pk[0xD4:0xD7] = bytes([today.year - 2000, today.month, today.day])  # met date = transfer date
    struct.pack_into("<HHB", pk, 0xD8, pt.u16(pk4, 0x7E), met_location, pk4[0x83])
    pk[0xDD] = level | (pk4[0x84] & 0x80)                       # met level = level at transfer, OT gender
    pk[0xDE] = pk4[0x85]                                        # encounter type
    pk[0xDF] = pk4[0x5F]                                        # origin game
    pk[0xE0] = save[xy.TRAINER_COUNTRY]
    pk[0xE1] = save[xy.TRAINER_REGION]
    pk[0xE2] = save[xy.TRAINER_CONSOLE_REGION]
    pk[0xE3] = pk4[0x17]                                        # language

    if party:
        stats = calc_stats(species, level, iv32, evs, nature)
        pk[0xEC] = level
        struct.pack_into("<H6H", pk, 0xF0, stats[0], *stats)
    struct.pack_into("<H", pk, 0x06, sum(struct.unpack_from("<112H", pk, 0x08)) & 0xFFFF)

    notes.append(f"met location: Poke Transfer Lab, Lv {level}, on {today.isoformat()}")
    notes.append(f"friendship reset to {friendship}; handled by {xy.decode_string(save, xy.TRAINER_NAME, 13)} "
                 f"with a Transporter memory (feeling {feeling})")
    return encrypt_pk6(bytes(pk)), notes


# ---------------------------------------------------------------- save edits

def set_flag(save, base, bit):
    save[base + (bit >> 3)] |= 1 << (bit & 7)


def mark_dex(save, pk6_plain):
    """Mirror PKHeX Zukan6XY.SetDex: seen (by gender/shiny), owned, displayed, language."""
    species = pt.u16(pk6_plain, 0x08)
    bit = species - 1
    gender = (pk6_plain[0x1D] >> 1) & 3
    pid, tid, sid = pt.u32(pk6_plain, 0x18), pt.u16(pk6_plain, 0x0C), pt.u16(pk6_plain, 0x0E)
    shiny = (tid ^ sid ^ (pid >> 16) ^ (pid & 0xFFFF)) < 16
    region = (2 if shiny else 0) | (gender & 1)

    seen_before = any(xy.flag(save, xy.DEX_SEEN + r * xy.DEX_REGION_SIZE, bit) for r in range(4))
    if species == 327 and not seen_before:
        struct.pack_into("<I", save, xy.DEX_SPINDA, pt.u32(pk6_plain, 0x00))
    set_flag(save, xy.DEX_SEEN + region * xy.DEX_REGION_SIZE, bit)
    # X/Y counts Pokemon from Gen 3-5 games with a separate "foreign" owned flag
    if pk6_plain[0xDF] < GAME_X and species <= xy.DEX_MAX_FOREIGN:
        set_flag(save, xy.DEX_FOREIGN, bit)
        owned_kind = "owned (from an older game)"
    else:
        set_flag(save, xy.DEX_CAUGHT, bit)
        owned_kind = "owned"
    if not any(xy.flag(save, xy.DEX_DISPLAYED + r * xy.DEX_REGION_SIZE, bit) for r in range(4)):
        set_flag(save, xy.DEX_DISPLAYED + region * xy.DEX_REGION_SIZE, bit)
    lang = pk6_plain[0xE3] - 1
    if lang > 5:
        lang -= 1
    if 0 <= lang <= 6:
        set_flag(save, xy.DEX_LANGUAGE, bit * DEX_LANGUAGE_BITS + lang)
    return f"Pokédex: seen ({['male', 'female', 'male shiny', 'female shiny'][region]}), {owned_kind}, language"


def refresh_checksums(save, block_ids):
    for block_id in block_ids:
        struct.pack_into("<H", save, xy.stored_checksum_offset(block_id), xy.block_checksum(save, block_id))


def main():
    args = sys.argv[1:]
    if len(args) not in (5, 6) or args[4] not in ("party", "box"):
        sys.exit(__doc__)
    pt_path, slot, xy_in, xy_out, target = args[:5]
    box = int(args[5]) if len(args) == 6 else 1

    general, _ = pt.pick_block(pt.load_save(pt_path), 0, pt.GENERAL_SIZE, "general")
    slot = int(slot)
    if not 1 <= slot <= general[pt.PARTY_COUNT]:
        sys.exit(f"Platinum party slot {slot} is empty.")
    o = pt.PARTY_START + (slot - 1) * pt.PARTY_PK_SIZE
    pk4, ok = pt.decrypt_pokemon(general[o:o + pt.PARTY_PK_SIZE])
    if not ok:
        sys.exit("That Platinum Pokemon fails its checksum; refusing to copy it.")

    save = bytearray(xy.load_save(xy_in))
    if xy.bad_blocks(save):
        sys.exit(f"X/Y save already has bad block checksums {xy.bad_blocks(save)}; refusing to edit it.")

    pk6, notes = convert_pk4_to_pk6(pk4, save, party=(target == "party"))
    plain, _ = xy.decrypt_pokemon(pk6)

    if target == "party":
        count = save[xy.PARTY_COUNT]
        if count >= 6:
            sys.exit("X/Y party is full.")
        o = xy.PARTY_START + count * xy.PARTY_PK_SIZE
        save[o:o + xy.PARTY_PK_SIZE] = pk6
        save[xy.PARTY_COUNT] = count + 1
        where, blocks = f"party slot {count + 1}", [xy.BLOCK_PARTY, xy.BLOCK_DEX]
    else:
        for s in range(xy.BOX_SLOTS):
            o = xy.BOX_START + ((box - 1) * xy.BOX_SLOTS + s) * xy.PK_SIZE
            if pt.u16(xy.decrypt_pokemon(save[o:o + xy.PK_SIZE])[0], 0x08) == 0:
                save[o:o + xy.PK_SIZE] = pk6
                break
        else:
            sys.exit(f"X/Y box {box} is full.")
        where, blocks = f"box {box}, slot {s + 1}", [xy.BLOCK_BOXES, xy.BLOCK_DEX]

    notes.insert(0, mark_dex(save, plain))
    refresh_checksums(save, blocks)
    with open(xy_out, "wb") as f:
        f.write(save)

    print(f"Copied {SPECIES[pt.u16(pk4, 0x08)]} to X/Y {where} -> {xy_out}")
    for note in notes:
        print(f"  note: {note}")


if __name__ == "__main__":
    main()
