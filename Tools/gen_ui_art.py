# Turns the owner's ChatGPT UI sheets (M32b) into game sprites. Input: the untracked Data/ui_raw/<kind>/<sheet>.png,
# each with a <sheet>.txt naming its cells row by row ("-" skips a cell; an optional first line "grid C R" overrides
# the kind's grid; single-image kinds hold one name). Output: Assets/_Game/Art/UI/<kind>/<name>.png at 3x design size
# (Docs/UiArtSizing.md) and Assets/_Game/Art/UI/ui_art.json, listing every sprite with its kind, size, 9-slice border
# (left, bottom, right, top; none = 0) and, for cursors, the hotspot. The editor importer (UiArtImporter) sets each
# sprite's border from it and UiArtTests checks every import against it. Needs Pillow (pip install pillow). Deterministic: re-running on
# the same sheets rewrites the same files. Run: python Tools/gen_ui_art.py, then let Unity import and commit the
# PNGs, ui_art.json and the .meta files.
#
# What it does to a cell: cut it out, trim to its visible pixels (alpha > 8), then resize with premultiplied alpha
# (ChatGPT leaves colour in fully transparent pixels; resizing straight would bleed it into the edges as a halo).
import json, os, sys
from PIL import Image, ImageChops, ImageFilter, ImageStat

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "..", "Data", "ui_raw")
OUT = os.path.join(HERE, "..", "Assets", "_Game", "Art", "UI")
LANCZOS = Image.Resampling.LANCZOS
ALPHA_CUT = 8

# Per kind: how a sheet is cut and how big each piece ships (px at 3x). Icons fit a square with a small margin.
KINDS = {
    "hud":         dict(mode="icon", grid=(4, 4), size=72),
    "tools":       dict(mode="icon", grid=(4, 4), size=96),
    "buildings":   dict(mode="icon", grid=(4, 4), size=144),
    "controls":    dict(mode="icon", grid=(2, 2), size=60),
    "cursors":     dict(mode="cursor", grid=(2, 2), sizes=(32, 64)),
    "buttons":     dict(mode="sliced", grid=(2, 2)),
    "frames":      dict(mode="sliced", grid=(1, 1)),
    "hudbar":      dict(mode="sliced", grid=(1, 1)),
    "meters":      dict(mode="meters"),
    "backgrounds": dict(mode="picture", size=(1536, 864)),
    "events":      dict(mode="picture", size=(1080, 608)),
    "logo":        dict(mode="fit_width", width=1140),
}
ICON_MARGIN = 0.04   # of the icon size, on every side

# Sliced art: the height it ships at and its border in source px of the trimmed art (left, bottom, right, top), measured
# where the corner ornaments end. The shipped border is that times the scale. flat_inset (source px, just inside the
# frame's inner edge) repaints the centre and the four edges' inner parts one even colour, leaving the corners alone
# (ChatGPT adds blotches and vignettes that a 9-slice would stretch).
SLICED = {
    "frame_panel":  dict(height=244, border=(186, 186, 186, 186), flat_inset=80),   # border 37 = 12 design px
    "frame_window": dict(height=244, border=(200, 200, 200, 200), flat_inset=92),   # border 40
    "button":       dict(height=96, border=(165, 160, 165, 160)),                       # border 33 x 32
    "hud_bar":      dict(height=228, border=(345, 0, 345, 0)),                          # 3-slice, ends 254
}
METER_HEIGHT = 30     # the track ships 30 px tall (10 design px); the fill keeps the track's scale
METER_END = 125       # source px of the track's end caps


def own_art(cell):
    """Clears what isn't the cell's own art: specks (under 2% of the biggest shape) and slivers of a neighbour's art
    that reach in over the cell's edge (touching it and under 30% of the biggest shape)."""
    step = 2   # label on a half-size mask: fast enough in pure Python, and specks smaller than that don't matter
    small = cell.getchannel("A").resize((max(1, cell.width // step), max(1, cell.height // step)), Image.Resampling.BOX)
    w, h = small.size
    filled = [v > ALPHA_CUT for v in small.getdata()]
    label = [0] * (w * h)
    shapes = []   # (size, touches the edge, pixel indices)
    for start in range(w * h):
        if not filled[start] or label[start]:
            continue
        n = len(shapes) + 1
        label[start] = n
        stack, pixels, edge = [start], [], False
        while stack:
            i = stack.pop()
            pixels.append(i)
            x, y = i % w, i // w
            if x == 0 or y == 0 or x == w - 1 or y == h - 1:
                edge = True
            for j in ((i - 1) if x > 0 else -1, (i + 1) if x < w - 1 else -1, (i - w) if y > 0 else -1,
                      (i + w) if y < h - 1 else -1):
                if j >= 0 and filled[j] and not label[j]:
                    label[j] = n
                    stack.append(j)
        shapes.append((len(pixels), edge, pixels))
    if len(shapes) <= 1:
        return cell
    biggest = max(size for size, _, _ in shapes)
    drop = Image.new("L", (w, h), 0)
    for size, edge, pixels in shapes:
        if size < 0.02 * biggest or (edge and size < 0.3 * biggest):
            for i in pixels:
                drop.putpixel((i % w, i // w), 255)
    if drop.getbbox() is None:
        return cell
    drop = drop.resize(cell.size, Image.Resampling.NEAREST).filter(ImageFilter.MaxFilter(5))
    keep = ImageChops.invert(drop)
    out = cell.copy()
    out.putalpha(ImageChops.multiply(cell.getchannel("A"), keep))
    return out


def trim(im):
    box = im.getchannel("A").point(lambda v: 255 if v > ALPHA_CUT else 0).getbbox()
    if box is None:
        return None
    return im.crop(box)


def resize(im, size):
    """Premultiplied resize, so hidden colour in transparent pixels can't bleed into the edges."""
    out = im.convert("RGBa").resize(size, LANCZOS).convert("RGBA")
    return clean(out)


def clean(im):
    """Fully transparent pixels to (0, 0, 0, 0): smaller files, no stray colour."""
    alpha = im.getchannel("A")
    mask = alpha.point(lambda v: 255 if v > 0 else 0)
    blank = Image.new("RGBA", im.size, (0, 0, 0, 0))
    return Image.composite(im, blank, mask)


def cells(im, cols, rows):
    w, h = im.width / cols, im.height / rows
    for r in range(rows):
        for c in range(cols):
            yield im.crop((round(c * w), round(r * h), round((c + 1) * w), round((r + 1) * h)))


def read_names(txt, grid):
    lines = [l.split() for l in open(txt, encoding="utf-8").read().splitlines() if l.strip()]
    if lines and lines[0][0] == "grid":
        grid = (int(lines[0][1]), int(lines[0][2]))
        lines = lines[1:]
    names = [n for line in lines for n in line]
    return names, grid


def fit_icon(piece, size):
    inner = size * (1 - 2 * ICON_MARGIN)
    scale = inner / max(piece.size)
    w, h = max(1, round(piece.width * scale)), max(1, round(piece.height * scale))
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(resize(piece, (w, h)), ((size - w) // 2, (size - h) // 2))
    return canvas


def cursor(piece, size):
    """Tip at the top-left: the trimmed art's corner goes to (pad, pad), the hotspot."""
    pad = max(1, size // 32)
    scale = (size - pad) / max(piece.size)
    w, h = max(1, round(piece.width * scale)), max(1, round(piece.height * scale))
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(resize(piece, (w, h)), (pad, pad))
    return canvas, pad


def flatten_centre(piece, border, inset):
    """One colour (the centre's median) over a cross: the centre plus the edges between the corners, from inset in."""
    l, b, r, t = border
    w, h = piece.size
    fill = tuple(int(v) for v in ImageStat.Stat(piece.crop((l, t, w - r, h - b))).median)
    piece = piece.copy()
    for box in ((inset, t, w - inset, h - b), (l, inset, w - r, h - inset)):
        piece.paste(Image.new("RGBA", (box[2] - box[0], box[3] - box[1]), fill), box[:2])
    return piece


def same_size(pieces):
    """Pads every piece to the largest, centred, so button states line up exactly."""
    w = max(p.width for p in pieces)
    h = max(p.height for p in pieces)
    out = []
    for p in pieces:
        canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        canvas.paste(p, ((w - p.width) // 2, (h - p.height) // 2))
        out.append(canvas)
    return out


def sliced_spec(name):
    for key, spec in SLICED.items():
        if name == key or name.startswith(key + "_"):
            return spec
    sys.exit(f"no SLICED entry for {name}")


def sliced(piece, spec):
    if spec.get("flat_inset"):
        piece = flatten_centre(piece, spec["border"], spec["flat_inset"])
    scale = spec["height"] / piece.height
    out = resize(piece, (round(piece.width * scale), spec["height"]))
    border = [round(v * scale) for v in spec["border"]]
    return out, border


def bands(im):
    """Horizontal runs of rows that hold visible pixels (the meter sheet's two bars)."""
    alpha = im.getchannel("A").point(lambda v: 255 if v > ALPHA_CUT else 0)
    runs, start = [], None
    for y in range(im.height):
        filled = alpha.crop((0, y, im.width, y + 1)).getbbox() is not None
        if filled and start is None:
            start = y
        elif not filled and start is not None:
            runs.append((start, y)); start = None
    if start is not None:
        runs.append((start, im.height))
    return [im.crop((0, a, im.width, b)) for a, b in runs if b - a > 8]


def light_fill(bar):
    """The fill bar's light-grey face only (its brass rim stays on the track), so tinting it colours only the fill."""
    hsv = bar.convert("RGB").convert("HSV")
    _, s, v = hsv.split()
    mask = ImageChops.multiply(s.point(lambda x: 255 if x < 50 else 0), v.point(lambda x: 255 if x > 150 else 0))
    mask = mask.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(1.5))
    alpha = ImageChops.multiply(bar.getchannel("A"), mask)
    face = bar.copy()
    face.putalpha(alpha)
    return trim(face)


def picture(im, size):
    w, h = size
    src = im.convert("RGB")
    aspect = w / h
    if src.width / src.height > aspect:
        cw = round(src.height * aspect)
        src = src.crop(((src.width - cw) // 2, 0, (src.width - cw) // 2 + cw, src.height))
    else:
        ch = round(src.width / aspect)
        src = src.crop((0, (src.height - ch) // 2, src.width, (src.height - ch) // 2 + ch))
    return src.resize(size, LANCZOS)


def main():
    manifest = {}
    written = []

    def save(kind, name, image, **info):
        folder = os.path.join(OUT, kind)
        os.makedirs(folder, exist_ok=True)
        image.save(os.path.join(folder, name + ".png"), optimize=True)
        if name in manifest:
            sys.exit(f"duplicate name {name}")
        manifest[name] = dict(kind=kind, size=list(image.size), **info)
        written.append(f"{kind}/{name}.png")

    for kind in sorted(os.listdir(RAW)):
        spec = KINDS.get(kind)
        folder = os.path.join(RAW, kind)
        if spec is None or not os.path.isdir(folder):
            print(f"skipped {kind} (no rule in KINDS)")
            continue
        for sheet in sorted(f for f in os.listdir(folder) if f.endswith(".png")):
            path = os.path.join(folder, sheet)
            txt = path[:-4] + ".txt"
            if not os.path.exists(txt):
                sys.exit(f"{path}: no {os.path.basename(txt)} naming its cells")
            im = Image.open(path).convert("RGBA")
            names, grid = read_names(txt, spec.get("grid", (1, 1)))
            mode = spec["mode"]

            if mode == "meters":
                track, fill = [trim(b) for b in bands(im)][:2]
                scale = METER_HEIGHT / track.height
                end = round(METER_END * scale)
                face = light_fill(fill)
                track_out = resize(track, (round(track.width * scale), METER_HEIGHT))
                fill_out = resize(face, (round(face.width * scale), round(face.height * scale)))
                half = fill_out.height // 2
                save(kind, names[0], track_out, border=[end, 0, end, 0])
                save(kind, names[1], fill_out, border=[half, 0, half, 0])
                continue
            if mode == "picture":
                save(kind, names[0], picture(im, spec["size"]))
                continue
            if mode == "fit_width":
                piece = trim(im)
                h = round(piece.height * spec["width"] / piece.width)
                save(kind, names[0], resize(piece, (spec["width"], h)))
                continue

            pieces = list(cells(im, *grid))
            if len(names) != len(pieces):
                sys.exit(f"{txt}: {len(names)} names for {len(pieces)} cells ({grid[0]} x {grid[1]})")
            named = [(n, trim(own_art(p))) for n, p in zip(names, pieces) if n != "-"]
            for n, p in named:
                if p is None:
                    sys.exit(f"{sheet}: cell {n} is empty")
            if mode == "icon":
                for n, p in named:
                    save(kind, n, fit_icon(p, spec["size"]))
            elif mode == "cursor":
                for n, p in named:
                    for size in spec["sizes"]:
                        image, pad = cursor(p, size)
                        save(kind, f"{n}_{size}", image, hotspot=[pad, pad])
            elif mode == "sliced":
                if len(named) > 1:
                    named = list(zip([n for n, _ in named], same_size([p for _, p in named])))
                for n, p in named:
                    image, border = sliced(p, sliced_spec(n))
                    save(kind, n, image, border=border)

    # A list of objects (not a name-keyed map) so Unity's JsonUtility can read it.
    sprites = [dict(name=n, **manifest[n]) for n in sorted(manifest)]
    with open(os.path.join(OUT, "ui_art.json"), "w", newline="\n") as f:
        f.write('{"sprites": [\n')
        f.write(",\n".join(" " + json.dumps(s) for s in sprites))
        f.write("\n]}\n")
    stale = []
    for kind in os.listdir(OUT):
        folder = os.path.join(OUT, kind)
        if os.path.isdir(folder):
            stale += [f"{kind}/{f}" for f in os.listdir(folder) if f.endswith(".png") and f"{kind}/{f}" not in written]
    print(f"{len(written)} sprites written to Assets/_Game/Art/UI")
    for s in sorted(stale):
        print(f"  stale (no longer made, delete it and its .meta if unused): {s}")


if __name__ == "__main__":
    main()
