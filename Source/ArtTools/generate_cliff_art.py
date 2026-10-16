"""
Generates Living Landscapes' cliff edge textures (our own art, RimWorld style: soft shading, dark outlines).

A cliff is drawn as thin edges on the sides of a higher cell that drop to lower ground, not as a full-cell block:
  Edge_South  a short face of stacked stones along the bottom of the cell (the drop faces the viewer)
  Edge_North  a thin lip of stones along the top (the drop faces away; only the rim shows)
  Edge_East / Edge_West  a thin column of stones along that side
  Stairs_*    the same edges with steps cut in, for the passable stair pieces
Textures are greyscale and tinted in game with the local rock's colour. Each is one cell (64 px), transparent outside
the edge, and tiles seamlessly with its neighbours along the edge.

Run: python Source/ArtTools/generate_cliff_art.py   (writes Textures/Things/Building/LivingLandscapes/Cliff/)
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
import png_min  # noqa: E402

SIZE = 64
OUTLINE = 0.16      # dark outline value
GAP = 0.24          # mortar/shadow between stones
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Textures', 'Things', 'Building', 'LivingLandscapes', 'Cliff')


def blank():
    return [[None] * SIZE for _ in range(SIZE)]


def put(img, x, y, v, a=1.0):
    if 0 <= x < SIZE and 0 <= y < SIZE:
        img[y][x] = (v, a)


def stone(img, cx, cy, rx, ry, base, rng, wrap_x=False, wrap_y=False):
    """A rounded stone: outlined ellipse, lit from the top left, darker at the bottom."""
    for dy in range(-ry - 1, ry + 2):
        for dx in range(-rx - 1, rx + 2):
            nx, ny = dx / (rx + 0.5), dy / (ry + 0.5)
            d = nx * nx + ny * ny
            if d > 1.0:
                continue
            x, y = cx + dx, cy + dy
            if wrap_x:
                x %= SIZE
            if wrap_y:
                y %= SIZE
            edge = d > 0.62
            if edge:
                put(img, x, y, OUTLINE)
                continue
            light = base + 0.18 * (-nx * 0.5 - ny) * 0.6 - 0.12 * max(0.0, ny)
            light += rng.uniform(-0.03, 0.03)
            put(img, x, y, max(0.05, min(0.95, light)))


def fill_band(img, x0, y0, x1, y1, v):
    for y in range(y0, y1):
        for x in range(x0, x1):
            put(img, x, y, v)


def outline_row(img, y, v=OUTLINE):
    for x in range(SIZE):
        put(img, x, y, v)


def outline_col(img, x, v=OUTLINE):
    for y in range(SIZE):
        put(img, x, y, v)


def edge_south(rng, stairs=False):
    """Face along the bottom: lip, two staggered rows of stones, dark foot."""
    img = blank()
    top = SIZE - 22
    fill_band(img, 0, top, SIZE, SIZE - 1, GAP)
    # Lip: small flat stones along the top of the face.
    x = rng.randint(0, 4)
    while x < SIZE + 6:
        w = rng.randint(3, 5)
        stone(img, x, top + 2, w, 2, 0.78, rng, wrap_x=True)
        x += 2 * w + rng.randint(0, 2)
    if stairs:
        # Three treads across the face, light tops and dark risers.
        for i, y in enumerate((top + 6, top + 11, top + 16)):
            for xx in range(10, SIZE - 10):
                put(img, xx, y, 0.72 - 0.06 * i)
                put(img, xx, y + 1, 0.62 - 0.06 * i)
                put(img, xx, y + 3, OUTLINE)
        for y in range(top + 5, SIZE - 1):
            put(img, 9, y, OUTLINE)
            put(img, SIZE - 10, y, OUTLINE)
    else:
        for row, (cy, ry) in enumerate(((top + 8, 4), (top + 15, 4))):
            x = rng.randint(0, 6) + row * 5
            while x < SIZE + 8:
                w = rng.randint(4, 7)
                stone(img, x, cy, w, ry, 0.6 - 0.07 * row, rng, wrap_x=True)
                x += 2 * w + rng.randint(1, 2)
    outline_row(img, top - 1)
    outline_row(img, SIZE - 1)
    return img


def edge_north(rng, stairs=False):
    """Only the rim shows: a thin row of flat stones along the top."""
    img = blank()
    fill_band(img, 0, 0, SIZE, 7, GAP)
    x = rng.randint(0, 4)
    while x < SIZE + 6:
        w = rng.randint(3, 6)
        stone(img, x, 3, w, 2, 0.74, rng, wrap_x=True)
        x += 2 * w + rng.randint(0, 2)
    outline_row(img, 7)
    if stairs:
        for xx in range(14, SIZE - 14):
            for y in range(0, 8):
                put(img, xx, y, 0.66 if y % 3 else OUTLINE)
    return img


def edge_east(rng, stairs=False):
    """A thin column of stones along the right side."""
    img = blank()
    x0 = SIZE - 9
    fill_band(img, x0, 0, SIZE, SIZE, GAP)
    y = rng.randint(0, 4)
    while y < SIZE + 6:
        h = rng.randint(3, 6)
        stone(img, SIZE - 5, y, 3, h, 0.68, rng, wrap_y=True)
        y += 2 * h + rng.randint(0, 2)
    outline_col(img, x0 - 1)
    if stairs:
        for y in range(14, SIZE - 14):
            for x in range(x0, SIZE):
                put(img, x, y, 0.66 if y % 4 else OUTLINE)
    return img


def mirror_x(img):
    return [list(reversed(row)) for row in img]


def to_rgba(img):
    px = []
    for row in img:
        out = []
        for p in row:
            if p is None:
                out.append((0, 0, 0, 0))
            else:
                v = int(round(p[0] * 255))
                out.append((v, v, v, int(round(p[1] * 255))))
        px.append(out)
    return px


def main():
    os.makedirs(OUT, exist_ok=True)
    pieces = {
        'Edge_South': edge_south(random.Random(1)),
        'Edge_North': edge_north(random.Random(2)),
        'Edge_East': edge_east(random.Random(3)),
        'Edge_West': mirror_x(edge_east(random.Random(4))),
        'Stairs_South': edge_south(random.Random(5), stairs=True),
        'Stairs_North': edge_north(random.Random(6), stairs=True),
        'Stairs_East': edge_east(random.Random(7), stairs=True),
        'Stairs_West': mirror_x(edge_east(random.Random(8), stairs=True)),
    }
    for name, img in pieces.items():
        png_min.write(os.path.join(OUT, name + '.png'), to_rgba(img))
    print('wrote', len(pieces), 'textures to', os.path.normpath(OUT))


if __name__ == '__main__':
    main()
