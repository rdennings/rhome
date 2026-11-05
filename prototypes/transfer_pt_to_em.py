#!/usr/bin/env python3
"""Copy one Pokemon from a Platinum save into the first empty slot of an Emerald box.

Usage:
    python3 transfer_pt_to_em.py PLATINUM.sav party|box N EMERALD_IN.srm EMERALD_OUT.srm [EMERALD_BOX]

  party N  -> party slot N (1-6)
  box N    -> not supported yet
  EMERALD_BOX defaults to 1. The Platinum save is never modified; EMERALD_OUT is a new file.

Test-quality: data Emerald can't hold (Gen 4 moves, held items, Sinnoh met
location, ribbons) is dropped and reported, not kept anywhere.
"""
import struct
import sys

import read_em_save as em
import read_pt_save as pt
from gen3_data import HOENN_INDEX_TO_NATIONAL
from gen4_data import MOVES, SPECIES

GEN3_MAX_SPECIES = 386
GEN3_MAX_MOVE = 354
GEN3_MAX_BALL = 12          # Master..Premier share IDs in Gen 3 and 4
POKE_BALL = 4
GAME_EMERALD = 3
METLOC_FATEFUL_ENCOUNTER = 0xFF

NATIONAL_TO_GEN3_INDEX = {nat: idx for idx, nat in HOENN_INDEX_TO_NATIONAL.items()}
GEN3_ENCODE = {ch: code for code, ch in em.CHARMAP.items()}


def encode_gen3(text, size):
    out = bytearray(b"\xFF" * size)
    for i, ch in enumerate(text[:size]):
        out[i] = GEN3_ENCODE.get(ch, GEN3_ENCODE["?"])
    return bytes(out)


def gen3_species_index(national):
    return national if national <= 251 else NATIONAL_TO_GEN3_INDEX[national]


def convert_pk4_to_pk3(pk4):
    """Decrypted Gen 4 Pokemon -> encrypted 80-byte Gen 3 box Pokemon. Returns (bytes, notes)."""
    notes = []
    species = pt.u16(pk4, 0x08)
    if species > GEN3_MAX_SPECIES:
        sys.exit(f"{SPECIES[species]} doesn't exist in Gen 3; can't move it to Emerald.")
    pid = pt.u32(pk4, 0x00)
    ivs = pt.u32(pk4, 0x38)
    if ivs & (1 << 30):
        sys.exit("Eggs aren't supported by this test.")

    item = pt.u16(pk4, 0x0A)
    if item:
        notes.append(f"held item #{item} dropped (Gen 4 item IDs differ from Gen 3)")

    moves, pps, pp_bonuses = [], [], 0
    for i in range(4):
        move = pt.u16(pk4, 0x28 + i * 2)
        if not move:
            continue
        if move > GEN3_MAX_MOVE:
            notes.append(f"move {MOVES[move]} dropped (not in Gen 3)")
            continue
        pp_bonuses |= (pk4[0x34 + i] & 3) << (len(moves) * 2)
        moves.append(move)
        pps.append(pk4[0x30 + i])
    if not moves:
        sys.exit("No Gen 3 moves left after dropping Gen 4 moves; not supported.")
    moves += [0] * (4 - len(moves))
    pps += [0] * (4 - len(pps))

    ball = pk4[0x83]
    if ball > GEN3_MAX_BALL:
        notes.append(f"ball #{ball} became a Poké Ball (not in Gen 3)")
        ball = POKE_BALL
    met_level = pk4[0x84] & 0x7F
    ot_female = pk4[0x84] >> 7
    origins = met_level | (GAME_EMERALD << 7) | (ball << 11) | (ot_female << 15)
    notes.append("met location set to 'fateful encounter' and origin game to Emerald "
                 "(Gen 3 can't express Sinnoh/Platinum)")
    if any(pk4[0x24:0x28]) or any(pk4[0x3C:0x40]) or any(pk4[0x60:0x68]):
        notes.append("ribbons dropped")

    growth = struct.pack("<HHIBBH", gen3_species_index(species), 0, pt.u32(pk4, 0x10),
                         pp_bonuses, pk4[0x14], 0)
    attacks = struct.pack("<4H4B", *moves, *pps)
    evs = bytes(pk4[0x18:0x24])                     # 6 EVs + 6 contest stats, same order
    ability_slot = pid & 1                          # Gen 4 also picks the ability slot from PID bit 0
    iv_word = (ivs & 0x3FFFFFFF) | (ability_slot << 31)
    misc = struct.pack("<BBHII", pk4[0x82], METLOC_FATEFUL_ENCOUNTER, origins, iv_word, 0)

    order = em.SUB_ORDERS[pid % 24]
    subs = {"G": growth, "A": attacks, "E": evs, "M": misc}
    plain = growth + attacks + evs + misc
    checksum = sum(struct.unpack("<24H", plain)) & 0xFFFF
    shuffled = b"".join(subs[letter] for letter in order)
    otid = pt.u16(pk4, 0x0C) | (pt.u16(pk4, 0x0E) << 16)
    key = pid ^ otid
    encrypted = b"".join(struct.pack("<I", em.u32(shuffled, o) ^ key) for o in range(0, 48, 4))

    nickname = pt.decode_string(pk4, 0x48, 11)
    ot_name = pt.decode_string(pk4, 0x68, 8)
    header = struct.pack("<II", pid, otid) + encode_gen3(nickname, 10)
    header += bytes([pk4[0x17], 0x02])              # language, flags: hasSpecies
    header += encode_gen3(ot_name, 7) + bytes([pk4[0x16] & 0x0F])
    header += struct.pack("<HH", checksum, 0)
    return header + encrypted, notes


def open_newest_slot(save):
    """Editable copies of the newest slot's sections, plus each section's physical position."""
    slots = [em.read_slot(save, s) for s in (0, 1)]
    newest = max((s for s in (0, 1) if len(slots[s][1]) == em.SECTION_COUNT and not slots[s][2]),
                 key=lambda s: slots[s][0])
    positions = {}
    for i in range(em.SECTION_COUNT):
        base = newest * em.SLOT_SIZE + i * em.SECTION_SIZE
        positions[em.u16(save, base + 0xFF4)] = base
    sections = {sid: bytearray(body) for sid, body in slots[newest][1].items()}
    return sections, positions


def commit_sections(save, sections, positions):
    """Write sections back in place with fresh checksums (the save index stays the same)."""
    for sid, body in sections.items():
        base = positions[sid]
        save[base:base + len(body)] = body
        struct.pack_into("<H", save, base + 0xFF6, em.section_checksum(bytes(body)))


def write_box_pokemon(sections, pk3, box):
    """Put pk3 in the first empty slot of `box` (1-based). Returns slot number."""
    storage = bytearray(b"".join(sections[i] for i in range(5, 14)))
    for slot in range(em.BOX_SLOTS):
        o = em.BOX_START + ((box - 1) * em.BOX_SLOTS + slot) * em.PK_SIZE
        if not any(storage[o:o + em.PK_SIZE]):
            storage[o:o + em.PK_SIZE] = pk3
            break
    else:
        sys.exit(f"Box {box} is full.")

    offset = 0
    for sid in range(5, 14):
        size = em.SECTION_DATA_SIZE[sid]
        sections[sid][:] = storage[offset:offset + size]
        offset += size
    return slot + 1


def mark_dex_caught(sections, national, pid):
    """Set seen + owned for a species in every copy, like the game does on catch or trade."""
    byte, bit = (national - 1) >> 3, 1 << ((national - 1) & 7)
    already_seen = sections[0][em.DEX_SEEN + byte] & bit
    for s, o in em.dex_flag_regions("seen") + em.dex_flag_regions("owned"):
        sections[s][o + byte] |= bit
    # The dex remembers the first Unown/Spinda seen, to draw its form/spots.
    if not already_seen and national in (201, 327):
        o = em.DEX_UNOWN_PID if national == 201 else em.DEX_SPINDA_PID
        struct.pack_into("<I", sections[0], o, pid)


def main():
    if len(sys.argv) not in (6, 7) or sys.argv[2] != "party":
        sys.exit(__doc__)
    pt_path, _, n, em_in, em_out = sys.argv[1:6]
    box = int(sys.argv[6]) if len(sys.argv) == 7 else 1

    general, _ = pt.pick_block(pt.load_save(pt_path), 0, pt.GENERAL_SIZE, "general")
    n = int(n)
    if not 1 <= n <= general[pt.PARTY_COUNT]:
        sys.exit(f"Party slot {n} is empty.")
    o = pt.PARTY_START + (n - 1) * pt.PARTY_PK_SIZE
    pk4, ok = pt.decrypt_pokemon(general[o:o + pt.PARTY_PK_SIZE])
    if not ok:
        sys.exit("That Platinum Pokemon fails its checksum; refusing to copy it.")

    pk3, notes = convert_pk4_to_pk3(pk4)
    with open(em_in, "rb") as f:
        raw = f.read()
    save = bytearray(raw[:em.SAVE_SIZE])
    sections, positions = open_newest_slot(save)
    slot = write_box_pokemon(sections, pk3, box)
    mark_dex_caught(sections, pt.u16(pk4, 0x08), pt.u32(pk4, 0x00))
    commit_sections(save, sections, positions)
    with open(em_out, "wb") as f:
        f.write(bytes(save) + raw[em.SAVE_SIZE:])

    print(f"Copied {SPECIES[pt.u16(pk4, 0x08)]} to Emerald box {box}, slot {slot} -> {em_out}")
    print("  Pokédex: marked as seen and owned")
    for note in notes:
        print(f"  note: {note}")


if __name__ == "__main__":
    main()
