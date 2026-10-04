# Generates the M18e placeholder audio: 21 UI / world sound effects and 14 ambience loops as 22.05 kHz mono
# 16-bit WAVs, plus Scriptables/Audio/AudioCatalog.asset wiring them up. Standard library only and fully
# deterministic (own PRNG, fixed seeds), with deterministic GUIDs. Run: python Tools/gen_audio.py, then
# let Unity import (it rewrites the .meta files; commit its version). Replace a WAV with a hand-made clip
# of the same name (keep the .meta), or add clips to the catalog's MusicSet slots in the Inspector.
import io, math, os, struct, uuid, wave

HERE = os.path.dirname(os.path.abspath(__file__))
AUDIO = os.path.join(HERE, "..", "Assets", "_Game", "Audio")
CATALOG = os.path.join(HERE, "..", "Assets", "_Game", "Scriptables", "Audio", "AudioCatalog.asset")
GUID_CATALOG_SCRIPT = "c07a2b5d4e6f4a8b9c0d1e2f3a4b5c81"       # Scripts/Audio/AudioCatalog.cs
NS = uuid.UUID("6f1c0e52-8d0a-4c3a-9a51-3c1d2b7e9f10")
SR = 22050
LOOP_SECONDS = 14
AGES = ["medieval", "renaissance", "industrial", "modern"]


def guid(name):
    return uuid.uuid5(NS, name).hex


class Rng:
    def __init__(self, seed):
        self.s = (seed * 2654435761 + 88172645463325252) & 0xFFFFFFFFFFFFFFFF or 1

    def next(self):                         # xorshift64*, 0..1
        x = self.s
        x ^= x >> 12
        x ^= (x << 25) & 0xFFFFFFFFFFFFFFFF
        x ^= x >> 27
        self.s = x
        return (((x * 0x2545F4914F6CDD1D) & 0xFFFFFFFFFFFFFFFF) >> 11) / float(1 << 53)

    def signed(self):
        return self.next() * 2.0 - 1.0

    def range(self, a, b):
        return a + (b - a) * self.next()


def zeros(n):
    return [0.0] * n


def noise(n, rng):
    return [rng.signed() for _ in range(n)]


def lowpass(x, a):
    y, out = 0.0, []
    for v in x:
        y += a * (v - y)
        out.append(y)
    return out


def highpass(x, a):
    lp = lowpass(x, a)
    return [v - l for v, l in zip(x, lp)]


def bandpass(x, lo, hi):
    return lowpass(highpass(x, lo), hi)


def amp(x, g):
    return [v * g for v in x]


def mulv(a, b):
    return [p * q for p, q in zip(a, b)]


def add_into(dst, src, start=0, gain=1.0):
    end = min(len(dst), start + len(src))
    for i in range(start, end):
        dst[i] += src[i - start] * gain


def norm(x, peak=0.9):
    m = max(1e-9, max(abs(v) for v in x))
    return [v * peak / m for v in x]


def fade(x, ins=0.003, outs=0.01):
    n = len(x)
    a, b = int(ins * SR), int(outs * SR)
    for i in range(min(a, n)):
        x[i] *= i / max(1, a)
    for i in range(min(b, n)):
        x[n - 1 - i] *= i / max(1, b)
    return x


def sine(freq, n, phase=0.0):
    return [math.sin(2 * math.pi * freq * i / SR + phase) for i in range(n)]


def sweep(f0, f1, n):
    out, ph = [], 0.0
    for i in range(n):
        f = f0 + (f1 - f0) * i / max(1, n - 1)
        ph += 2 * math.pi * f / SR
        out.append(math.sin(ph))
    return out


def decay(n, tau):                         # exp decay envelope, tau seconds
    return [math.exp(-i / SR / tau) for i in range(n)]


def tone(freq, seconds, tau, harmonics=(1.0,), start_noise=0.0, rng=None):
    n = int(seconds * SR)
    x = zeros(n)
    for k, h in enumerate(harmonics, 1):
        add_into(x, sine(freq * k, n), 0, h)
    x = mulv(x, decay(n, tau))
    if start_noise and rng:
        add_into(x, mulv(noise(int(0.01 * SR), rng), decay(int(0.01 * SR), 0.003)), 0, start_noise)
    return fade(x)


def seq(parts, gap):
    """Plays `parts` one after another, each starting `gap` seconds after the previous."""
    total = int(sum(gap for _ in parts) * SR) + max(len(p) for p in parts)
    out = zeros(total)
    for i, p in enumerate(parts):
        add_into(out, p, int(i * gap * SR))
    return out


# ---- sound effects (variation = seed, pitch = frequency scale) ----------------------------------------------------

def sfx_click(seed, p):
    r = Rng(seed)
    return fade(mulv(add_mix(tone(1800 * p, 0.03, 0.006), noise(int(0.03 * SR), r), 0.15), decay(int(0.03 * SR), 0.01)), 0.0005, 0.005)


def add_mix(a, b, g):
    out = list(a)
    add_into(out, b, 0, g)
    return out


def sfx_place(seed, p):
    r = Rng(seed)
    n = int(0.22 * SR)
    body = mulv(sweep(130 * p, 55 * p, n), decay(n, 0.07))
    knock = mulv(bandpass(noise(n, r), 0.05, 0.4), decay(n, 0.02))
    return fade(add_mix(body, knock, 0.5))


def sfx_zone(seed, p):
    r = Rng(seed)
    n = int(0.16 * SR)
    sw = [math.sin(math.pi * i / n) for i in range(n)]
    return fade(mulv(bandpass(noise(n, r), 0.08, 0.35 * p), sw))


def sfx_road(seed, p):
    r = Rng(seed)
    n = int(0.2 * SR)
    out = zeros(n)
    for _ in range(9):
        t = int(r.next() * (n - 400))
        burst = mulv(bandpass(noise(300, r), 0.1, 0.6), decay(300, 0.004))
        add_into(out, burst, t, r.range(0.4, 1.0))
    return fade(out)


def sfx_road_upgrade(seed, p):
    return seq([tone(500 * p, 0.12, 0.05, (1, 0.4)), tone(750 * p, 0.2, 0.08, (1, 0.4))], 0.07)


def sfx_pipe(seed, p):
    n = int(0.18 * SR)
    return fade(mulv(sweep(300 * p, 700 * p, n), decay(n, 0.06)))


def sfx_demolish(seed, p):
    r = Rng(seed)
    n = int(0.55 * SR)
    rumble = mulv(lowpass(noise(n, r), 0.08 * p), decay(n, 0.18))
    out = list(rumble)
    for _ in range(14):
        t = int(r.next() * (n - 600))
        add_into(out, mulv(bandpass(noise(500, r), 0.05, 0.5), decay(500, 0.01)), t, r.range(0.2, 0.7))
    return fade(norm(out))


def sfx_refused(seed, p):
    n = int(0.22 * SR)
    sq = [1.0 if math.sin(2 * math.pi * 120 * p * i / SR) > 0 else -1.0 for i in range(n)]
    return fade(mulv(lowpass(sq, 0.15), decay(n, 0.12)))


def sfx_nomoney(seed, p):
    return seq([tone(440 * p, 0.12, 0.08, (1, 0.5)), tone(330 * p, 0.2, 0.1, (1, 0.5))], 0.11)


def sfx_toast(seed, p):
    return tone(1320 * p, 0.35, 0.12, (1, 0.3, 0.1))


def sfx_levelup(seed, p):
    return seq([tone(f * p, 0.15, 0.05, (1, 0.3)) for f in (523, 659, 784)], 0.06)


def sfx_research(seed, p):
    return seq([tone(f * p, 0.5, 0.2, (1, 0.35, 0.12)) for f in (659, 784, 1047)], 0.11)


def sfx_age(seed, p):
    parts = [tone(f * p, 1.4, 0.5, (1, 0.5, 0.25, 0.1)) for f in (262, 330, 392, 523)]
    return seq(parts, 0.12)


def sfx_event_open(seed, p):
    r = Rng(seed)
    n = int(0.3 * SR)
    paper = mulv(bandpass(noise(n, r), 0.1, 0.5), [math.sin(math.pi * i / n) ** 2 for i in range(n)])
    return fade(add_mix(amp(paper, 0.6), tone(440 * p, 0.3, 0.1), 0.35))


def sfx_event_choice(seed, p):
    r = Rng(seed)
    n = int(0.18 * SR)
    stamp = mulv(sweep(180 * p, 70 * p, n), decay(n, 0.05))
    return fade(add_mix(stamp, mulv(bandpass(noise(n, r), 0.06, 0.3), decay(n, 0.015)), 0.4))


def sfx_fire(seed, p):
    r = Rng(seed)
    n = int(0.9 * SR)
    swell = [math.sin(math.pi * min(1.0, i / (0.5 * n))) ** 1.5 * math.exp(-i / SR / 0.6) for i in range(n)]
    whoosh = mulv(bandpass(noise(n, r), 0.04, 0.3), swell)
    out = list(whoosh)
    for _ in range(40):
        t = int(r.next() * (n - 300))
        add_into(out, mulv(noise(200, r), decay(200, 0.003)), t, r.range(0.1, 0.5))
    return fade(norm(out))


def sfx_plague(seed, p):
    return tone(196 * p, 1.6, 0.55, (1, 0.55, 0.32, 0.18))


def sfx_breakdown(seed, p):
    r = Rng(seed)
    n = int(0.5 * SR)
    clank = zeros(n)
    for f, g in ((310, 1.0), (587, 0.7), (1210, 0.5), (1890, 0.3)):
        add_into(clank, mulv(sine(f * p, n), decay(n, 0.08)), 0, g)
    add_into(clank, mulv(noise(int(0.02 * SR), r), decay(int(0.02 * SR), 0.004)), 0, 0.8)
    return fade(norm(clank))


def sfx_repair(seed, p):
    return seq([tone(900 * p, 0.06, 0.02, (1, 0.5)), tone(1100 * p, 0.06, 0.02, (1, 0.5)), tone(1400 * p, 0.12, 0.04, (1, 0.4))], 0.07)


def sfx_save(seed, p):
    return seq([tone(784 * p, 0.12, 0.06, (1, 0.3)), tone(1047 * p, 0.2, 0.09, (1, 0.3))], 0.08)


def sfx_load(seed, p):
    return seq([tone(1047 * p, 0.12, 0.06, (1, 0.3)), tone(784 * p, 0.2, 0.09, (1, 0.3))], 0.08)


# id, name, builder, loudness, variations [(seed, pitch)]
SFX = [
    (0, "click", sfx_click, 0.45, [(1, 1.0), (2, 1.08)]),
    (1, "place", sfx_place, 0.9, [(3, 1.0), (4, 1.1), (5, 0.92)]),
    (2, "zone_paint", sfx_zone, 0.5, [(6, 1.0), (7, 1.15)]),
    (3, "road_lay", sfx_road, 0.55, [(8, 1.0), (9, 1.0), (10, 1.0)]),
    (4, "road_upgrade", sfx_road_upgrade, 0.6, [(11, 1.0)]),
    (5, "pipe_lay", sfx_pipe, 0.55, [(12, 1.0), (13, 1.2)]),
    (6, "demolish", sfx_demolish, 0.9, [(14, 1.0), (15, 0.85)]),
    (7, "refused", sfx_refused, 0.6, [(16, 1.0)]),
    (8, "no_money", sfx_nomoney, 0.6, [(17, 1.0)]),
    (9, "toast", sfx_toast, 0.35, [(18, 1.0)]),
    (10, "level_up", sfx_levelup, 0.3, [(19, 1.0), (20, 1.12)]),
    (11, "research_done", sfx_research, 0.7, [(21, 1.0)]),
    (12, "age_advance", sfx_age, 0.85, [(22, 1.0)]),
    (13, "event_open", sfx_event_open, 0.7, [(23, 1.0)]),
    (14, "event_choice", sfx_event_choice, 0.7, [(24, 1.0)]),
    (15, "fire_start", sfx_fire, 0.9, [(25, 1.0)]),
    (16, "plague_bell", sfx_plague, 0.8, [(26, 1.0)]),
    (17, "breakdown", sfx_breakdown, 0.8, [(27, 1.0)]),
    (18, "repair", sfx_repair, 0.7, [(28, 1.0)]),
    (19, "save", sfx_save, 0.55, [(29, 1.0)]),
    (20, "load", sfx_load, 0.55, [(30, 1.0)]),
]


# ---- ambience loops -----------------------------------------------------------------------------------------------

def slow_lfo(n, rng, f1=0.07, f2=0.13):
    p1, p2 = rng.next() * 6.28, rng.next() * 6.28
    return [0.55 + 0.25 * math.sin(2 * math.pi * f1 * i / SR + p1) + 0.2 * math.sin(2 * math.pi * f2 * i / SR + p2) for i in range(n)]


def wind(n, rng, level, color):
    x = lowpass(noise(n, rng), color)
    x = lowpass(x, 0.5)
    return amp(mulv(x, slow_lfo(n, rng)), level)


def birds(n, rng, per_minute, lo=2200, hi=4600):
    out = zeros(n)
    count = int(per_minute * n / SR / 60)
    for _ in range(count):
        t = int(rng.next() * (n - SR))
        f0 = rng.range(lo, hi)
        notes = int(rng.range(2, 6))
        for k in range(notes):
            ln = int(rng.range(0.04, 0.10) * SR)
            f1 = f0 * rng.range(0.8, 1.35)
            note = mulv(sweep(f0, f1, ln), [math.sin(math.pi * i / ln) for i in range(ln)])
            add_into(out, note, t + int(k * 0.11 * SR), 0.22)
            f0 = f1
    return out


def murmur(n, rng, level):
    x = bandpass(noise(n, rng), 0.03, 0.35)
    syl = lowpass([abs(v) for v in noise(n, rng)], 0.0006)
    syl = [min(1.0, s * 9.0) for s in syl]
    return amp(mulv(mulv(x, syl), slow_lfo(n, rng, 0.05, 0.11)), level * 6.0)


def rumble(n, rng, level):
    return amp(mulv(lowpass(noise(n, rng), 0.012), slow_lfo(n, rng)), level * 6.0)


def passing_cars(n, rng, per_minute, level):
    out = zeros(n)
    for _ in range(int(per_minute * n / SR / 60)):
        ln = int(rng.range(1.5, 3.0) * SR)
        t = int(rng.next() * (n - ln))
        body = mulv(bandpass(noise(ln, rng), 0.02, 0.2), [math.sin(math.pi * i / ln) ** 2 for i in range(ln)])
        add_into(out, body, t, level)
    return out


def hammering(n, rng, per_minute, f0, level):
    out = zeros(n)
    t = 0.0
    while t < n / SR - 1.0:
        t += rng.range(60.0 / per_minute * 0.5, 60.0 / per_minute * 1.6)
        for k in range(int(rng.range(1, 4))):
            at = int((t + k * 0.28) * SR)
            if at + SR // 2 >= n:
                continue
            ping = mulv(add_mix(sine(f0 * rng.range(0.95, 1.05), SR // 2), sine(f0 * 2.4, SR // 2), 0.4), decay(SR // 2, 0.07))
            add_into(out, ping, at, level * rng.range(0.6, 1.0))
            add_into(out, mulv(noise(150, rng), decay(150, 0.003)), at, level * 0.7)
    return out


def chug(n, rng, bpm, level):
    out = zeros(n)
    beat = 60.0 / bpm
    t = 0.0
    while t * SR < n - SR // 2:
        thump = mulv(sweep(80, 38, SR // 3), decay(SR // 3, 0.09))
        add_into(out, thump, int(t * SR), level)
        add_into(out, mulv(highpass(noise(SR // 4, rng), 0.3), decay(SR // 4, 0.06)), int((t + beat * 0.5) * SR), level * 0.25)
        t += beat
    return out


def machine_hum(n, rng, level, base):
    out = zeros(n)
    for k, g in ((1, 1.0), (2, 0.6), (3, 0.35), (5, 0.15)):
        add_into(out, sine(base * k, n, rng.next() * 6.28), 0, g * level)
    add_into(out, mulv(lowpass(noise(n, rng), 0.05), slow_lfo(n, rng)), 0, level * 1.5)
    return out


def crickets(n, rng, voices):
    out = zeros(n)
    for _ in range(voices):
        f = rng.range(3800, 4600)
        t = rng.next() * 2.0
        while t * SR < n - SR // 2:
            chirps = int(rng.range(3, 6))
            for c in range(chirps):
                at = int((t + c * 0.045) * SR)
                ln = int(0.03 * SR)
                if at + ln >= n:
                    continue
                add_into(out, mulv(sine(f, ln), [math.sin(math.pi * i / ln) for i in range(ln)]), at, 0.16)
            t += rng.range(0.55, 1.5)
    return out


def crackle(n, rng):
    out = amp(lowpass(noise(n, rng), 0.02), 0.5)
    for _ in range(int(60 * n / SR)):
        t = int(rng.next() * (n - 400))
        add_into(out, mulv(highpass(noise(300, rng), 0.3), decay(300, rng.range(0.002, 0.006))), t, rng.range(0.2, 0.9))
    return out


def horse_clops(n, rng, per_minute, level):
    out = zeros(n)
    for _ in range(int(per_minute * n / SR / 60)):
        t = int(rng.next() * (n - SR))
        for k in range(int(rng.range(4, 9))):
            add_into(out, mulv(bandpass(noise(260, rng), 0.05, 0.4), decay(260, 0.01)), t + int(k * 0.32 * SR) + int(rng.signed() * 400), level)
            add_into(out, mulv(bandpass(noise(260, rng), 0.05, 0.4), decay(260, 0.01)), t + int((k * 0.32 + 0.12) * SR), level * 0.7)
    return out


def loop(builder, seed):
    n = LOOP_SECONDS * SR
    f = SR  # 1 s crossfade tail
    x = builder(Rng(seed), n + f)
    out = x[:n]
    for i in range(f):
        t = i / f
        out[i] = x[n + i] * (1.0 - t) + x[i] * t
    return norm(out, 0.8)


def mixes(*parts):
    n = max(len(p) for p in parts)
    out = zeros(n)
    for p in parts:
        add_into(out, p)
    return out


def amb(age, layer):
    def base(r, n):
        if age == "medieval":
            return mixes(wind(n, r, 1.0, 0.05), birds(n, r, 22), horse_clops(n, r, 1.5, 0.08))
        if age == "renaissance":
            return mixes(wind(n, r, 0.9, 0.05), birds(n, r, 16), tone_bells(n, r))
        if age == "industrial":
            return mixes(wind(n, r, 1.0, 0.04), birds(n, r, 6))
        return mixes(wind(n, r, 0.7, 0.03), birds(n, r, 4), machine_hum(n, r, 0.012, 60))

    def city(r, n):
        if age == "medieval":
            return mixes(murmur(n, r, 0.8), horse_clops(n, r, 4, 0.18))
        if age == "renaissance":
            return mixes(murmur(n, r, 1.0), horse_clops(n, r, 5, 0.16), tone_bells(n, r))
        if age == "industrial":
            return mixes(murmur(n, r, 0.8), horse_clops(n, r, 3, 0.14), rumble(n, r, 0.5), chug(n, r, 42, 0.12))
        return mixes(rumble(n, r, 1.0), passing_cars(n, r, 14, 0.5), murmur(n, r, 0.3))

    def industry(r, n):
        if age == "medieval":
            return hammering(n, r, 30, 1100, 0.6)
        if age == "renaissance":
            return mixes(hammering(n, r, 40, 900, 0.5), murmur(n, r, 0.2))
        if age == "industrial":
            return mixes(chug(n, r, 96, 0.7), highpass(amp(wind(n, r, 1.0, 0.3), 0.8), 0.1), machine_hum(n, r, 0.05, 55))
        return mixes(machine_hum(n, r, 0.09, 100), amp(wind(n, r, 1.0, 0.25), 0.5))

    return {"base": base, "city": city, "industry": industry}[layer]


def tone_bells(n, rng):
    out = zeros(n)
    for _ in range(2):
        t = int(rng.next() * (n - 4 * SR))
        f = rng.range(380, 520)
        for k in range(int(rng.range(2, 5))):
            add_into(out, tone(f, 3.0, 1.1, (1, 0.5, 0.25)), t + int(k * 1.3 * SR), 0.16)
    return out


def shared(kind):
    if kind == "night":
        return lambda r, n: mixes(crickets(n, r, 5), wind(n, r, 0.4, 0.03))
    return lambda r, n: crackle(n, r)


# ---- output -----------------------------------------------------------------------------------------------------

META = """fileFormatVersion: 2
guid: %s
AudioImporter:
  externalObjects: {}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: %d
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.6
    conversionMode: 0
    preloadAudioData: %d
  platformSettingOverrides: {}
  forceToMono: 1
  normalize: 1
  loadInBackground: %d
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_wav(rel, samples, is_loop):
    path = os.path.join(AUDIO, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    w = wave.open(path, "wb")
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in samples))
    w.close()
    g = guid("audio_" + rel)
    meta = path + ".meta"
    if os.path.exists(meta):
        for line in io.open(meta, encoding="utf-8"):
            if line.startswith("guid:"):
                return line.split()[1]
    # sfx: decompress on load (short); loops: compressed in memory, loaded in the background
    io.open(meta, "w", encoding="utf-8", newline="\n").write(META % (g, 1 if is_loop else 0, 0 if is_loop else 1, 1 if is_loop else 0))
    return g


def clip_ref(g):
    return "{fileID: 8300000, guid: %s, type: 3}" % g


def main():
    text = ("%%YAML 1.1\n%%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
            "  m_Script: {fileID: 11500000, guid: %s, type: 3}\n  m_Name: AudioCatalog\n"
            "  m_EditorClassIdentifier: Assembly-CSharp::AudioCatalog\n" % GUID_CATALOG_SCRIPT)
    text += "  m_Sfx:\n"
    sfx_count = 0
    for sid, name, builder, loud, variations in SFX:
        refs = []
        for i, (seed, pitch) in enumerate(variations):
            samples = norm(builder(seed, pitch), 0.9)
            refs.append(write_wav("Sfx/%s_%d.wav" % (name, i), samples, False))
            sfx_count += 1
        text += "  - Id: %d\n    Volume: %s\n    Clips:\n" % (sid, loud) + "".join("    - %s\n" % clip_ref(g) for g in refs)
    text += "  m_Ambience:\n"
    for ai, age in enumerate(AGES):
        refs = {}
        for li, layer in enumerate(("base", "city", "industry")):
            refs[layer] = write_wav("Ambience/%s_%s.wav" % (age, layer), loop(amb(age, layer), 100 + ai * 10 + li), True)
        text += "  - Base: %s\n    City: %s\n    Industry: %s\n" % (clip_ref(refs["base"]), clip_ref(refs["city"]), clip_ref(refs["industry"]))
    text += "  m_Music:\n" + "".join("  - Tracks: []\n" for _ in AGES)
    night = write_wav("Ambience/night.wav", loop(shared("night"), 200), True)
    fire = write_wav("Ambience/fire.wav", loop(shared("fire"), 201), True)
    text += "  m_Night: %s\n  m_Fire: %s\n" % (clip_ref(night), clip_ref(fire))
    os.makedirs(os.path.dirname(CATALOG), exist_ok=True)
    io.open(CATALOG, "w", encoding="utf-8", newline="\n").write(text)
    meta = CATALOG + ".meta"
    if not os.path.exists(meta):
        io.open(meta, "w", encoding="utf-8", newline="\n").write(
            "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid("audio_catalog"))
    print("sfx clips", sfx_count, "loops", 14)


main()
