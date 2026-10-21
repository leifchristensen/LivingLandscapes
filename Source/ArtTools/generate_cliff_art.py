"""
Generates Living Landscapes' cliff textures (our own art, styled after RimWorld's natural rock walls: mottled stone with a
heavy dark outline and rounded corners).

A cliff cell is a higher cell beside lower ground. Which of its 8 neighbours are lower decides its look (autotiling):
the higher ground's outline is the union of the higher cells, rounded by blurring and given a wobble that is periodic
over one cell, so the outlines of neighbouring cells meet seamlessly. Along every drop the cell shows the rim of a rock
wall: a dark line of varying width at the foot, then mottled, fissured rock up to an irregular lit lip, beyond which the
ground above shows through in the middle of the cell. The rim reads as a rock obstacle at a glance, like the edge of a
mountain; the open middle says it is not solid rock. Rounded-off corners show the lower ground with a shadow.

  Cliff_<mask>   one texture per neighbour pattern (all 256: the rounding blur sees diagonal neighbours too),
                 greyscale, tinted in game with the rock
  Shadow_South/East/West  soft shadows cast onto the lower neighbouring cells (drawn there)

Mask bits (lower neighbours): 1 N, 2 E, 4 S, 8 W, 16 NE, 32 SE, 64 SW, 128 NW (as in Building_Cliff).

Run: python Source/ArtTools/generate_cliff_art.py   (writes Textures/Things/Building/LivingLandscapes/Cliff/)
"""
import math
import multiprocessing
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
import png_min  # noqa: E402

C = 128                      # pixels per cell
W = 3 * C                    # working area: the cell and its 8 neighbours
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Textures', 'Things', 'Building', 'LivingLandscapes', 'Cliff')

BLUR = 20         # box-blur radius (applied twice) that rounds the outlines
WOBBLE = 0.11     # broad wobble of the outline (threshold amplitude)
JAG = 0.14        # fine, rocky irregularity of the outline (threshold amplitude)
OUTLINE_PX = (2.5, 5.5)  # the dark line at the foot of the drop varies in width along it
SHADE_PX = 9      # then the rock darkens towards the foot over this width
BAND_PX = 50      # rock rim width, to an irregular lip on the plateau side
LIP_JAG_PX = 13   # how far the lip wanders in and out
LIP_PX = 4        # light lip (the top edge of the rock) just inside it
OUTLINE = 0.2     # outline brightness: dark, not black
ROCK = 0.64       # mean rock brightness (tinted by the rock's colour in game)
CRACK = 0.035     # fissure width in noise units

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


def value_noise(seed, octaves=((32, 1.0), (16, 0.6), (8, 0.35), (4, 0.2))):
    """Mottled stone: value noise on lattices that divide the cell (so it tiles), about [-1, 1]."""
    rng = random.Random(seed)
    table = [[0.0] * C for _ in range(C)]
    norm = sum(a for _, a in octaves)
    for step, amp in octaves:
        k = C // step
        lattice = [[rng.uniform(-1, 1) for _ in range(k)] for _ in range(k)]
        for y in range(C):
            gy, fy = divmod(y, step)
            ty = fy / step
            ty = ty * ty * (3 - 2 * ty)
            for x in range(C):
                gx, fx = divmod(x, step)
                tx = fx / step
                tx = tx * tx * (3 - 2 * tx)
                a, b = lattice[gy][gx], lattice[gy][(gx + 1) % k]
                c, d = lattice[(gy + 1) % k][gx], lattice[(gy + 1) % k][(gx + 1) % k]
                top, bottom = a + (b - a) * tx, c + (d - c) * tx
                table[y][x] += amp * (top + (bottom - top) * ty) / norm
    return lambda x, y: table[y % C][x % C]


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


def distance_inside(inside):
    """Chamfer (3-4) distance, in pixels, from each inside pixel to the nearest outside pixel."""
    n = len(inside)
    big = 1e9
    d = [[big if inside[y][x] else 0.0 for x in range(n)] for y in range(n)]
    for y in range(n):
        row, up = d[y], d[y - 1] if y > 0 else None
        for x in range(n):
            v = row[x]
            if v == 0.0:
                continue
            if x > 0:
                v = min(v, row[x - 1] + 1)
            if up:
                v = min(v, up[x] + 1)
                if x > 0:
                    v = min(v, up[x - 1] + 1.4142)
                if x < n - 1:
                    v = min(v, up[x + 1] + 1.4142)
            row[x] = v
    for y in range(n - 1, -1, -1):
        row, down = d[y], d[y + 1] if y < n - 1 else None
        for x in range(n - 1, -1, -1):
            v = row[x]
            if v == 0.0:
                continue
            if x < n - 1:
                v = min(v, row[x + 1] + 1)
            if down:
                v = min(v, down[x] + 1)
                if x < n - 1:
                    v = min(v, down[x + 1] + 1.4142)
                if x > 0:
                    v = min(v, down[x - 1] + 1.4142)
            row[x] = v
    return d


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


NOISE = periodic_2d(1)
STONE = value_noise(2)
JAG_NOISE = value_noise(3, ((8, 1.0), (4, 0.5)))
LIP_NOISE = value_noise(4, ((32, 1.0), (16, 0.7), (8, 0.4)))
WIDTH_NOISE = value_noise(5, ((32, 1.0), (16, 0.5)))
CRACKS = value_noise(6, ((32, 1.0), (16, 0.5)))


def threshold(x, y):
    return 0.5 + WOBBLE * NOISE(x, y) + JAG * JAG_NOISE(x, y)


def render(mask):
    high = high_grid(mask)
    occ = box_blur(box_blur([[1.0 if high[y // C][x // C] else 0.0 for x in range(W)] for y in range(W)], BLUR), BLUR)
    inside = [[occ[y][x] >= threshold(x, y) for x in range(W)] for y in range(W)]
    dist = distance_inside(inside)

    img = []
    for y in range(C):
        row = []
        for x in range(C):
            gx, gy = x + C, y + C
            o, th = occ[gy][gx], threshold(x, y)
            if not inside[gy][gx]:
                # Lower ground inside this cell (a rounded-off corner): ground with a soft shadow.
                row.append((0, 0, 0, int(round(255 * 0.42 * smoothstep(th - 0.35, th, o)))))
                continue
            d = dist[gy][gx]
            lip = BAND_PX + LIP_JAG_PX * LIP_NOISE(x, y)
            if d > lip + 1:
                row.append((0, 0, 0, 0))   # the plateau: the ground shows through
                continue
            stone = STONE(x, y)
            outline = OUTLINE_PX[0] + (OUTLINE_PX[1] - OUTLINE_PX[0]) * (0.5 + 0.5 * WIDTH_NOISE(x, y))
            # Rock: mottled, with fissures, shaded towards the foot, lit along the lip.
            v = ROCK + 0.2 * stone
            if abs(CRACKS(x, y)) < CRACK:
                v -= 0.16
            v -= 0.2 * (1 - smoothstep(outline, outline + SHADE_PX, d))
            v += 0.13 * smoothstep(lip - LIP_PX - 3, lip - 1, d)
            # The foot: a dark line blending into the rock, soft against the ground.
            v += (OUTLINE + 0.05 * stone - v) * (1 - smoothstep(outline - 1.5, outline + 1.5, d))
            a = smoothstep(0, 1.6, d) * (1 - smoothstep(lip - 0.5, lip + 1, d))
            row.append((int(round(min(1, max(0, v)) * 255)),) * 3 + (int(round(a * 255)),))
        img.append(row)
    return mask, img


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
    with multiprocessing.Pool() as pool:
        for mask, img in pool.imap_unordered(render, range(256)):
            png_min.write(os.path.join(OUT, f'Cliff_{mask:03d}.png'), img)
    png_min.write(os.path.join(OUT, 'Shadow_South.png'), shadow('South', 0.5, 56, 4))
    png_min.write(os.path.join(OUT, 'Shadow_East.png'), shadow('East', 0.32, 30, 5))
    png_min.write(os.path.join(OUT, 'Shadow_West.png'), shadow('West', 0.22, 24, 6))
    print('wrote 256 cliff shapes to', os.path.normpath(OUT))


if __name__ == '__main__':
    main()
