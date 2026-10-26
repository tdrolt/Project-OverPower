"""Procedural flame-tongue flipbook, variant q3 (built from bake-off winner p2): readability first at game scale.

Writes the game's sheets "Fire Body.png" and "Fire Core.png", their plain team copies "Fire Body Team.png" and "Fire Core Team.png" (8x8 grid, 128 px cells, straight alpha, sRGB) two folders up, in
Shared/FX/Textures, and params.json (the bake-off renderer's numbers) next to this script.
Same teardrop-eroded-by-scrolling-noise idea as p2, but tuned to read from a top-down camera at 5 m:
- fewer, larger tongues (low-frequency splitting noise, 2-3 tongues per flame instead of many small licks);
- less fine erosion, a crisper alpha edge, so each flame is one bold silhouette rather than a ragged blob;
- a distinct hot inner layer: the sheet colour steps from a red rim to a yellow inner tongue, and the additive core
  traces that inner tongue, so the "hot centre / red edge" structure survives being shrunk to a few dozen pixels.
The noise scrolls a whole number of periods over 64 frames, so the sheet loops. Deterministic: numpy + PIL only.
"""
import json, os
import numpy as np
from PIL import Image

SEED = 7
COLS = ROWS = 8
CELL = 128
N = COLS * ROWS
HERE = os.path.dirname(os.path.abspath(__file__))
rng = np.random.default_rng(SEED)


def make_lattice(gx, gy):
    return rng.random((gy, gx)).astype(np.float32)


def pnoise(lat, X, Y):
    """Value noise on a lattice that wraps in both directions (smoothstep interpolation)."""
    gy, gx = lat.shape
    x0, y0 = np.floor(X), np.floor(Y)
    fx, fy = X - x0, Y - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    x0, y0 = x0.astype(int) % gx, y0.astype(int) % gy
    x1, y1 = (x0 + 1) % gx, (y0 + 1) % gy
    a = lat[y0, x0] * (1 - fx) + lat[y0, x1] * fx
    b = lat[y1, x0] * (1 - fx) + lat[y1, x1] * fx
    return a * (1 - fy) + b * fy


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


GY = 16
# Coarser octaves than p2 and the finest one nearly dropped: bold shapes, little fizz.
OCT = [(make_lattice(32, GY), 1.8, 1.8, 1, 0.70),
       (make_lattice(32, GY), 3.6, 3.6, 2, 0.25),
       (make_lattice(32, GY), 7.2, 7.2, 3, 0.05)]
WARP = [(make_lattice(32, GY), 1.3, 1.3, 1), (make_lattice(32, GY), 2.6, 2.6, 2)]

EROSION = (0.45, 1.05)  # base + height-scaled noise erosion (p2: 0.55, 1.25)
EDGE = (0.03, 0.09)    # alpha ramp on the field; narrower = crisper silhouette (p2: 0.02, 0.14)

c = (np.arange(CELL) + 0.5) / CELL * 2 - 1
x, y = np.meshgrid(c, -c)          # y up
r = np.sqrt(x * x + y * y)
circle = 1 - smooth(0.86, 0.97, r)  # keep everything inside the inscribed circle

BASE_Y, TIP_Y = -0.64, 0.93


def fbm(octs, X, Y, phase, xoff):
    v = np.zeros_like(X)
    for lat, fx, fy, per, w in octs:
        v += w * pnoise(lat, (X + xoff) * fx, Y * fy - phase * per * GY)
    return v


# Three explicit tongues per flame (centre tall, two shorter side ones). Each sways and stretches on integer
# cycles over the loop, so the sheet still loops. (x offset, base height share, half-width, sway cycles, stretch cycles, phase)
TONGUES = [(0.00, 1.00, 0.40, 1, 2, 0.00),
           (-0.38, 0.60, 0.27, 2, 3, 0.35),
           (0.38, 0.68, 0.27, 2, 3, 0.70)]


def frame(k):
    ph = k / N
    tau = 2 * np.pi
    h = np.clip((y - BASE_Y) / (TIP_Y - BASE_Y), 0, 1)       # 0 at base, 1 at tip
    warp = sum((pnoise(lat, (x + 3) * fx, y * fy - ph * per * GY) - 0.5) for lat, fx, fy, per in WARP)
    xw = x + warp * (0.08 + 0.35 * h ** 1.3)
    body = np.full_like(x, -1.0)
    for ox, hs, hw, sway, stretch, phs in TONGUES:
        top = BASE_Y + (TIP_Y - BASE_Y) * hs * (0.82 + 0.18 * np.sin(tau * (stretch * ph + phs)))
        cx = ox * (1 - 0.22 * np.clip((y - BASE_Y) / (top - BASE_Y), 0, 1)) + 0.07 * np.sin(tau * (sway * ph + phs))
        hh = np.clip((y - BASE_Y) / (top - BASE_Y), 0, 1)
        wid = hw * (1 - hh) ** 1.15 + 0.02
        t = 1 - ((xw - cx) / wid) ** 2
        t = np.where(y > top, -1, t)
        body = np.maximum(body, t) + 0.08 * np.clip(1 - np.abs(body - t) / 0.25, 0, 1) * (np.minimum(body, t) > -0.4)
    # one shared, wide rounded base so the tongues grow out of a single flame
    body = np.maximum(body, 1 - (xw / 0.54) ** 2 - ((y - BASE_Y - 0.18) / 0.36) ** 2)
    body *= smooth(BASE_Y - 0.30, BASE_Y + 0.34, y + 0.06 * np.abs(x) ** 0.5 * 4 + 0.10 * (fbm(OCT, x, y, 0.0, 11.0) - 0.5))
    n = fbm(OCT, x, y, ph, 5.0)
    f = body * (1.08 - 0.12 * h) - (n - 0.25) * (EROSION[0] + EROSION[1] * h)
    alpha = smooth(EDGE[0], EDGE[1], f) * circle
    # two-step heat: a clear red rim, then a yellow inner tongue in the lower half
    inner = smooth(0.55, 0.70, f) * (1 - smooth(0.30, 0.62, h))
    heat = 0.35 * smooth(0.18, 0.50, f) + 0.65 * inner
    core_a = smooth(0.55, 0.75, f) * (1 - smooth(0.22, 0.58, h)) * circle
    return alpha, heat, core_a


def to_srgb(lin):
    lin = np.clip(lin, 0, 1)
    return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * lin ** (1 / 2.4) - 0.055)


body_sheet = np.zeros((ROWS * CELL, COLS * CELL, 4), np.float32)
core_sheet = np.zeros_like(body_sheet)
# Team fire takes all its colour from the team's ramp, so its sheets are the same frames in grey: the rim-to-inner-tongue step is brightness
# only (a coloured sheet would multiply a blue or purple ramp down to near black), and the heart is plain white.
team_body_sheet = np.zeros_like(body_sheet)
team_core_sheet = np.zeros_like(body_sheet)
for k in range(N):
    a, ht, ca = frame(k)
    # linear heat ramp: deep red rim (1, .22, .06) -> orange -> yellow inner (1, .95, .55)
    rgb = np.stack([np.ones_like(ht), 0.22 + 0.73 * ht ** 1.1, 0.06 + 0.49 * ht ** 2.2], -1)
    rr, cc = divmod(k, COLS)
    sl = (slice(rr * CELL, (rr + 1) * CELL), slice(cc * CELL, (cc + 1) * CELL))
    body_sheet[sl] = np.concatenate([to_srgb(rgb), a[..., None]], -1)
    crgb = np.stack([np.ones_like(ca), 0.82 + 0.18 * ca, 0.45 + 0.4 * ca], -1)
    core_sheet[sl] = np.concatenate([to_srgb(crgb), ca[..., None]], -1)
    grey = np.repeat((0.85 + 0.15 * ht)[..., None], 3, -1)
    team_body_sheet[sl] = np.concatenate([to_srgb(grey), a[..., None]], -1)
    team_core_sheet[sl] = np.concatenate([np.ones_like(grey), ca[..., None]], -1)

Image.fromarray((body_sheet * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(HERE, "..", "..", "Fire Body.png"))
Image.fromarray((core_sheet * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(HERE, "..", "..", "Fire Core.png"))
Image.fromarray((team_body_sheet * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(HERE, "..", "..", "Fire Body Team.png"))
Image.fromarray((team_core_sheet * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(HERE, "..", "..", "Fire Core Team.png"))

params = {
    "_note": "q3b: q3 with the late-life ramp fading out before it darkens (no brown ghost flames at the jet tail / ground centre).",
    "grid": [COLS, ROWS],
    "body": {"gradient": [[0, 1.05, 0.30, 0.05, 1.0], [0.35, 0.9, 0.19, 0.03, 1.0], [0.62, 0.78, 0.15, 0.025, 0.5],
                          [0.82, 0.6, 0.1, 0.02, 0.0], [1, 0.12, 0.02, 0.01, 0.0]]},
    "core": {"enabled": True, "size_scale": 0.9,
             "gradient": [[0, 2.0, 0.9, 0.18, 0.8], [0.5, 1.3, 0.4, 0.06, 0.6], [0.85, 0.35, 0.07, 0.01, 0.15], [1, 0, 0, 0, 0]]},
    "flipbook": {"mode": "fps", "fps": 32, "random_start": True},
    "rotation": {"mode": "flow", "jitter_deg": 10},
    "jet": {"rate": 95, "life": [0.45, 0.65], "speed": 11.0, "slowdown": 0.4, "size_start": 0.3, "size_end": 1.45,
            "rise": 0.35, "rise_pow": 1.5, "spread_share": 0.5},
    "ground": {"alive": 42, "life": [0.7, 1.1], "size": [1.2, 1.8], "rise": 0.8, "catch": 0.3, "radius": 2.0},
}
with open(os.path.join(HERE, "params.json"), "w", encoding="utf-8") as fh:
    json.dump(params, fh, indent=1)
print("wrote Fire Body.png, Fire Core.png, Fire Body Team.png, Fire Core Team.png, params.json")
