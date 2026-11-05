#!/usr/bin/env python3
"""Copy one Pokemon from a Platinum save straight into a Sword/Shield party.

Usage:
    python3 transfer_pt_to_swsh.py PLATINUM.sav PARTY_SLOT SWSH_IN_main SWSH_OUT_main

The Platinum save is never modified; SWSH_OUT_main is a new file.

The Pokemon gets the same result as the real route Platinum -> Black/White (Poke Transfer)
-> X/Y (Poke Transporter) -> Bank -> HOME -> Sword/Shield, following PKHeX
(github.com/kwsch/PKHeX): PK4.ConvertToPK5, PK5.ConvertToPK6, PK6.ConvertToPK7,
PKH/GameDataPK8 (HOME), then PK8.UpdateHandler when Sword/Shield receives it.
It never visits those games, so no in-between handler is recorded.
"""
import datetime
import random
import struct
import sys

import read_pt_save as pt
import read_swsh_save as sw
import swish_crypto
from gen8_data import (ABILITIES, BASE_FRIENDSHIP, BASE_STATS, DUMMIED_MOVES, FORM_COUNT, GALAR_DEX, MOVE_PP,
                       MOVES, PRESENT_IN_GAME, SPECIES)
from transfer_pt_to_xy import HM_MOVES, TRANSFER_LOCATION, TRANSFER_LOCATION_CELEBI, TRANSFER_LOCATION_CROWN, \
    encode_utf16, level_from_exp, transfer_pid

KEY_ZUKAN_GALAR = 0x4716C404
DEX_ENTRY_SIZE = 0x30
DEX_OWNED = 0x20            # u32: bit 0 owned, bits 2-14 languages, 15-27 form shown, 29-30 gender shown, 31 shiny shown
DEX_BATTLED = 0x24
AFFIXED_RIBBON_NONE = 0xFF  # -1 as a signed byte


def calc_stats(base, level, iv32, evs, nature):
    """Gen 3+ stat formula; base/evs/result in (HP, Atk, Def, Spe, SpA, SpD) order."""
    ivs = [(iv32 >> (5 * i)) & 31 for i in range(6)]
    stats = [((2 * base[0] + ivs[0] + evs[0] // 4) * level) // 100 + level + 10]
    up, down = nature // 5, nature % 5
    for i in range(1, 6):
        value = ((2 * base[i] + ivs[i] + evs[i] // 4) * level) // 100 + 5
        if up != down and i - 1 == up:
            value = value * 110 // 100
        elif up != down and i - 1 == down:
            value = value * 90 // 100
        stats.append(value)
    return stats


def random_scalar():
    """HOME gives Pokemon without a size a random height/weight (PKHeX PokeSizeUtil.GetRandomScalar)."""
    return random.randrange(0x81) + random.randrange(0x80)


def encrypt_pk8(pk):
    ec = pt.u32(pk, 0)
    order = pt.BLOCK_ORDERS[((ec >> 13) & 31) % 24]
    blocks = {letter: pk[0x08 + i * 0x50:0x08 + (i + 1) * 0x50] for i, letter in enumerate("ABCD")}
    out = bytearray(pk)
    out[0x08:sw.PK_STORED] = b"".join(blocks[letter] for letter in order)
    out = pt.prng_xor(out, 0x08, sw.PK_STORED, ec)
    return bytes(pt.prng_xor(out, sw.PK_STORED, sw.PK_SIZE, ec))


def convert_pk4_to_pk8(pk4, status):
    """Decrypted Gen 4 Pokemon -> (encrypted party-format PK8, decrypted PK8, notes)."""
    notes = []
    species = pt.u16(pk4, 0x08)
    pid = pt.u32(pk4, 0x00)
    tid, sid = pt.u16(pk4, 0x0C), pt.u16(pk4, 0x0E)
    iv32 = pt.u32(pk4, 0x38)
    if iv32 & (1 << 30):
        sys.exit("Eggs can't go through Poke Transfer.")
    if species >= len(PRESENT_IN_GAME) or not PRESENT_IN_GAME[species]:
        sys.exit(f"{SPECIES[species] if species < len(SPECIES) else species} isn't in Sword/Shield.")
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

    # Moves: drop ones Gen 8 removed, compact, refill PP
    moves, ppups = [], []
    for i, move in enumerate(moves4):
        if not move:
            continue
        if move in DUMMIED_MOVES:
            notes.append(f"move {MOVES[move]} dropped (not in Sword/Shield)")
            continue
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
    evs = [min(v, 252) for v in pk4[0x18:0x1E]]
    markings = sum(1 << (2 * i) for i in range(6) if pk4[0x16] & (1 << i))  # Gen 7+: 2 bits each, 1 = blue
    tracker = random.getrandbits(64) or 1

    pk = bytearray(sw.PK_SIZE)
    struct.pack_into("<I", pk, 0x00, pid)                       # encryption constant = original PID
    struct.pack_into("<HHHHI", pk, 0x08, species, 0, tid, sid, exp)
    struct.pack_into("<H", pk, 0x14, abilities[slot])
    pk[0x16] = 1 << slot                                        # ability number
    struct.pack_into("<H", pk, 0x18, markings)
    struct.pack_into("<I", pk, 0x1C, transfer_pid(pid, tid | (sid << 16)))
    pk[0x20] = pk[0x21] = nature                                # nature, stat nature
    pk[0x22] = fateful | (gender << 2)
    pk[0x26:0x2C] = bytes(evs)
    pk[0x2C:0x32] = pk4[0x1E:0x24]                              # contest stats
    pk[0x32] = pk4[0x82]                                        # Pokerus
    pk[0x50], pk[0x51] = random_scalar(), random_scalar()       # height/weight, assigned by HOME
    pk[0x58:0x72] = encode_utf16(nickname, 0x1A)
    struct.pack_into("<4H4B4B", pk, 0x72, *moves, *pps, *ppups)
    struct.pack_into("<I", pk, 0x8C, iv32)
    # Handler: the Sword/Shield player (OT differs, so the game treats it as traded)
    pk[0xA8:0xC2] = encode_utf16(sw.decode_string(status, sw.TRAINER_NAME, 12), 0x1A)
    pk[0xC2] = status[sw.TRAINER_GENDER]
    pk[0xC3] = status[sw.TRAINER_LANGUAGE]
    pk[0xC4] = 1
    pk[0xC8] = friendship
    pk[0xDE] = pk4[0x5F]                                        # origin game (Platinum)
    pk[0xE2] = pk4[0x17]                                        # language
    pk[0xE8] = AFFIXED_RIBBON_NONE                              # 0 would display ribbon #0 (Kalos Champion)
    pk[0xF8:0x112] = encode_utf16(ot_name, 0x1A)
    pk[0x112] = friendship
    pk[0x119:0x11C] = pk4[0x78:0x7B]                            # egg date
    pk[0x11C:0x11F] = bytes([today.year - 2000, today.month, today.day])  # met date = transfer date
    struct.pack_into("<HHB", pk, 0x120, pt.u16(pk4, 0x7E), met_location, pk4[0x83])
    pk[0x125] = level | (pk4[0x84] & 0x80)                      # met level = level at transfer, OT gender
    struct.pack_into("<Q", pk, 0x135, tracker)                  # HOME tracker

    stats = calc_stats(BASE_STATS[species], level, iv32, evs, nature)
    struct.pack_into("<H", pk, 0x8A, stats[0])                  # current HP
    pk[0x148] = level
    struct.pack_into("<6H", pk, 0x14A, *stats)
    struct.pack_into("<H", pk, 0x06, sum(struct.unpack_from("<160H", pk, 0x08)) & 0xFFFF)

    notes.append(f"met location: Poke Transfer Lab, Lv {level}, on {today.isoformat()}")
    notes.append(f"friendship {friendship}; handled by {sw.decode_string(status, sw.TRAINER_NAME, 12)}; "
                 f"HOME tracker {tracker:016X}; size {pk[0x50]}/{pk[0x51]}")
    return encrypt_pk8(bytes(pk)), bytes(pk), notes


def mark_dex(zukan, pk):
    """Mirror PKHeX Zukan8.SetDex for the Galar dex."""
    species = pt.u16(pk, 0x08)
    index = GALAR_DEX[species]
    if not index:
        return "Pokédex: not in the Galar dex, nothing to set (DLC dexes not supported yet)"
    base = (index - 1) * DEX_ENTRY_SIZE
    gender = (pk[0x22] >> 2) & 3
    pid, tid, sid = pt.u32(pk, 0x1C), pt.u16(pk, 0x0C), pt.u16(pk, 0x0E)
    shiny = (tid ^ sid ^ (pid >> 16) ^ (pid & 0xFFFF)) < 16
    region = (gender & 1) | (2 if shiny else 0)
    form = struct.unpack_from("<H", pk, 0x24)[0]

    zukan[base + region * 8 + (form >> 3)] |= 1 << (form & 7)  # seen (this form, gender, shininess)
    owned = struct.unpack_from("<I", zukan, base + DEX_OWNED)[0]
    was_owned = owned & 1
    owned |= 1
    lang = pk[0xE2]
    lang_index = -1 if lang > 10 or lang == 6 or lang <= 0 else (lang - 2 if lang >= 7 else lang - 1)
    if lang_index >= 0:
        owned |= 1 << (2 + lang_index)
    if not was_owned:
        owned = (owned & ~(0x1FFF << 15)) | ((form & 0x1FFF) << 15)
        if shiny:
            owned |= 1 << 31
        owned = (owned & ~(3 << 29)) | ((gender & 1) << 29)
    struct.pack_into("<I", zukan, base + DEX_OWNED, owned)
    if struct.unpack_from("<I", zukan, base + DEX_BATTLED)[0] == 0:
        struct.pack_into("<I", zukan, base + DEX_BATTLED, 1)
    return f"Pokédex: Galar #{index} seen ({['male', 'female', 'male shiny', 'female shiny'][region]}), owned, language"


def main():
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    pt_path, slot, sw_in, sw_out = sys.argv[1:]

    general, _ = pt.pick_block(pt.load_save(pt_path), 0, pt.GENERAL_SIZE, "general")
    slot = int(slot)
    if not 1 <= slot <= general[pt.PARTY_COUNT]:
        sys.exit(f"Platinum party slot {slot} is empty.")
    o = pt.PARTY_START + (slot - 1) * pt.PARTY_PK_SIZE
    pk4, ok = pt.decrypt_pokemon(general[o:o + pt.PARTY_PK_SIZE])
    if not ok:
        sys.exit("That Platinum Pokemon fails its checksum; refusing to copy it.")

    with open(sw_in, "rb") as f:
        raw = f.read()
    if not swish_crypto.hash_valid(raw):
        sys.exit("Sword/Shield save hash is already invalid; refusing to edit it.")
    blocks = swish_crypto.decrypt(raw)
    by_key = {b.key: b for b in blocks}
    party = by_key[sw.KEY_PARTY].data
    count = party[sw.PARTY_COUNT]
    if count >= 6:
        sys.exit("Sword/Shield party is full.")

    pk8, plain, notes = convert_pk4_to_pk8(pk4, by_key[sw.KEY_MY_STATUS].data)
    party[count * sw.PK_SIZE:(count + 1) * sw.PK_SIZE] = pk8
    party[sw.PARTY_COUNT] = count + 1
    notes.insert(0, mark_dex(by_key[KEY_ZUKAN_GALAR].data, plain))

    with open(sw_out, "wb") as f:
        f.write(swish_crypto.encrypt(blocks))

    print(f"Copied {SPECIES[pt.u16(pk4, 0x08)]} to Sword/Shield party slot {count + 1} -> {sw_out}")
    for note in notes:
        print(f"  note: {note}")


if __name__ == "__main__":
    main()
