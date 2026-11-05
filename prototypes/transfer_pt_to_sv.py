#!/usr/bin/env python3
"""Copy one Pokemon from a Platinum save straight into a Scarlet/Violet save.

Usage:
    python3 transfer_pt_to_sv.py PLATINUM.sav PARTY_SLOT SV_IN_main SV_OUT_main party|box [BOX]

The Platinum save is never modified; SV_OUT_main is a new file.

The Pokemon gets the same result as the real route Platinum -> Black/White (Poke Transfer)
-> X/Y (Poke Transporter) -> Bank -> HOME -> Scarlet/Violet, following PKHeX
(github.com/kwsch/PKHeX): PK4.ConvertToPK5, PK5.ConvertToPK6, PK6.ConvertToPK7,
PKH/GameDataPK9 (HOME), then PK9.UpdateHandler when Scarlet/Violet receives it.
Entering S/V for the first time, HOME resets its moves to its latest S/V level-up moves.

Pokedex: only the base-game "Paldea" block layout is supported. One deliberate difference from
PKHeX: it skips species whose personal "DexGroup" is above 1 on base-game saves, but every
dex species (Pikachu, Kirlia...) has DexGroup 2, so here any species with a Paldea dex number
is registered, as the base game does when you catch one.
"""
import datetime
import random
import struct
import sys

import read_pt_save as pt
import read_sv_save as sv
import swish_crypto
from gen9_data import (ABILITIES, BASE_FRIENDSHIP, BASE_STATS, FORM_COUNT, LEVEL_UP, MOVE_PP, MOVES, PALDEA_DEX,
                       PRESENT_IN_GAME, SPECIES, TYPES)
from transfer_pt_to_swsh import calc_stats, encrypt_pk8, random_scalar
from transfer_pt_to_xy import HM_MOVES, TRANSFER_LOCATION, TRANSFER_LOCATION_CELEBI, TRANSFER_LOCATION_CROWN, \
    encode_utf16, level_from_exp, transfer_pid

TYPE_NORMAL = 0
AFFIXED_RIBBON_NONE = 0xFF  # -1 as a signed byte


def reset_moves(species, level):
    """HOME's first-entry moves: the last 4 distinct level-up moves at or below this level
    (PKHeX Learnset.SetEncounterMoves, including its final rotation)."""
    moves, ctr = [0, 0, 0, 0], 0
    for move, req in LEVEL_UP[species]:
        if req < 1:
            continue
        if req > level:
            break
        if move not in moves:
            moves[ctr & 3] = move
            ctr += 1
    if ctr > 4:
        for _ in range(ctr & 3):
            moves = moves[1:] + moves[:1]
    return moves


def convert_pk4_to_pk9(pk4, status):
    """Decrypted Gen 4 Pokemon -> (encrypted PK9 in party format, decrypted PK9, notes)."""
    notes = []
    species = pt.u16(pk4, 0x08)
    pid = pt.u32(pk4, 0x00)
    tid, sid = pt.u16(pk4, 0x0C), pt.u16(pk4, 0x0E)
    iv32 = pt.u32(pk4, 0x38)
    if iv32 & (1 << 30):
        sys.exit("Eggs can't go through Poke Transfer.")
    if species >= len(PRESENT_IN_GAME) or not PRESENT_IN_GAME[species]:
        sys.exit(f"{SPECIES[species] if species < len(SPECIES) else species} isn't in Scarlet/Violet.")
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

    abilities = ABILITIES[species]
    ability_id = pk4[0x15]
    slot = 0 if ability_id == abilities[0] else 1 if ability_id == abilities[1] else pid & 1

    moves = reset_moves(species, level)
    pps = [MOVE_PP[m] if m else 0 for m in moves]

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
    evs = [min(v, 252) for v in pk4[0x18:0x1E]]
    markings = sum(1 << (2 * i) for i in range(6) if pk4[0x16] & (1 << i))  # Gen 7+: 2 bits each, 1 = blue
    tracker = random.getrandbits(64) or 1
    height, weight = random_scalar(), random_scalar()
    type1, type2 = TYPES[species]
    tera = type1 if type1 != TYPE_NORMAL else type2

    pk = bytearray(sv.PK_SIZE)
    struct.pack_into("<I", pk, 0x00, pid)                       # encryption constant = original PID
    struct.pack_into("<HHHHI", pk, 0x08, sv.to_internal(species), 0, tid, sid, exp)
    struct.pack_into("<H", pk, 0x14, abilities[slot])
    pk[0x16] = 1 << slot                                        # ability number
    struct.pack_into("<H", pk, 0x18, markings)
    struct.pack_into("<I", pk, 0x1C, transfer_pid(pid, tid | (sid << 16)))
    pk[0x20] = pk[0x21] = nature                                # nature, stat nature
    pk[0x22] = fateful | (gender << 1)
    pk[0x26:0x2C] = bytes(evs)
    pk[0x2C:0x32] = pk4[0x1E:0x24]                              # contest stats
    pk[0x32] = pk4[0x82]                                        # Pokerus
    pk[0x48], pk[0x49], pk[0x4A] = height, weight, height       # HOME's random size; S/V scale = height
    pk[0x58:0x72] = encode_utf16(nickname, 0x1A)
    struct.pack_into("<4H4B", pk, 0x72, *moves, *pps)           # PP Ups cleared by the reset
    struct.pack_into("<I", pk, 0x8C, iv32)
    pk[0x94] = pk[0x95] = tera                                  # Tera Type (original, override)
    # Handler: the Scarlet/Violet player (OT differs, so the game treats it as traded)
    pk[0xA8:0xC2] = encode_utf16(sv.decode_string(status, sv.TRAINER_NAME, 12), 0x1A)
    pk[0xC2] = status[sv.TRAINER_GENDER]
    pk[0xC3] = status[sv.TRAINER_LANGUAGE]
    pk[0xC4] = 1
    pk[0xC8] = friendship
    pk[0xCE] = pk4[0x5F]                                        # origin game (Platinum)
    pk[0xD4] = AFFIXED_RIBBON_NONE                              # 0 would display ribbon #0 (Kalos Champion)
    pk[0xD5] = pk4[0x17]                                        # language
    pk[0xF8:0x112] = encode_utf16(ot_name, 0x1A)
    pk[0x112] = friendship
    pk[0x119:0x11C] = pk4[0x78:0x7B]                            # egg date
    pk[0x11C:0x11F] = bytes([today.year - 2000, today.month, today.day])  # met date = transfer date
    pk[0x11F] = level                                           # obedience level = met level
    struct.pack_into("<HHB", pk, 0x120, pt.u16(pk4, 0x7E), met_location, pk4[0x83])
    pk[0x125] = level | (pk4[0x84] & 0x80)                      # met level = level at transfer, OT gender
    struct.pack_into("<Q", pk, 0x127, tracker)                  # HOME tracker

    stats = calc_stats(BASE_STATS[species], level, iv32, evs, nature)
    struct.pack_into("<H", pk, 0x8A, stats[0])                  # current HP
    pk[0x148] = level
    struct.pack_into("<6H", pk, 0x14A, *stats)
    struct.pack_into("<H", pk, 0x06, sum(struct.unpack_from("<160H", pk, 0x08)) & 0xFFFF)

    notes.append("moves reset to S/V level-up moves: " + ", ".join(MOVES[m] for m in moves if m))
    notes.append(f"met location: Poke Transfer Lab, Lv {level}, on {today.isoformat()}; Tera Type id {tera}")
    notes.append(f"friendship {friendship}; handled by {sv.decode_string(status, sv.TRAINER_NAME, 12)}; "
                 f"HOME tracker {tracker:016X}; size {height}/{weight}")
    return encrypt_pk8(bytes(pk)), bytes(pk), notes


def species_at_paldea(index):
    for species, number in enumerate(PALDEA_DEX):
        if number == index:
            return species
    return 0


def mark_dex(blocks, pk, save_language):
    """Mirror PKHeX Zukan9Paldea.Register (see the module note on DexGroup)."""
    if sv.KEY_ZUKAN_KITAKAMI in blocks and blocks[sv.KEY_ZUKAN_KITAKAMI].data:
        sys.exit("This save uses the newer (2.0.1+) Pokedex layout, which isn't supported yet.")
    dex = blocks[sv.KEY_ZUKAN_PALDEA].data
    species = sv.to_national(pt.u16(pk, 0x08))
    index = PALDEA_DEX[species]
    if not index:
        return "Pokédex: not in the Paldea dex, nothing registered"

    def entry(sp):
        return sv.to_internal(sp) * sv.DEX_ENTRY_SIZE

    e = entry(species)
    gender = (pk[0x22] >> 1) & 3
    form = struct.unpack_from("<H", pk, 0x24)[0]
    pid, tid, sid = pt.u32(pk, 0x1C), pt.u16(pk, 0x0C), pt.u16(pk, 0x0E)
    shiny = (tid ^ sid ^ (pid >> 16) ^ (pid & 0xFFFF)) < 16

    def lang_bit(lang):
        return 1 << (0 if lang > 10 or lang == 6 or lang <= 0 else (lang - 2 if lang >= 7 else lang - 1))

    if pt.u32(dex, e) == 0:
        dex[e + 0x0D] = 1                                       # "new"
    struct.pack_into("<I", dex, e, 3)                           # caught
    struct.pack_into("<I", dex, e + 0x04, pt.u32(dex, e + 0x04) | (1 << form))
    struct.pack_into("<H", dex, e + 0x08, pt.u16(dex, e + 0x08) | (1 << gender))
    struct.pack_into("<I", dex, e + 0x10, form)                 # displayed form
    dex[e + 0x14] = gender if gender < 2 else 2                 # displayed gender
    if shiny:
        dex[e + 0x15] = dex[e + 0x0C] = 1
    langs = pt.u16(dex, e + 0x0A) | lang_bit(pk[0xD5]) | lang_bit(save_language)
    struct.pack_into("<H", dex, e + 0x0A, langs)

    heard = []
    for neighbour in (index - 1, index + 1):
        if neighbour <= 9:                                      # starters' lines stay hidden
            continue
        sp = species_at_paldea(neighbour)
        if sp and pt.u32(dex, entry(sp)) == 0:
            struct.pack_into("<I", dex, entry(sp), 1)           # "heard of"
            heard.append(SPECIES[sp])
    extra = f"; neighbours now heard of: {', '.join(heard)}" if heard else ""
    return f"Pokédex: Paldea #{index} caught ({['male', 'female', 'genderless'][min(gender, 2)]}), language{extra}"


def main():
    args = sys.argv[1:]
    if len(args) not in (5, 6) or args[4] not in ("party", "box"):
        sys.exit(__doc__)
    pt_path, slot, sv_in, sv_out, target = args[:5]
    box = int(args[5]) if len(args) == 6 else 1

    general, _ = pt.pick_block(pt.load_save(pt_path), 0, pt.GENERAL_SIZE, "general")
    slot = int(slot)
    if not 1 <= slot <= general[pt.PARTY_COUNT]:
        sys.exit(f"Platinum party slot {slot} is empty.")
    o = pt.PARTY_START + (slot - 1) * pt.PARTY_PK_SIZE
    pk4, ok = pt.decrypt_pokemon(general[o:o + pt.PARTY_PK_SIZE])
    if not ok:
        sys.exit("That Platinum Pokemon fails its checksum; refusing to copy it.")

    with open(sv_in, "rb") as f:
        raw = f.read()
    if not swish_crypto.hash_valid(raw):
        sys.exit("Scarlet/Violet save hash is already invalid; refusing to edit it.")
    block_list = swish_crypto.decrypt(raw)
    blocks = {b.key: b for b in block_list}
    status = blocks[sv.KEY_MY_STATUS].data

    pk9, plain, notes = convert_pk4_to_pk9(pk4, status)
    if target == "party":
        party = blocks[sv.KEY_PARTY].data
        count = party[sv.PARTY_COUNT]
        if count >= 6:
            sys.exit("Scarlet/Violet party is full.")
        party[count * sv.PK_SIZE:(count + 1) * sv.PK_SIZE] = pk9
        party[sv.PARTY_COUNT] = count + 1
        where = f"party slot {count + 1}"
    else:
        boxes = blocks[sv.KEY_BOX].data
        for s in range(sv.BOX_SLOTS):
            o = ((box - 1) * sv.BOX_SLOTS + s) * sv.PK_SIZE
            if pt.u16(sv.decrypt_pokemon(boxes[o:o + sv.PK_SIZE])[0], 0x08) == 0:
                boxes[o:o + sv.PK_SIZE] = pk9
                break
        else:
            sys.exit(f"Scarlet/Violet box {box} is full.")
        where = f"box {box}, slot {s + 1}"
    notes.insert(0, mark_dex(blocks, plain, status[sv.TRAINER_LANGUAGE]))

    with open(sv_out, "wb") as f:
        f.write(swish_crypto.encrypt(block_list))

    print(f"Copied {SPECIES[pt.u16(pk4, 0x08)]} to Scarlet/Violet {where} -> {sv_out}")
    for note in notes:
        print(f"  note: {note}")


if __name__ == "__main__":
    main()
