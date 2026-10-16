"""Minimal PNG read/write (8-bit RGBA/RGB/grey+alpha, non-interlaced) without third-party libraries."""
import struct
import zlib


def read(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n'
    pos, idat, w = 8, b'', 0
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        if kind == b'IHDR':
            w, h, depth, ctype, _, _, interlace = struct.unpack('>IIBBBBB', chunk)
            assert depth == 8 and interlace == 0, (depth, interlace)
        elif kind == b'IDAT':
            idat += chunk
        pos += 12 + length
    bpp = {6: 4, 2: 3, 4: 2, 0: 1}[ctype]
    raw = zlib.decompress(idat)
    stride = w * bpp
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(h):
        f = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    px = []
    for line in rows:
        row = []
        for x in range(w):
            v = line[x * bpp:(x + 1) * bpp]
            if bpp == 4:
                row.append(tuple(v))
            elif bpp == 3:
                row.append((v[0], v[1], v[2], 255))
            elif bpp == 2:
                row.append((v[0], v[0], v[0], v[1]))
            else:
                row.append((v[0], v[0], v[0], 255))
        px.append(row)
    return px


def write(path, px):
    h, w = len(px), len(px[0])
    raw = b''.join(b'\x00' + bytes(c for p in row for c in p) for row in px)

    def chunk(kind, body):
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xffffffff)

    out = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    out += chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    open(path, 'wb').write(out)


def scale(px, k):
    return [[p for p in row for _ in range(k)] for row in px for _ in range(k)]


def crop(px, x, y, w, h):
    return [row[x:x + w] for row in px[y:y + h]]
