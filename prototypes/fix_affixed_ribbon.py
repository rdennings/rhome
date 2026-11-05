#!/usr/bin/env python3
"""Repair a Pokemon written by an early transfer_pt_to_swsh/sv run that shows a Kalos Champion Ribbon it doesn't have.

Usage:
    python3 fix_affixed_ribbon.py SAVE_IN_main SAVE_OUT_main ENCRYPTION_CONSTANT_HEX

Those runs left the "affixed ribbon" byte at 0 (= ribbon #0, Kalos Champion) instead of -1 (= none).
Finds the Pokemon by encryption constant anywhere in the party or boxes, so it works even after
the player has moved it, sets that byte to none, and fixes its checksum. Works on Sword/Shield and Scarlet/Violet.
"""
import struct
import sys

import read_sv_save as sv
import read_swsh_save as sw
import swish_crypto
from read_pt_save import u16, u32
from transfer_pt_to_swsh import encrypt_pk8

AFFIXED_RIBBON = {"Sword/Shield": 0xE8, "Scarlet/Violet": 0xD4}
NONE = 0xFF


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    src, dst, ec = sys.argv[1], sys.argv[2], int(sys.argv[3], 16)
    with open(src, "rb") as f:
        raw = f.read()
    if not swish_crypto.hash_valid(raw):
        sys.exit("Save hash is invalid; refusing to edit it.")
    block_list = swish_crypto.decrypt(raw)
    blocks = {b.key: b for b in block_list}
    if sv.KEY_PARTY in blocks:
        game, party_key = "Scarlet/Violet", sv.KEY_PARTY
    elif sw.KEY_PARTY in blocks:
        game, party_key = "Sword/Shield", sw.KEY_PARTY
    else:
        sys.exit("Not a Sword/Shield or Scarlet/Violet save.")
    offset = AFFIXED_RIBBON[game]

    fixed = []
    for label, data, slots in (("party", blocks[party_key].data, 6), ("box", blocks[sv.KEY_BOX].data, 32 * 30)):
        for i in range(slots):
            o = i * sw.PK_SIZE
            raw_pk = data[o:o + sw.PK_SIZE]
            if u32(raw_pk, 0) != ec:
                continue
            pk, ok = sw.decrypt_pokemon(raw_pk)
            if not ok:
                sys.exit(f"Found it in {label} slot {i + 1}, but its checksum is already bad; not touching it.")
            if pk[offset] == NONE:
                fixed.append(f"{label} slot {i + 1} (already fine)")
                continue
            pk = bytearray(pk)
            pk[offset] = NONE
            struct.pack_into("<H", pk, 0x06, sum(struct.unpack_from("<160H", pk, 0x08)) & 0xFFFF)
            data[o:o + sw.PK_SIZE] = encrypt_pk8(bytes(pk))
            fixed.append(f"{label} slot {i + 1}" if label == "party" else f"box {i // 30 + 1} slot {i % 30 + 1}")
    if not fixed:
        sys.exit(f"No Pokemon with encryption constant {ec:08X} found.")

    with open(dst, "wb") as f:
        f.write(swish_crypto.encrypt(block_list))
    print(f"{game}: set affixed ribbon to none for {ec:08X} in: {', '.join(fixed)} -> {dst}")


if __name__ == "__main__":
    main()
