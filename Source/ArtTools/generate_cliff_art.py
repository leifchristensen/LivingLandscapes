"""
Generates Living Landscapes' cliff textures (our own art, RimWorld style: soft painted shading, dark outlines on the
silhouette, rounded organic shapes, no pixel-art grid).

A cliff cell is a higher cell beside lower ground. Which of its 8 neighbours are lower decides its look (autotiling):
the higher ground's outline is the union of the higher cells, rounded by blurring and given a wobble that is periodic
over one cell, so the outlines of neighbouring cells meet seamlessly. The drop is drawn as a short rock face where the
lower ground lies to the south (the face is in view), and as a thin rim on the other sides. The plateau side of the
rim fades into the ground; the foot of the drop has a dark outline; rounded-off corners show the ground with a shadow.

  Cliff_<mask>   one texture per neighbour pattern (all 256: the rounding blur sees diagonal neighbours too),
                 greyscale, tinted in game with the rock
  Stairs_<mask>  the same with steps worked into the south face (passable stair pieces)
  Shadow_South/East/West  soft shadows cast onto the lower neighbouring cells (drawn there)

Mask bits (lower neighbours): 1 N, 2 E, 4 S, 8 W, 16 NE, 32 SE, 64 SW, 128 NW (as in Building_Cliff).

Run: python Source/ArtTools/generate_cliff_art.py   (writes Textures/Things/Building/LivingLandscapes/Cliff/)
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
import png_min  # noqa: E402

C = 128                      # pixels per cell
W = 3 * C                    # working area: the cell and its 8 neighbours
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Textures', 'Things', 'Building', 'LivingLandscapes', 'Cliff')

FACE = 40       # px of rock face below the lip where the drop faces south
SIDE = 12       # px of rim where the drop faces east or west
NORTH = 7       # px of rim where the drop faces north
BLUR = 9        # box-blur radius (applied twice) that rounds the outlines
WOBBLE = 0.11   # threshold wobble amplitude
OUTLINE = 0.13

N, E, S, Wst, NE, SE, SW, NW = 1, 2, 4, 8, 16, 32, 64, 128
NEIGHBOURS = {N: (0, -1), E: (1, 0), S: (0, 1), Wst: (-1, 0), NE: (1, -1), SE: (1, 1), SW: (-1, 1), NW: (-1, -1)}


def periodic_2d(seed):
    """Smooth noise over one cell, periodic in x and y, about [-1, 1]."""
    rng = random.Random(seed)
    terms = [(kx, ky, a, rng.uniform(0, 2 * math.pi))
             for kx, ky, a in ((1, 0, 1), (0, 1, 1), (1, 1, 0.7), (2, 1, 0.5), (1, 2, 0.5), (3, 2, 0.3), (2, 3, 0.3), (4, 4, 0.2))]
    norm = sum(t[2] for t in terms)
    table = [[sum(a * math.sin(2 * math.pi * (kx * x + ky * y) / C + p) for kx, ky, a, p in terms) / norm
              for x in range(C)] for y in range(C)]
    return lambda x, y: table[y % C][x % C]


def fissures(seed):
    rng = random.Random(seed)
    terms = [(k, a, rng.uniform(0, 2 * math.pi)) for k, a in ((5, 1), (9, 0.7), (14, 0.5), (23, 0.3))]
    norm = sum(a for _, a, _ in terms)
    table = [sum(a * math.sin(2 * math.pi * k * x / C + p) for k, a, p in terms) / norm for x in range(C)]
    return lambda x: table[x % C]


def box_blur(grid, r):
    """Box blur via a summed-area table (clamped at the borders)."""
    n = len(grid)
    sat = [[0.0] * (n + 1) for _ in range(n + 1)]
    for y in range(n):
        row, acc = grid[y], 0.0
        for x in range(n):
            acc += row[x]
            sat[y + 1][x + 1] = sat[y][x + 1] + acc
    out = [[0.0] * n for _ in range(n)]
    for y in range(n):
        y0, y1 = max(0, y - r), min(n, y + r + 1)
        for x in range(n):
            x0, x1 = max(0, x - r), min(n, x + r + 1)
            s = sat[y1][x1] - sat[y0][x1] - sat[y1][x0] + sat[y0][x0]
            out[y][x] = s / ((y1 - y0) * (x1 - x0))
    return out


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def high_grid(mask):
    """3x3 cells: True where the ground is high (not a lower neighbour)."""
    high = [[True] * 3 for _ in range(3)]
    for bit, (dx, dy) in NEIGHBOURS.items():
        if mask & bit:
            high[1 + dy][1 + dx] = False
    return high


def render(mask, noise, noise2, crack, stairs=False):
    high = high_grid(mask)

    def H(x, y):
        if x < 0 or y < 0 or x >= W or y >= W:
            return high[min(2, max(0, y // C))][min(2, max(0, x // C))]
        return high[y // C][x // C]

    occ = [[1.0 if H(x, y) else 0.0 for x in range(W)] for y in range(W)]
    # Top surface: the high ground drawn back from every drop by the size of the face (south) or rim (others).
    top = [[1.0 if (H(x, y) and H(x, y + FACE) and H(x + SIDE, y) and H(x - SIDE, y) and H(x, y - NORTH)) else 0.0
            for x in range(W)] for y in range(W)]
    occ = box_blur(box_blur(occ, BLUR), BLUR)
    top = box_blur(box_blur(top, BLUR), BLUR)

    img = []
    for y in range(C):
        row = []
        for x in range(C):
            gx, gy = x + C, y + C
            th = 0.5 + WOBBLE * noise(x, y)
            tt = 0.5 + WOBBLE * noise2(x, y)
            o, t = occ[gy][gx], top[gy][gx]
            if t >= tt:
                # Plateau: transparent, with a light lip that fades into the ground.
                lip = 1 - smoothstep(tt, tt + 0.18, t)
                v, a = 0.80, 0.5 * lip
                if t - tt < 0.035:
                    v, a = OUTLINE + 0.1, 0.75
            elif o >= th:
                # The drop: rock face, lit at the lip and shaded towards the foot.
                r = (tt - t) / max(1e-4, (tt - t) + (o - th))
                v = 0.70 - 0.36 * r + 0.07 * crack(x) * (0.5 + r)
                if stairs and C * 0.18 < x < C * 0.82 and not (mask & (E | Wst) and False):
                    band = int(r * 3.999)
                    frac = r * 4 - band
                    v = (0.74 - 0.08 * band) if frac < 0.55 else (0.40 - 0.05 * band)
                    if abs(x - C * 0.18) < 3 or abs(x - C * 0.82) < 3:
                        v = OUTLINE
                a = 1.0
                if o - th < 0.045:
                    v = OUTLINE           # outline at the foot of the drop
                elif tt - t < 0.03:
                    v = OUTLINE + 0.08    # crisp line at the lip
            else:
                # Lower ground inside this cell (a rounded-off corner): ground with a soft shadow.
                v, a = 0.0, 0.42 * smoothstep(th - 0.35, th, o)
            row.append((int(round(v * 255)),) * 3 + (int(round(a * 255)),))
        img.append(row)
    return img


def shadow(direction, strength, reach_px, seed):
    rng = random.Random(seed)
    phase = [rng.uniform(0, 2 * math.pi) for _ in range(3)]

    def wob(t):
        return (math.sin(2 * math.pi * t / C + phase[0]) + 0.5 * math.sin(4 * math.pi * t / C + phase[1])) / 1.5

    img = []
    for y in range(C):
        row = []
        for x in range(C):
            along, depth = (x, y) if direction == 'South' else (y, x if direction == 'East' else C - 1 - x)
            reach = reach_px * (1 + 0.15 * wob(along))
            a = strength * max(0.0, 1 - depth / reach) ** 1.7
            row.append((0, 0, 0, int(round(a * 255))))
        img.append(row)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    for name in os.listdir(OUT):
        if name.endswith('.png'):
            os.remove(os.path.join(OUT, name))
    noise, noise2, crack = periodic_2d(1), periodic_2d(2), fissures(3)
    masks = range(256)
    for m in masks:
        png_min.write(os.path.join(OUT, f'Cliff_{m:03d}.png'), render(m, noise, noise2, crack))
        if m & S:
            png_min.write(os.path.join(OUT, f'Stairs_{m:03d}.png'), render(m, noise, noise2, crack, stairs=True))
    png_min.write(os.path.join(OUT, 'Shadow_South.png'), shadow('South', 0.5, 56, 4))
    png_min.write(os.path.join(OUT, 'Shadow_East.png'), shadow('East', 0.32, 30, 5))
    png_min.write(os.path.join(OUT, 'Shadow_West.png'), shadow('West', 0.22, 24, 6))
    print('wrote', len(masks), 'cliff shapes to', os.path.normpath(OUT))


if __name__ == '__main__':
    main()
