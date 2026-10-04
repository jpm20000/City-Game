#!/usr/bin/env python3
"""Draws the game icon (an isometric cottage on a tile) with the standard library only (no PIL / numpy).

Writes Assets/_Game/Art/Icon/icon_<size>.png for 256, 128, 64, 48, 32 and 16 px (M19g). Edit the shapes below and
re-run; commit the PNGs and Unity's rewritten .meta files. The picture is drawn at 512 px and box-filtered down.
"""
import os
import struct
import zlib

SIZE = 512
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Game", "Art", "Icon")

BG = (27, 36, 54)
BG_RIM = (52, 70, 104)
GRASS = (112, 142, 62)
GRASS_EDGE = (82, 108, 44)
WALL_L = (214, 188, 146)
WALL_R = (172, 146, 108)
ROOF_L = (186, 76, 54)
ROOF_R = (142, 52, 40)
DOOR = (84, 56, 38)
WINDOW = (250, 214, 110)
CHIMNEY = (120, 100, 92)
SHADOW = (60, 80, 40)


def new_canvas():
    return [[(0, 0, 0, 0)] * SIZE for _ in range(SIZE)]


def fill_polygon(canvas, pts, color):
    """Scanline fill of a convex or concave polygon (even-odd)."""
    ys = [p[1] for p in pts]
    for y in range(max(0, int(min(ys))), min(SIZE - 1, int(max(ys))) + 1):
        xs = []
        n = len(pts)
        for i in range(n):
            (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
            if (y0 <= y + 0.5 < y1) or (y1 <= y + 0.5 < y0):
                xs.append(x0 + (y + 0.5 - y0) * (x1 - x0) / (y1 - y0))
        xs.sort()
        for i in range(0, len(xs) - 1, 2):
            for x in range(max(0, int(round(xs[i]))), min(SIZE, int(round(xs[i + 1])))):
                canvas[y][x] = color + (255,)


def rounded_square(canvas, margin, radius, color):
    for y in range(SIZE):
        for x in range(SIZE):
            left, right = margin, SIZE - 1 - margin
            cx = min(max(x, left + radius), right - radius)
            cy = min(max(y, left + radius), right - radius)
            if left <= x <= right and left <= y <= right and (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2:
                canvas[y][x] = color + (255,)


def draw():
    c = new_canvas()
    rounded_square(c, 16, 96, BG_RIM)
    rounded_square(c, 28, 86, BG)
    # the ground tile, with a darker rim, and a soft shadow under the house
    fill_polygon(c, [(256, 238), (448, 338), (256, 438), (64, 338)], GRASS_EDGE)
    fill_polygon(c, [(256, 252), (426, 338), (256, 424), (86, 338)], GRASS)
    fill_polygon(c, [(256, 322), (350, 372), (256, 410), (170, 372)], SHADOW)
    # walls: left face lit, right face in shade
    fill_polygon(c, [(176, 322), (256, 362), (256, 268), (176, 228)], WALL_L)
    fill_polygon(c, [(256, 362), (336, 322), (336, 228), (256, 268)], WALL_R)
    # door and window
    fill_polygon(c, [(204, 336), (226, 347), (226, 304), (204, 293)], DOOR)
    fill_polygon(c, [(280, 313), (312, 297), (312, 270), (280, 286)], WINDOW)
    # chimney behind the roof
    fill_polygon(c, [(286, 196), (312, 208), (312, 160), (286, 148)], CHIMNEY)
    # hip roof
    fill_polygon(c, [(158, 232), (256, 282), (256, 168)], ROOF_L)
    fill_polygon(c, [(256, 282), (354, 232), (256, 168)], ROOF_R)
    return c


def downsample(canvas, size):
    k = SIZE // size
    rows = []
    for y in range(size):
        row = []
        for x in range(size):
            r = g = b = a = 0
            for dy in range(k):
                for dx in range(k):
                    pr, pg, pb, pa = canvas[y * k + dy][x * k + dx]
                    r += pr * pa
                    g += pg * pa
                    b += pb * pa
                    a += pa
            n = k * k
            if a == 0:
                row.append((0, 0, 0, 0))
            else:
                row.append((round(r / a), round(g / a), round(b / a), round(a / n)))
        rows.append(row)
    return rows


def write_png(path, rows):
    h, w = len(rows), len(rows[0])
    raw = b"".join(b"\x00" + b"".join(bytes(p) for p in row) for row in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def main():
    os.makedirs(OUT, exist_ok=True)
    canvas = draw()
    for size in (256, 128, 64, 48, 32, 16):
        write_png(os.path.join(OUT, "icon_%d.png" % size), downsample(canvas, size))
        print("wrote icon_%d.png" % size)


if __name__ == "__main__":
    main()
