# Generates the M18c tile art: textured road tiles (5 tiers x 16 connection masks) and ground tiles
# (4 ages x 4 variants), plus Scriptables/Art/TileArtSet.asset wiring them up. Standard library only,
# deterministic (hash noise, no random module) with deterministic GUIDs, so re-running rewrites the same
# files. Run: python Tools/gen_tiles.py, then let Unity import (it rewrites the .meta files; commit
# Unity's version). Replace any PNG with hand-made art of the same name and size (64 x 64) and keep the
# .meta: the TileArtSet keeps pointing at it. Delete a PNG's sprite from the set to fall back to the
# runtime-drawn road (RoadTilemapView) or the scene's ground tile.
import io, os, struct, uuid, zlib

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, "..", "Assets", "_Game", "Art", "Tiles")
SET_PATH = os.path.join(HERE, "..", "Assets", "_Game", "Scriptables", "Art", "TileArtSet.asset")
GUID_TILE_ART_SET_SCRIPT = "9f3c5d7e1a2b4c6d8e0f1a2b3c4d5e70"   # Scripts/Placement/TileArtSet.cs
NS = uuid.UUID("6f1c0e52-8d0a-4c3a-9a51-3c1d2b7e9f10")
SIZE = 64
N, E, S, W = 1, 2, 4, 8        # connection bits, logical directions (RoadTilemapView)
TIERS = 5
AGES = ["medieval", "renaissance", "industrial", "modern"]
VARIANTS = 4


def guid(name):
    return uuid.uuid5(NS, name).hex


def clamp(x):
    return 0.0 if x < 0.0 else 1.0 if x > 1.0 else x


def hash2(x, y, seed):
    h = (x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)
    h &= 0xFFFFFFFF
    h ^= h >> 13
    h = (h * 0x5BD1E995) & 0xFFFFFFFF
    h ^= h >> 15
    return h


def noise(x, y, seed):
    """-1..1"""
    return (hash2(x, y, seed) & 0xFFFF) / 32767.5 - 1.0


def mul(c, f):
    return (c[0] * f, c[1] * f, c[2] * f)


def add(c, d):
    return (c[0] + d, c[1] + d, c[2] + d)


def mix(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def write_png(path, pixels):
    """pixels[row][col] = (r, g, b) floats, row 0 = TEXTURE row 0 (the bottom of the image); stored top-down."""
    raw = bytearray()
    for row in reversed(pixels):
        raw.append(0)
        for r, g, b in row:
            raw += bytes((int(clamp(r) * 255 + 0.5), int(clamp(g) * 255 + 0.5), int(clamp(b) * 255 + 0.5)))

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, "wb").write(png)


META = """fileFormatVersion: 2
guid: %s
TextureImporter:
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  isReadable: 0
  streamingMipmaps: 0
  vTOnly: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 64
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 0
  alphaIsTransparency: 0
  spriteTessellationMethod: 0
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def emit(rel, pixels):
    """Writes the PNG and (only if it doesn't exist yet) a .meta with a deterministic GUID; returns the GUID."""
    path = os.path.join(ART, rel)
    write_png(path, pixels)
    g = guid("tile_" + rel)
    meta = path + ".meta"
    if os.path.exists(meta):                       # keep Unity's rewritten meta (and its guid)
        for line in io.open(meta, encoding="utf-8"):
            if line.startswith("guid:"):
                return line.split()[1]
    io.open(meta, "w", encoding="utf-8", newline="\n").write(META % g)
    return g


# ---- roads ----------------------------------------------------------------------------------------------------

LINE_HALF = 1
DASH = 8


class Style:
    def __init__(self, asphalt, curb, line, curb_width, offsets, kind):
        self.asphalt, self.curb, self.line, self.curb_width, self.offsets, self.kind = asphalt, curb, line, curb_width, offsets, kind


STYLES = {
    1: Style((0.46, 0.35, 0.23), (0.37, 0.28, 0.18), None, 4, [], "dirt"),
    2: Style((0.53, 0.49, 0.44), (0.64, 0.62, 0.58), None, 6, [], "cobble"),
    3: Style((0.30, 0.32, 0.35), (0.58, 0.59, 0.60), (0.95, 0.85, 0.40), 7, [0], "asphalt"),
    4: Style((0.26, 0.28, 0.31), (0.58, 0.59, 0.60), (0.95, 0.85, 0.40), 5, [-3, 3], "asphalt"),
    5: Style((0.16, 0.17, 0.19), (0.88, 0.88, 0.86), (0.92, 0.92, 0.90), 3, [-11, 11], "highway"),
}


def on_centre_line(px, py, center, mask, offsets):
    north, south, east, west = mask & N, mask & S, mask & E, mask & W
    for offset in offsets:
        on_v = abs(px - center + 0.5 - offset) <= LINE_HALF
        on_h = abs(py - center + 0.5 - offset) <= LINE_HALF
        seg = ((on_v and north and py <= center) or (on_v and south and py >= center)
               or (on_h and west and px <= center) or (on_h and east and px >= center))
        if not seg:
            continue
        along = py if (on_v and (north or south)) else px
        if (along // DASH) % 2 == 0:
            return True
    return False


def near_axis(px, py, center, mask, offset, half):
    """True on a band `offset` px from the centre line along every connected side (dirt ruts)."""
    v = abs(px - center + 0.5 - offset) <= half
    h = abs(py - center + 0.5 - offset) <= half
    return ((v and mask & N and py <= center) or (v and mask & S and py >= center)
            or (h and mask & W and px <= center) or (h and mask & E and px >= center))


def road_pixel(tier, mask, px, py):
    style = STYLES[tier]
    center = SIZE // 2
    to_w, to_e, to_s, to_n = px, SIZE - 1 - px, SIZE - 1 - py, py
    cw = style.curb_width
    curb = ((to_n < cw and not mask & N) or (to_s < cw and not mask & S)
            or (to_e < cw and not mask & E) or (to_w < cw and not mask & W))
    corner = ((to_n < cw and to_e < cw) or (to_n < cw and to_w < cw)
              or (to_s < cw and to_e < cw) or (to_s < cw and to_w < cw))
    seed = tier * 101 + mask
    g = noise(px, py, tier)                        # the same grain on every mask so neighbours blend

    if style.kind == "dirt":
        c = add(style.asphalt, g * 0.025)
        if near_axis(px, py, center, mask, -9, 2.0) or near_axis(px, py, center, mask, 9, 2.0):
            c = mul(c, 0.90)                        # wheel ruts
        if hash2(px, py, 7) % 53 == 0:
            c = add(c, 0.10 if hash2(px, py, 8) & 1 else -0.08)   # pebbles
        if curb or corner:
            c = add(style.curb, g * 0.03)
            if hash2(px, py, 9) % 9 == 0:
                c = mix(c, (0.38, 0.48, 0.24), 0.7)               # grass tufts along the verge
        return c

    if style.kind == "cobble":
        row = py // 8
        off = 4 if row % 2 else 0
        col = (px + off) // 8
        stone = noise(col, row, 21) * 0.045
        mortar = (px + off) % 8 == 0 or py % 8 == 0
        c = add(style.asphalt, stone + g * 0.012)
        if mortar:
            c = mul(c, 0.80)
        if curb or corner:
            c = add(style.curb, noise(col, row, 22) * 0.04 + g * 0.012)
            if (px + off) % 8 == 0 or py % 8 == 0:
                c = mul(c, 0.86)
        return c

    if style.kind == "highway":
        c = add(style.asphalt, g * 0.018)
        if hash2(px // 3, py // 3, 31) % 40 == 0:
            c = add(c, 0.025)
        if curb or corner:
            return add(style.curb, g * 0.02)
        if on_centre_line(px, py, center, mask, style.offsets):
            return mul(style.line, 0.96 - 0.1 * (hash2(px, py, 5) % 4 == 0))
        return c

    # asphalt (paved, avenue)
    c = add(style.asphalt, g * 0.02)
    if hash2(px // 2, py // 2, 41) % 37 == 0:
        c = add(c, 0.03)
    if hash2(px, py, 43) % 211 == 0:
        c = mul(c, 0.78)                            # a crack speck
    if curb or corner:
        return add(style.curb, g * 0.02)
    if on_centre_line(px, py, center, mask, style.offsets):
        wear = 0.82 + 0.18 * ((hash2(px, py, 6) % 8) / 7.0)
        return mix(c, style.line, wear)
    return c


def road_tile(tier, mask):
    return [[road_pixel(tier, mask, px, py) for px in range(SIZE)] for py in range(SIZE)]


# ---- ground ---------------------------------------------------------------------------------------------------

GROUND_BASE = {
    "medieval": (0.47, 0.60, 0.34),
    "renaissance": (0.50, 0.62, 0.38),
    "industrial": (0.45, 0.52, 0.36),
    "modern": (0.52, 0.65, 0.40),
}


def ground_tile(age, variant):
    base = GROUND_BASE[age]
    seed = AGES.index(age) * 17 + variant * 5 + 1000
    pixels = []
    for py in range(SIZE):
        row = []
        for px in range(SIZE):
            blob = noise(px // 3, py // 3, seed) * 0.03          # soft patches
            fine = noise(px, py, seed + 1) * 0.012                # blades
            c = add(base, blob + fine)
            if age == "renaissance":
                c = add(c, 0.018 if (py // 8) % 2 == 0 else -0.018)   # mown stripes, continuous across tiles
            elif age == "modern":
                c = add(c, 0.0)
            elif age == "industrial":
                if hash2(px, py, seed + 2) % 41 == 0:
                    c = mul(c, 0.62)                              # soot flecks
                if hash2(px, py, seed + 3) % 97 == 0:
                    c = (0.55, 0.54, 0.52)                        # gravel
            interior = 3 <= px < SIZE - 3 and 3 <= py < SIZE - 3
            if age == "medieval" and interior:
                h = hash2(px, py, seed + 4)
                if h % 331 == 0:
                    c = (0.95, 0.85, 0.30)                        # buttercups
                elif h % 397 == 1:
                    c = (0.94, 0.94, 0.90)                        # daisies
                elif h % 521 == 2:
                    c = (0.66, 0.45, 0.72)                        # clover blooms
                elif h % 61 == 3:
                    c = mul(base, 0.78)                           # long-grass tufts
            if age == "renaissance" and interior and hash2(px, py, seed + 5) % 251 == 0:
                c = (0.96, 0.92, 0.55)
            row.append(c)
        pixels.append(row)
    return pixels


# ---- run ------------------------------------------------------------------------------------------------------

def sprite_ref(g):
    return "{fileID: 21300000, guid: %s, type: 3}" % g


def main():
    roads, ground = [], []
    for tier in range(1, TIERS + 1):
        for mask in range(16):
            roads.append(emit("Roads/road_t%d_m%02d.png" % (tier, mask), road_tile(tier, mask)))
    for age in AGES:
        for v in range(VARIANTS):
            ground.append(emit("Ground/ground_%s_%d.png" % (age, v), ground_tile(age, v)))

    text = ("%%YAML 1.1\n%%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
            "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n  m_Name: TileArtSet\n"
            "  m_EditorClassIdentifier: Assembly-CSharp::TileArtSet\n" % GUID_TILE_ART_SET_SCRIPT)
    text += "  m_Roads:\n" + "".join("  - %s\n" % sprite_ref(g) for g in roads)
    text += "  m_Ground:\n" + "".join("  - %s\n" % sprite_ref(g) for g in ground)
    os.makedirs(os.path.dirname(SET_PATH), exist_ok=True)
    io.open(SET_PATH, "w", encoding="utf-8", newline="\n").write(text)
    meta = SET_PATH + ".meta"
    if not os.path.exists(meta):
        io.open(meta, "w", encoding="utf-8", newline="\n").write(
            "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid("tile_art_set"))
    print("road tiles", len(roads), "ground tiles", len(ground))


main()
