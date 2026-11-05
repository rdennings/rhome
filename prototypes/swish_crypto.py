"""Switch Pokemon save encryption ("SwishCrypto"), used by Sword/Shield and later.

Ported from PKHeX (github.com/kwsch/PKHeX, PKHeX.Core/Saves/Encryption/SwishCrypto), which is GPLv3;
the constants below are copied from it.

File layout: [blocks...][SHA-256 hash]. The block area is XORed with a repeating 0x7F-byte pad.
The hash is SHA-256(INTRO + encrypted block area + OUTRO).
Each block: u32 key, then a type byte and data, all XORed with an xorshift32 stream seeded by the key.
"""
import hashlib
import struct

INTRO_HASH = bytes.fromhex((
    "9ec99cd70ed33c44fb9303dceb39b42a1947e9634ba2334416bf82a2ba6355b6"
    "3d9df24b5f7b6ab2621dc21b68e5c8b53a059000e8a8103de2ecf00cb2ed4f6d"
))

OUTRO_HASH = bytes.fromhex((
    "d6c01c598bc8b8cb46e153fc828c757513e045df32693c75f059f8d9a25fb217"
    "e08052dbea8973997579afcb2e8007e6f126e0030ae66ff641bf7e59c2ae55fd"
))

STATIC_XORPAD = bytes.fromhex((
    "a092d10607db32a1ae01f5c51e844fe353ca37f4a7b04da018b7c297da5f532b"
    "75fa4816f8d48a6f6105f4e2fd04b5a30ffc4492cb32e61bb9b12e01b0565336"
    "d2d1503dde5b2e0e52fddf2f7bca6350a4675d2317c052e1a6307c2bb670365b"
    "2a276933f5637b363f269ba3ed7a5300a448b3509e14a052de7e102b1b776e"
))

HASH_SIZE = 0x20

TYPE_BOOL1, TYPE_BOOL2, TYPE_BOOL3, TYPE_OBJECT, TYPE_ARRAY = 1, 2, 3, 4, 5
TYPE_SIZES = {3: 1, 8: 1, 9: 2, 10: 4, 11: 8, 12: 1, 13: 2, 14: 4, 15: 8, 16: 4, 17: 8}


class XorShift32:
    def __init__(self, seed):
        state = seed
        for _ in range(bin(seed).count("1")):
            state = self._advance(state)
        self.state, self.counter = state, 0

    @staticmethod
    def _advance(s):
        s ^= (s << 2) & 0xFFFFFFFF
        s ^= s >> 15
        s ^= (s << 13) & 0xFFFFFFFF
        return s

    def next(self):
        result = (self.state >> (self.counter * 8)) & 0xFF
        if self.counter == 3:
            self.state, self.counter = self._advance(self.state), 0
        else:
            self.counter += 1
        return result

    def next32(self):
        return self.next() | (self.next() << 8) | (self.next() << 16) | (self.next() << 24)

    def xor(self, data):
        return bytes(b ^ self.next() for b in data)


class Block:
    def __init__(self, key, type_code, data=b"", subtype=0):
        self.key, self.type, self.data, self.subtype = key, type_code, bytearray(data), subtype


def crypt_static(data):
    pad, n = STATIC_XORPAD, len(STATIC_XORPAD)
    return bytes(b ^ pad[i % n] for i, b in enumerate(data))


def hash_valid(raw):
    payload, stored = raw[:-HASH_SIZE], raw[-HASH_SIZE:]
    return hashlib.sha256(INTRO_HASH + payload + OUTRO_HASH).digest() == stored


def decrypt(raw):
    """Encrypted save bytes -> list of Blocks (in file order)."""
    data = crypt_static(raw[:-HASH_SIZE])
    blocks, o = [], 0
    while o < len(data):
        key = struct.unpack_from("<I", data, o)[0]
        o += 4
        xk = XorShift32(key)
        t = data[o] ^ xk.next()
        o += 1
        if t in (TYPE_BOOL1, TYPE_BOOL2, TYPE_BOOL3):
            blocks.append(Block(key, t))
        elif t == TYPE_OBJECT:
            size = struct.unpack_from("<I", data, o)[0] ^ xk.next32()
            o += 4
            blocks.append(Block(key, t, xk.xor(data[o:o + size])))
            o += size
        elif t == TYPE_ARRAY:
            count = struct.unpack_from("<I", data, o)[0] ^ xk.next32()
            sub = data[o + 4] ^ xk.next()
            o += 5
            size = count * TYPE_SIZES[sub]
            blocks.append(Block(key, t, xk.xor(data[o:o + size]), sub))
            o += size
        else:
            size = TYPE_SIZES[t]
            blocks.append(Block(key, t, xk.xor(data[o:o + size])))
            o += size
    return blocks


def encrypt(blocks):
    """List of Blocks -> encrypted save bytes, with a fresh hash."""
    out = bytearray()
    for b in blocks:
        xk = XorShift32(b.key)
        out += struct.pack("<I", b.key)
        out.append(b.type ^ xk.next())
        if b.type == TYPE_OBJECT:
            out += struct.pack("<I", len(b.data) ^ xk.next32())
        elif b.type == TYPE_ARRAY:
            out += struct.pack("<I", (len(b.data) // TYPE_SIZES[b.subtype]) ^ xk.next32())
            out.append(b.subtype ^ xk.next())
        out += xk.xor(b.data)
    payload = crypt_static(bytes(out))
    return payload + hashlib.sha256(INTRO_HASH + payload + OUTRO_HASH).digest()
