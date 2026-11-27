# Nestation CC BY-NC 3.0
import json
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = sys.argv[1] if len(sys.argv) > 1 else "Resources/Textures/_nestation/Reactor"
S = 32
LICENSE = "Nestation CC BY-NC 3.0"

OUTLINE = (18, 20, 26)
STEEL_XD = (30, 34, 42)
STEEL_D = (52, 58, 68)
STEEL = (88, 97, 110)
STEEL_L = (134, 145, 160)
STEEL_XL = (186, 197, 210)
WHITE = (238, 242, 246)
YELLOW = (236, 196, 44)
RED = (206, 52, 44)
ORANGE = (240, 142, 44)
GREEN = (70, 196, 96)
CYAN = (96, 214, 236)
BLUE = (52, 120, 224)
COPPER = (184, 112, 58)
CONCRETE = (116, 114, 108)
CONCRETE_D = (82, 80, 76)
CONCRETE_L = (150, 148, 140)

MEDIUM = {
    "coolant": (150, 98, 64),
    "steam": (176, 182, 190),
    "feed": (66, 98, 142),
    "cooling": (54, 120, 118),
    "shaft": (118, 122, 132),
    "control": (58, 66, 62),
}

ACCENT = {
    "coolant": (196, 122, 66),
    "steam": (170, 64, 58),
    "feed": (86, 132, 196),
    "cooling": (74, 164, 152),
    "shaft": (190, 160, 60),
    "control": (190, 160, 60),
}

PIPE_W = {"coolant": 12, "steam": 12, "feed": 8, "cooling": 10, "shaft": 8, "control": 4}


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c)


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def bevel(d, box, fill, light=STEEL_L, dark=STEEL_XD, outline=OUTLINE):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=fill, outline=outline)
    d.line((x0 + 1, y0 + 1, x1 - 1, y0 + 1), fill=light)
    d.line((x0 + 1, y0 + 1, x0 + 1, y1 - 1), fill=light)
    d.line((x0 + 1, y1 - 1, x1 - 1, y1 - 1), fill=dark)
    d.line((x1 - 1, y0 + 1, x1 - 1, y1 - 1), fill=dark)


def lamp(d, x, y, color, on=True):
    c = color if on else shade(color, 0.25)
    d.rectangle((x, y, x + 1, y + 1), fill=c)


def sheet(frames):
    n = len(frames)
    w, h = frames[0].size
    img = Image.new("RGBA", (w * n, h), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        img.paste(f, (w * i, 0))
    return img


class Rsi:
    def __init__(self, name, w=S, h=S):
        self.name = name
        self.w = w
        self.h = h
        self.dir = os.path.join(ROOT, name + ".rsi")
        os.makedirs(self.dir, exist_ok=True)
        self.states = []

    def add(self, state, frames, delay=0.15):
        if not isinstance(frames, list):
            frames = [frames]
        sheet(frames).save(os.path.join(self.dir, state + ".png"))
        entry = {"name": state}
        if len(frames) > 1:
            entry["delays"] = [[delay] * len(frames)]
        self.states.append(entry)

    def save(self):
        meta = {
            "version": 1,
            "license": LICENSE,
            "copyright": "Nestation",
            "size": {"x": self.w, "y": self.h},
            "states": self.states,
        }
        with open(os.path.join(self.dir, "meta.json"), "w", encoding="utf-8") as fh:
            json.dump(meta, fh, indent=2)
            fh.write("\n")


def new(w=S, h=S):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def circle(d, cx, cy, r, fill=None, outline=None):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=fill, outline=outline)


def tile_px(w, h, off):
    return ((off[0] + (w - 1) / 2) * 32 + 16, ((h - 1) / 2 - off[1]) * 32 + 16)


def nozzle(d, w, h, off, side, medium, length=18, thick=None):
    thick = PIPE_W[medium]
    cx, cy = tile_px(w, h, off)
    cx, cy = int(cx), int(cy)
    c = MEDIUM[medium]
    half = thick // 2
    if side == "E":
        box = (cx + 16 - length, cy - half, cx + 15, cy + half - 1)
        flange = (cx + 11, cy - half - 3, cx + 14, cy + half + 2)
        cyl_h(d, box, c, 0.45, 1.2)
        d.rectangle(flange, fill=shade(c, 0.75), outline=OUTLINE)
        d.line((cx + 12, cy - half - 2, cx + 12, cy + half + 1), fill=shade(c, 1.2))
        for yy in (cy - half - 2, cy + half + 1):
            d.point((cx + 13, yy), fill=STEEL_XL)
    elif side == "W":
        box = (cx - 16, cy - half, cx - 16 + length - 1, cy + half - 1)
        flange = (cx - 15, cy - half - 3, cx - 12, cy + half + 2)
        cyl_h(d, box, c, 0.45, 1.2)
        d.rectangle(flange, fill=shade(c, 0.75), outline=OUTLINE)
        d.line((cx - 14, cy - half - 2, cx - 14, cy + half + 1), fill=shade(c, 1.2))
        for yy in (cy - half - 2, cy + half + 1):
            d.point((cx - 13, yy), fill=STEEL_XL)
    elif side == "S":
        box = (cx - half, cy + 16 - length, cx + half - 1, cy + 15)
        flange = (cx - half - 3, cy + 11, cx + half + 2, cy + 14)
        cyl_v(d, box, c, 0.45, 1.2)
        d.rectangle(flange, fill=shade(c, 0.75), outline=OUTLINE)
        d.line((cx - half - 2, cy + 12, cx + half + 1, cy + 12), fill=shade(c, 1.2))
        for xx in (cx - half - 2, cx + half + 1):
            d.point((xx, cy + 13), fill=STEEL_XL)
    else:
        box = (cx - half, cy - 16, cx + half - 1, cy - 16 + length - 1)
        flange = (cx - half - 3, cy - 15, cx + half + 2, cy - 12)
        cyl_v(d, box, c, 0.45, 1.2)
        d.rectangle(flange, fill=shade(c, 0.75), outline=OUTLINE)
        d.line((cx - half - 2, cy - 14, cx + half + 1, cy - 14), fill=shade(c, 1.2))
        for xx in (cx - half - 2, cx + half + 1):
            d.point((xx, cy - 13), fill=STEEL_XL)


def cyl_h(d, box, base, k0=0.55, k1=1.15):
    x0, y0, x1, y1 = box
    for y in range(y0, y1 + 1):
        t = (y - y0) / max(1, y1 - y0)
        k = k0 + (k1 - k0) * math.sin(math.pi * min(1.0, t * 0.9 + 0.05))
        d.line((x0, y, x1, y), fill=shade(base, k))
    d.rectangle(box, outline=OUTLINE)


def cyl_v(d, box, base, k0=0.55, k1=1.15):
    x0, y0, x1, y1 = box
    for x in range(x0, x1 + 1):
        t = (x - x0) / max(1, x1 - x0)
        k = k0 + (k1 - k0) * math.sin(math.pi * min(1.0, t * 0.9 + 0.05))
        d.line((x, y0, x, y1), fill=shade(base, k))
    d.rectangle(box, outline=OUTLINE)


def glow(im, cx, cy, r, color, alpha):
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    for i in range(r, 0, -2):
        a = int(alpha * (1 - i / r) ** 1.4) + 6
        ld.ellipse((cx - i, cy - i, cx + i, cy + i), fill=color + (min(255, a),))
    im.alpha_composite(layer)


def trefoil(d, cx, cy, r):
    circle(d, cx, cy, r, YELLOW, OUTLINE)
    for k in range(3):
        a = math.radians(90 + k * 120)
        px_ = cx + (r * 0.55) * math.cos(a)
        py_ = cy - (r * 0.55) * math.sin(a)
        circle(d, px_, py_, max(1, r // 3), OUTLINE)
    circle(d, cx, cy, max(1, r // 5), YELLOW, OUTLINE)


def stack(base, layer, dx=3, dy=3, alpha=120, clip=True):
    mask = layer.split()[3].point(lambda v: 255 if v > 16 else 0)
    black = Image.new("RGBA", layer.size, (0, 0, 0, alpha))
    shadow = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    shadow.paste(black, (dx, dy), mask)
    if clip:
        clip_mask = base.split()[3]
        sa = ImageChops.multiply(shadow.split()[3], clip_mask)
        shadow.putalpha(sa)
    base.alpha_composite(shadow)
    base.alpha_composite(layer)


def dome(layer, cx, cy, r, base_color, light=(-0.55, -0.6), ambient=0.5, spec=0.45):
    px_ = layer.load()
    lx, ly = light
    lz = math.sqrt(max(0.0, 1 - lx * lx - ly * ly))
    for y in range(int(cy - r), int(cy + r) + 1):
        for x in range(int(cx - r), int(cx + r) + 1):
            dx, dy = x - cx, y - cy
            d2 = (dx * dx + dy * dy) / (r * r)
            if d2 > 1:
                continue
            nz = math.sqrt(1 - d2)
            nx, ny = dx / r, dy / r
            dot = max(0.0, nx * lx + ny * ly + nz * lz)
            k = ambient + (1 - ambient) * dot
            c = shade(base_color, 0.55 + 0.75 * k)
            if dot > 0.93:
                c = mix(c, STEEL_XL, (dot - 0.93) / 0.07 * spec)
            if d2 > 0.9:
                c = shade(c, 0.8)
            px_[x, y] = c + (255,)


def hex_bolt(d, x, y, r=2):
    circle(d, x, y, r, STEEL_L, OUTLINE)
    d.point((x - 1, y - 1), fill=STEEL_XL)
    d.point((x, y), fill=STEEL_XD)


LIFT = [0, 3, 7, 11, 13, 10, 5, 0, 1, 0, 3, 8, 12, 9, 4, 0]
JITTER = [(0, 0), (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (0, 0)]


def core_frame(state, f):
    W = H = 160
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    rnd = random.Random(7)
    lift = LIFT[f % len(LIFT)] if state == "critical" else 0

    slab = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(slab)
    d.rectangle((2, 2, 151, 151), fill=CONCRETE, outline=OUTLINE)
    for _ in range(420):
        x, y = rnd.randint(3, 150), rnd.randint(3, 150)
        d.point((x, y), fill=shade(CONCRETE, rnd.choice((0.85, 0.92, 1.08, 1.15))))
    for y in (50, 101):
        d.line((3, y, 150, y), fill=CONCRETE_D)
        d.line((3, y + 1, 150, y + 1), fill=CONCRETE_L)
    for x in (50, 101):
        d.line((x, 3, x, 150), fill=CONCRETE_D)
        d.line((x + 1, 3, x + 1, 150), fill=CONCRETE_L)
    d.line((3, 3, 150, 3), fill=CONCRETE_L)
    d.line((3, 3, 3, 150), fill=CONCRETE_L)
    d.line((3, 150, 150, 150), fill=CONCRETE_D)
    d.line((150, 3, 150, 150), fill=CONCRETE_D)
    for i in range(6, 148, 8):
        d.polygon([(i, 6), (i + 4, 6), (i + 8, 10), (i + 4, 10)], fill=YELLOW if (i // 8) % 2 == 0 else OUTLINE)
        d.polygon([(i, 145), (i + 4, 145), (i + 8, 149), (i + 4, 149)], fill=YELLOW if (i // 8) % 2 == 0 else OUTLINE)
    for x, y in ((8, 14), (143, 14), (8, 138), (143, 138)):
        hex_bolt(d, x, y, 3)
    for x, y in ((24, 30), (136, 128)):
        circle(d, x, y, 3, CONCRETE_D, OUTLINE)
        d.line((x - 2, y, x + 2, y), fill=OUTLINE)
        d.line((x, y - 2, x, y + 2), fill=OUTLINE)
    base.alpha_composite(slab)

    ring = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dome(ring, 80, 80, 66, mix(CONCRETE_D, STEEL_D, 0.5), ambient=0.6, spec=0.1)
    d = ImageDraw.Draw(ring)
    for k in range(24):
        a_ = math.radians(k * 15 + 7.5)
        d.line((80 + 54 * math.cos(a_), 80 - 54 * math.sin(a_), 80 + 65 * math.cos(a_), 80 - 65 * math.sin(a_)), fill=STEEL_XD)
    d.ellipse((14, 14, 146, 146), outline=OUTLINE)
    stack(base, ring, 4, 4, 130)

    vessel = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dome(vessel, 80, 80, 52, STEEL, ambient=0.45, spec=0.55)
    d = ImageDraw.Draw(vessel)
    d.ellipse((28, 28, 132, 132), outline=OUTLINE)
    for k in range(16):
        a_ = math.radians(k * 22.5)
        hex_bolt(d, 80 + 46 * math.cos(a_), 80 - 46 * math.sin(a_), 2)
    for k in range(32):
        a_ = math.radians(k * 11.25 + 5)
        d.point((80 + 38 * math.cos(a_), 80 - 38 * math.sin(a_)), fill=STEEL_XD)
    d.ellipse((38, 38, 122, 122), outline=shade(STEEL, 0.6))
    vessel_layer = vessel
    if state != "critical":
        stack(base, vessel, 4, 5, 130)

    rods = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(rods)
    gl = {"off": (44, 56, 66), "run": CYAN, "hot": (255, 120, 40), "alarm": RED, "broken": (14, 14, 16), "critical": (255, 170, 80)}[state]
    pulse = [0.7, 0.85, 1.0, 0.85][f % 4]
    if state == "alarm":
        gl = RED if f % 2 == 0 else shade(RED, 0.35)
    elif state in ("run", "hot"):
        gl = shade(gl, pulse)
    elif state == "critical":
        gl = [(255, 255, 235), (255, 150, 60), (255, 90, 40), (255, 220, 160)][f % 4]
    for row in range(-4, 5):
        for col in range(-4, 5):
            x = 80 + col * 8 + (4 if row % 2 else 0)
            y = 80 + row * 7
            rr = math.hypot(x - 80, y - 80)
            if rr > 36 or rr < 16:
                continue
            circle(d, x, y, 3, (30, 34, 42), STEEL_XD)
            d.arc((x - 3, y - 3, x + 3, y + 3), 180, 270, fill=STEEL_L)
            lit = state in ("run", "hot") and (row * 3 + col + f) % 5 == 0
            if state == "critical":
                jump = lift + (3 if (row + col) % 2 else 0) - (2 if (row * col) % 3 == 0 else 0)
                jump = max(0, jump)
                d.line((x, y, x, y - jump), fill=(176, 184, 196))
                d.line((x + 1, y, x + 1, y - jump), fill=(92, 98, 110))
                circle(d, x, y - jump, 3, (210, 216, 226), STEEL_XD)
                d.point((x - 1, y - jump - 1), fill=WHITE)
                d.point((x, y - jump), fill=gl)
            elif lit:
                d.point((x, y), fill=gl)
                d.point((x + 1, y), fill=gl)
            else:
                d.point((x, y), fill=shade(STEEL_L, 0.8))
    rods_layer = rods

    tower = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dome(tower, 80, 80, 15, STEEL_D, ambient=0.5, spec=0.5)
    d = ImageDraw.Draw(tower)
    d.ellipse((65, 65, 95, 95), outline=OUTLINE)
    circle(d, 80, 80, 9, shade(gl, 0.5), gl)
    circle(d, 80, 80, 5, gl)
    d.point((78, 78), fill=WHITE)
    if state == "critical":
        upper = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        upper.alpha_composite(vessel_layer)
        upper.alpha_composite(rods_layer)
        upper.alpha_composite(tower)
        up = lift // 2
        moved = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        moved.paste(upper, (0, -up))
        stack(base, moved, 4 + lift // 2, 5 + lift, max(60, 140 - lift * 5))
        for k in range(8):
            a_ = math.radians(k * 45 + f * 17)
            rr = 40 + lift * 1.6
            cx_, cy_ = 80 + rr * math.cos(a_), 80 - rr * math.sin(a_)
            if 0 <= cx_ < W and 0 <= cy_ < H:
                circle(ImageDraw.Draw(base), cx_, cy_, 2 + (f + k) % 3, (232, 236, 240, 150), None)
    else:
        stack(base, rods_layer, 2, 2, 140)
        stack(base, tower, 3, 3, 120)

    if state in ("run", "hot", "alarm", "critical"):
        glow(base, 80, 80, 44 + lift * 2, gl, 90 if state != "critical" else 150)

    gear = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(gear)
    for cx_, cy_ in ((26, 30), (134, 30)):
        dome(gear, cx_, cy_, 10, (170, 176, 188), ambient=0.5, spec=0.5)
        d.ellipse((cx_ - 10, cy_ - 10, cx_ + 10, cy_ + 10), outline=OUTLINE)
        circle(d, cx_, cy_, 4, (24, 28, 34), STEEL_L)
        ang = {"off": -2.0, "run": -0.9, "hot": 0.4, "alarm": 0.7, "broken": -2.5, "critical": 1.2 + 0.4 * (f % 4)}[state]
        d.line((cx_, cy_, cx_ + 3 * math.cos(ang), cy_ + 3 * math.sin(ang)), fill=RED if state in ("hot", "alarm") else WHITE)
    d.line((36, 33, 50, 42), fill=STEEL, width=2)
    d.line((124, 33, 110, 42), fill=STEEL, width=2)
    bevel(d, (14, 120, 40, 140), STEEL_D, light=STEEL_L)
    for i in range(3):
        lamp(d, 18 + i * 7, 124, (GREEN, YELLOW, RED)[i], (state in ("run", "hot"), state == "hot", state == "alarm" and f % 2 == 0)[i])
        d.rectangle((17 + i * 7, 123, 20 + i * 7, 126), outline=OUTLINE)
    for i in range(4):
        d.line((17, 130 + i * 2, 37, 130 + i * 2), fill=STEEL_XD)
    trefoil(d, 134, 130, 8)
    for x, y in ((12, 12), (146, 12), (12, 146), (146, 146)):
        circle(d, x, y, 3, RED if (state in ("alarm", "critical") and f % 2 == 0) else shade(RED, 0.4), OUTLINE)
    stack(base, gear, 3, 3, 130)

    noz = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(noz)
    for side, off in (("E", (2, 0)), ("W", (-2, 0))):
        nozzle(d, 5, 5, off, side, "coolant", 46, 12)
        cx_, cy_ = tile_px(5, 5, off)
        sx = int(cx_) + (16 if side == "E" else -16)
        sign = -1 if side == "E" else 1
        for k in range(1, 4):
            x = sx + sign * k * 9
            d.line((x, int(cy_) - 6, x, int(cy_) + 5), fill=shade(MEDIUM["coolant"], 0.55))
        d.line((sx + sign * 36, int(cy_) - 5, sx + sign * 12, int(cy_) - 5), fill=(255, 190, 120))
    stack(base, noz, 3, 4, 120, clip=False)

    if state == "broken":
        d = ImageDraw.Draw(base)
        for pts in (((50, 40), (70, 66), (80, 80)), ((80, 80), (104, 70), (122, 48)), ((80, 80), (72, 108), (84, 126))):
            d.line(pts, fill=(14, 14, 16), width=3)
        d.polygon([(108, 98), (126, 104), (118, 124), (100, 114)], fill=(10, 10, 12))
        d.polygon([(38, 96), (52, 104), (44, 120), (30, 112)], fill=(10, 10, 12))
        veil = Image.new("RGBA", (W, H), (0, 0, 0, 70))
        veil.putalpha(ImageChops.multiply(veil.split()[3], base.split()[3]))
        base.alpha_composite(veil)
    if state in ("alarm", "critical"):
        d = ImageDraw.Draw(base)
        for i in range(12, 148, 10):
            d.polygon([(i, 153), (i + 5, 153), (i + 9, 149), (i + 4, 149)], fill=YELLOW)
    if state == "critical":
        jx, jy = JITTER[f % len(JITTER)]
        shaken = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        shaken.paste(base, (jx, jy))
        return shaken
    return base


def make_core():
    r = Rsi("core", 160, 160)
    r.add("core_off", core_frame("off", 0))
    r.add("core_run", [core_frame("run", i) for i in range(4)], 0.25)
    r.add("core_hot", [core_frame("hot", i) for i in range(4)], 0.18)
    r.add("core_alarm", [core_frame("alarm", i) for i in range(2)], 0.3)
    r.add("core_broken", core_frame("broken", 0))
    r.add("core_critical", [core_frame("critical", i) for i in range(16)], 0.07)
    r.save()


def grime(layer, rnd, amount=320, lo=0.86, hi=1.1):
    px_ = layer.load()
    w, h = layer.size
    for _ in range(amount):
        x, y = rnd.randrange(w), rnd.randrange(h)
        r, g, b, a = px_[x, y]
        if a > 0:
            k = rnd.choice((lo, lo + 0.05, hi - 0.05, hi))
            px_[x, y] = (min(255, int(r * k)), min(255, int(g * k)), min(255, int(b * k)), a)


def streaks(layer, rnd, count=8, length=14):
    alpha = layer.split()[3]
    d = ImageDraw.Draw(layer)
    w, h = layer.size
    for _ in range(count):
        x, y = rnd.randrange(w), rnd.randrange(h)
        d.line((x, y, x, y + rnd.randint(4, length)), fill=(52, 46, 40, 255))
    layer.putalpha(alpha)


def hazard(d, box, vertical=False):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=(160, 134, 40))
    if vertical:
        for y in range(y0, y1, 6):
            d.polygon([(x0, y), (x1, y - 3), (x1, y + 0), (x0, y + 3)], fill=(30, 30, 32))
    else:
        for x in range(x0, x1, 6):
            d.polygon([(x, y0), (x + 3, y0), (x + 6, y1), (x + 3, y1)], fill=(30, 30, 32))
    d.rectangle(box, outline=OUTLINE)


def skid_plate(w, h, box, rnd, base=(60, 66, 76)):
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=base, outline=OUTLINE)
    d.line((x0 + 1, y0 + 1, x1 - 1, y0 + 1), fill=STEEL_L)
    d.line((x0 + 1, y0 + 1, x0 + 1, y1 - 1), fill=STEEL)
    d.line((x0 + 1, y1 - 1, x1 - 1, y1 - 1), fill=STEEL_XD)
    d.line((x1 - 1, y0 + 1, x1 - 1, y1 - 1), fill=STEEL_XD)
    for x in range(x0 + 6, x1 - 4, 6):
        for y in range(y0 + 6, y1 - 4, 6):
            d.point((x, y), fill=shade(base, 1.18))
            d.point((x + 1, y + 1), fill=shade(base, 0.8))
    for x, y in ((x0 + 4, y0 + 4), (x1 - 4, y0 + 4), (x0 + 4, y1 - 4), (x1 - 4, y1 - 4)):
        hex_bolt(d, x, y, 2)
    grime(layer, rnd)
    streaks(layer, rnd, 5, 10)
    return layer


def pump_frame(kind, on, f):
    W = H = 96
    medium = {"primary": "coolant", "feed": "feed", "circ": "cooling"}[kind]
    accent = ACCENT[medium]
    rnd = random.Random(11)
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    stack(base, skid_plate(W, H, (3, 30, 92, 93), rnd), 4, 4, 130, clip=False)

    arrow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(arrow)
    ac = shade(accent, 0.8)
    d.polygon([(10, 80), (20, 74), (20, 86)], fill=ac, outline=OUTLINE)
    d.rectangle((20, 77, 34, 83), fill=ac, outline=OUTLINE)
    base.alpha_composite(arrow)

    cas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dome(cas, 48, 62, 26, (126, 134, 146), ambient=0.5, spec=0.55)
    d = ImageDraw.Draw(cas)
    d.ellipse((22, 36, 74, 88), outline=OUTLINE)
    for k in range(12):
        a_ = math.radians(k * 30)
        hex_bolt(d, 48 + 21.5 * math.cos(a_), 62 - 21.5 * math.sin(a_), 2)
    circle(d, 48, 62, 12, (20, 24, 30), STEEL_L)
    ang = math.radians(f * 30) if on else 0.0
    for k in range(4):
        a_ = ang + k * math.pi / 2
        d.line((48, 62, 48 + 10 * math.cos(a_), 62 + 10 * math.sin(a_)), fill=accent if on else shade(accent, 0.5), width=2)
    circle(d, 48, 62, 3, STEEL_XL, OUTLINE)
    d.arc((36, 50, 60, 74), 200, 280, fill=(210, 220, 230))
    grime(cas, rnd, 120)
    stack(base, cas, 3, 4, 130)

    noz = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(noz)
    nozzle(d, 3, 3, (-1, 0), "W", medium, 26)
    nozzle(d, 3, 3, (1, 0), "E", medium, 26)
    stack(base, noz, 3, 4, 120, clip=False)

    guard = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(guard)
    hazard(d, (38, 35, 58, 41))
    stack(base, guard, 2, 3, 120)

    mot = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(mot)
    cyl_v(d, (26, 4, 70, 36), (104, 112, 126), 0.4, 1.3)
    for x in range(29, 69, 4):
        d.line((x, 8, x, 34), fill=shade((104, 112, 126), 0.55))
        d.line((x + 1, 8, x + 1, 34), fill=shade((104, 112, 126), 1.3))
    cyl_v(d, (26, 17, 70, 23), shade(accent, 0.9), 0.5, 1.2)
    d.ellipse((26, 2, 70, 10), fill=(142, 150, 164), outline=OUTLINE)
    d.ellipse((30, 3, 50, 7), fill=(176, 184, 196))
    bevel(d, (70, 12, 80, 28), (92, 98, 110))
    d.line((72, 20, 78, 20), fill=OUTLINE)
    lamp(d, 33, 26, GREEN, on)
    lamp(d, 39, 26, RED, False)
    grime(mot, rnd, 100)
    stack(base, mot, 4, 4, 130)
    return base


def make_pumps():
    r = Rsi("pumps", 96, 96)
    for kind in ("primary", "feed", "circ"):
        r.add(kind + "_off", pump_frame(kind, False, 0))
        r.add(kind + "_on", [pump_frame(kind, True, i) for i in range(4)], 0.08)
    r.save()


def sg_frame(state, f):
    W, H = 96, 160
    rnd = random.Random(21)
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    body_c = (136, 144, 156)

    sad = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(sad)
    for y in (32, 122):
        bevel(d, (12, y, 84, y + 10), (62, 68, 78), light=STEEL, dark=OUTLINE)
        d.line((16, y + 4, 80, y + 4), fill=STEEL_XD)
    stack(base, sad, 4, 4, 130, clip=False)

    noz = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(noz)
    nozzle(d, 3, 5, (-1, 1), "W", "coolant", 24)
    nozzle(d, 3, 5, (-1, -1), "W", "coolant", 24)
    nozzle(d, 3, 5, (0, 2), "N", "steam", 26)
    nozzle(d, 3, 5, (0, -2), "S", "feed", 26)
    stack(base, noz, 3, 4, 120, clip=False)

    body = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(body)
    cyl_v(d, (20, 18, 76, 142), body_c, 0.38, 1.35)
    for cap_y, up in ((18, True), (142, False)):
        for i in range(10):
            t = i / 10.0
            yy = cap_y - 10 * math.sqrt(1 - (1 - t) ** 2) if up else cap_y + 10 * math.sqrt(1 - (1 - t) ** 2)
            half_w = int(28 * math.sqrt(max(0.0, 1 - (1 - t) ** 2 * 0))) if False else 28
        d.ellipse((20, 8, 76, 28) if up else (20, 132, 76, 152), fill=shade(body_c, 1.1 if up else 0.7), outline=OUTLINE)
    d.arc((24, 10, 72, 26), 200, 340, fill=STEEL_XL)
    for y in range(32, 134, 14):
        d.line((21, y, 75, y), fill=shade(body_c, 0.55))
        d.line((21, y + 1, 75, y + 1), fill=shade(body_c, 1.25))
    for y in (46, 80, 114):
        d.rectangle((20, y, 76, y + 3), fill=(78, 84, 96), outline=OUTLINE)
        d.line((21, y + 1, 75, y + 1), fill=STEEL_L)
        d.rectangle((70, y - 1, 73, y + 4), fill=STEEL, outline=OUTLINE)
    circle(d, 46, 62, 10, (106, 114, 128), OUTLINE)
    for k in range(8):
        a_ = math.radians(k * 45)
        hex_bolt(d, 46 + 8 * math.cos(a_), 62 + 8 * math.sin(a_), 1)
    circle(d, 46, 62, 5, (74, 80, 92), STEEL_L)
    circle(d, 46, 100, 9, (230, 232, 236), OUTLINE)
    circle(d, 46, 100, 7, (30, 34, 40))
    ang = {"off": -2.2, "run": -0.9 + 0.1 * (f % 4), "hot": 0.5 + 0.08 * (f % 4)}[state]
    d.line((46, 100, 46 + 6 * math.cos(ang), 100 + 6 * math.sin(ang)), fill=RED if state == "hot" else WHITE)
    d.rectangle((62, 64, 67, 116), fill=(20, 28, 36), outline=OUTLINE)
    fill = {"off": (50, 66, 86), "run": (84, 140, 196), "hot": (200, 112, 70)}[state]
    d.rectangle((63, 92, 66, 115), fill=fill)
    if state != "off":
        for k in range(3):
            d.point((64, 112 - ((f * 3 + k * 6) % 20)), fill=WHITE)
    grime(body, rnd, 360)
    streaks(body, rnd, 10, 20)
    if state == "hot":
        glow(body, 48, 84, 38, (230, 90, 50), 80)
        body.putalpha(ImageChops.lighter(body.split()[3], body.split()[3]))
    stack(base, body, 5, 5, 125)

    top = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(top)
    cyl_v(d, (62, 2, 70, 16), (140, 100, 70), 0.45, 1.25)
    d.rectangle((60, 1, 72, 3), fill=STEEL_L, outline=OUTLINE)
    lamp(d, 28, 22, GREEN, state != "off")
    lamp(d, 34, 22, RED, state == "hot")
    ladder_x = 82
    d.line((ladder_x, 40, ladder_x, 138), fill=STEEL_L)
    d.line((ladder_x + 6, 40, ladder_x + 6, 138), fill=STEEL_L)
    for y in range(44, 138, 6):
        d.line((ladder_x, y, ladder_x + 6, y), fill=STEEL)
    stack(base, top, 3, 3, 110, clip=False)
    return base


def make_sg():
    r = Rsi("steamgen", 96, 160)
    r.add("sg_off", sg_frame("off", 0))
    r.add("sg_run", [sg_frame("run", i) for i in range(4)], 0.2)
    r.add("sg_hot", [sg_frame("hot", i) for i in range(4)], 0.15)
    r.save()


def turbine_frame(on, hot, f):
    W, H = 224, 96
    rnd = random.Random(31)
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    frame = skid_plate(W, H, (8, 66, 206, 90), rnd, (56, 62, 72))
    stack(base, frame, 4, 4, 130, clip=False)

    noz = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(noz)
    nozzle(d, 7, 3, (-2, -1), "S", "steam", 34)
    nozzle(d, 7, 3, (2, -1), "S", "steam", 28)
    stack(base, noz, 3, 4, 120, clip=False)

    cas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(cas)
    c0 = (128, 136, 148)
    cyl_h(d, (22, 30, 92, 66), c0, 0.38, 1.3)
    cyl_h(d, (92, 18, 190, 78), c0, 0.38, 1.3)
    d.polygon([(92, 30), (106, 18), (106, 78), (92, 66)], fill=shade(c0, 0.7), outline=OUTLINE)
    d.line((22, 48, 190, 48), fill=shade(c0, 0.4))
    d.line((22, 49, 190, 49), fill=shade(c0, 1.3))
    for x in range(26, 190, 6):
        hi_ = 33 if x < 92 else 22
        lo_ = 62 if x < 92 else 74
        d.point((x, hi_), fill=STEEL_XL)
        d.point((x, hi_ + 1), fill=STEEL_XD)
        d.point((x, lo_), fill=STEEL_XL)
        d.point((x, lo_ + 1), fill=STEEL_XD)
    for x in (34, 60, 84, 118, 142, 170):
        d.rectangle((x, 20 if x > 100 else 31, x + 3, 76 if x > 100 else 65), fill=shade(c0, 0.6), outline=shade(c0, 0.35))
    bevel(d, (36, 22, 74, 32), (96, 102, 116))
    for x in (44, 62):
        cyl_v(d, (x, 10, x + 6, 22), (150, 110, 80), 0.5, 1.2)
        d.rectangle((x - 1, 9, x + 7, 11), fill=STEEL_L, outline=OUTLINE)
    bevel(d, (12, 36, 26, 60), (92, 98, 112))
    bevel(d, (186, 32, 204, 64), (92, 98, 112))
    for cx, cy, r in ((56, 48, 8), (124, 48, 13), (158, 48, 13)):
        circle(d, cx, cy, r + 2, (86, 92, 104), OUTLINE)
        circle(d, cx, cy, r, (10, 14, 18), STEEL_L)
        ang = math.radians(f * 15) if on else 0.0
        col = (230, 120, 60) if hot else (186, 196, 208)
        for k in range(8):
            a_ = ang + k * math.pi / 4
            d.line((cx, cy, cx + (r - 1) * math.cos(a_), cy + (r - 1) * math.sin(a_)), fill=col)
        circle(d, cx, cy, 2, STEEL_XL, OUTLINE)
        if hot:
            glow(cas, cx, cy, r + 8, (230, 100, 50), 70)
    grime(cas, rnd, 520)
    streaks(cas, rnd, 12, 18)
    stack(base, cas, 5, 5, 125)

    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(sh)
    cyl_h(d, (204, 44, 223, 51), (150, 156, 168), 0.4, 1.3)
    hazard(d, (206, 38, 218, 57))
    lamp(d, 26, 24, GREEN, on)
    lamp(d, 32, 24, RED, hot)
    stack(base, sh, 3, 3, 120, clip=False)
    return base


def make_turbine():
    r = Rsi("turbine", 224, 96)
    r.add("turbine_off", turbine_frame(False, False, 0))
    r.add("turbine_run", [turbine_frame(True, False, i) for i in range(6)], 0.06)
    r.add("turbine_hot", [turbine_frame(True, True, i) for i in range(6)], 0.04)
    r.save()


def gen_frame(on, f):
    W, H = 160, 96
    rnd = random.Random(41)
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    stack(base, skid_plate(W, H, (22, 70, 152, 92), rnd, (56, 62, 72)), 4, 4, 130, clip=False)

    cas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(cas)
    cyl_h(d, (38, 18, 138, 78), (112, 122, 138), 0.36, 1.35)
    for x in range(44, 136, 5):
        d.line((x, 20, x, 76), fill=shade((112, 122, 138), 0.5))
        d.line((x + 1, 20, x + 1, 76), fill=shade((112, 122, 138), 1.25))
    cyl_h(d, (38, 42, 138, 54), (176, 108, 62), 0.45, 1.3)
    for x in range(42, 136, 12):
        d.line((x, 42, x, 54), fill=shade((176, 108, 62), 0.55))
    bevel(d, (138, 26, 154, 70), (88, 94, 108))
    for y in (34, 44, 54, 62):
        d.line((140, y, 152, y), fill=STEEL_XD)
    bevel(d, (26, 28, 42, 68), (88, 94, 108))
    bevel(d, (64, 2, 112, 20), (96, 102, 116), light=STEEL_XL)
    for x in (74, 88, 102):
        cyl_v(d, (x - 3, 6, x + 3, 22), (222, 224, 226), 0.55, 1.2)
        d.line((x, 22, x, 30), fill=(176, 108, 62), width=2)
    d.polygon([(88, 4), (80, 17), (96, 17)], fill=(190, 160, 50), outline=OUTLINE)
    d.line((88, 8, 88, 12), fill=OUTLINE)
    d.point((88, 14), fill=OUTLINE)
    lamp(d, 44, 22, GREEN, on)
    lamp(d, 50, 22, (210, 170, 60), on and f % 2 == 0)
    lamp(d, 56, 22, RED, False)
    grime(cas, rnd, 420)
    streaks(cas, rnd, 10, 16)
    if on:
        glow(cas, 88, 48, 30, (110, 190, 230), 40)
    stack(base, cas, 5, 5, 125)

    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(sh)
    cyl_h(d, (0, 44, 28, 51), (150, 156, 168), 0.4, 1.3)
    hazard(d, (6, 38, 20, 57))
    if on:
        sx = 40 + (f * 23) % 96
        d.line((sx, 70, sx + 4, 72), fill=(255, 235, 150))
        d.point((sx + 2, 71), fill=(140, 220, 240))
    stack(base, sh, 3, 3, 120, clip=False)
    return base


def make_gen():
    r = Rsi("generator", 160, 96)
    r.add("gen_off", gen_frame(False, 0))
    r.add("gen_on", [gen_frame(True, i) for i in range(4)], 0.12)
    r.save()


def cond_frame(on, f):
    W, H = 160, 96
    rnd = random.Random(51)
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sad = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(sad)
    for x in (34, 108):
        bevel(d, (x, 74, x + 14, 88), (60, 66, 76), light=STEEL, dark=OUTLINE)
    stack(base, sad, 4, 4, 130, clip=False)

    noz = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(noz)
    nozzle(d, 5, 3, (0, 1), "N", "steam", 22)
    nozzle(d, 5, 3, (-2, -1), "S", "feed", 22)
    nozzle(d, 5, 3, (2, 1), "E", "cooling", 20)
    nozzle(d, 5, 3, (2, -1), "E", "cooling", 20)
    stack(base, noz, 3, 4, 120, clip=False)

    shell = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(shell)
    c0 = (116, 124, 136)
    cyl_h(d, (24, 16, 130, 82), c0, 0.36, 1.3)
    for x in range(34, 124, 16):
        d.rectangle((x, 16, x + 2, 82), fill=shade(c0, 0.6))
        d.line((x + 3, 17, x + 3, 81), fill=shade(c0, 1.3))
    for x in range(64, 92, 3):
        d.line((x, 17, x, 81), fill=shade(c0, 0.5))
        d.line((x + 1, 17, x + 1, 81), fill=shade(c0, 1.35))
    for x0, x1 in ((8, 24), (130, 146)):
        bevel(d, (x0, 12, x1, 86), (84, 92, 106), light=STEEL_L)
        circle(d, (x0 + x1) // 2, 36, 5, (60, 66, 78), STEEL_L)
        circle(d, (x0 + x1) // 2, 62, 5, (60, 66, 78), STEEL_L)
        for yy in (20, 80):
            hex_bolt(d, (x0 + x1) // 2, yy, 2)
    d.rectangle((40, 28, 66, 66), fill=(14, 22, 30), outline=STEEL_XL)
    water = (50, 130, 150) if on else (36, 66, 80)
    d.rectangle((41, 48, 65, 65), fill=water)
    for row in range(5):
        for col in range(6):
            xx, yy = 43 + col * 4, 31 + row * 4
            d.rectangle((xx, yy, xx + 1, yy + 1), fill=STEEL_XL if (row + col) % 2 else STEEL_L)
    if on:
        for k in range(4):
            d.point((44 + k * 6, 64 - ((f * 3 + k * 4) % 14)), fill=WHITE)
    d.rectangle((100, 24, 108, 72), fill=(18, 26, 34), outline=OUTLINE)
    d.rectangle((101, 44 if on else 54, 107, 71), fill=(56, 132, 150) if on else (36, 66, 80))
    for yy in range(28, 72, 8):
        d.line((109, yy, 112, yy), fill=STEEL_XL)
    lamp(d, 28, 20, GREEN, on)
    lamp(d, 34, 20, RED, False)
    grime(shell, rnd, 460)
    streaks(shell, rnd, 12, 18)
    stack(base, shell, 5, 5, 125)
    return base


def make_cond():
    r = Rsi("condenser", 160, 96)
    r.add("cond_off", cond_frame(False, 0))
    r.add("cond_run", [cond_frame(True, i) for i in range(4)], 0.2)
    r.save()


def console_frame(state, f):
    W, H = 96, 32
    rnd = random.Random(61)
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    desk = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(desk)
    bevel(d, (1, 15, 93, 28), (66, 72, 84), light=STEEL_L)
    d.line((2, 22, 92, 22), fill=STEEL_XD)
    for col in range(20):
        for row in range(2):
            xx = 6 + col * 4
            yy = 16 + row * 3
            d.rectangle((xx, yy, xx + 2, yy + 1), fill=(176, 182, 192) if (row + col) % 3 else (136, 142, 152))
    for x in (8, 40, 70):
        d.rectangle((x, 23, x + 12, 25), fill=(30, 34, 40), outline=OUTLINE)
    circle(d, 86, 22, 4, RED, shade(RED, 0.4))
    d.arc((82, 18, 90, 26), 200, 340, fill=(255, 190, 180))
    grime(desk, rnd, 90)
    stack(base, desk, 3, 3, 130, clip=False)
    mons = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(mons)
    for i in range(3):
        x0 = 4 + i * 30
        bevel(d, (x0, 1, x0 + 26, 17), (84, 90, 102), light=STEEL_XL)
        scr = (8, 38, 30)
        if state == "off":
            scr = (14, 18, 20)
        elif state == "alarm" and f % 2 == 0:
            scr = (84, 16, 14)
        d.rectangle((x0 + 2, 3, x0 + 24, 14), fill=scr, outline=OUTLINE)
        if state == "on":
            if i == 0:
                pts = [(x0 + 3 + j * 2, 9 + int(2.5 * math.sin((j + f) * 0.9))) for j in range(10)]
                d.line(pts, fill=(80, 190, 110))
            elif i == 1:
                for j in range(3):
                    d.line((x0 + 4, 5 + j * 3, x0 + 4 + (6 + (f * 3 + j * 5) % 14), 5 + j * 3), fill=((100, 190, 210), (200, 170, 70))[j % 2])
            else:
                circle(d, x0 + 13, 9, 4, None, (80, 190, 110))
                d.line((x0 + 13, 9, x0 + 13 + 4 * math.cos(f), 9 + 4 * math.sin(f)), fill=(80, 190, 110))
        elif state == "alarm":
            cl = RED if f % 2 == 0 else (210, 170, 70)
            d.line((x0 + 4, 5, x0 + 22, 13), fill=cl)
            d.line((x0 + 22, 5, x0 + 4, 13), fill=cl)
        d.line((x0 + 3, 4, x0 + 10, 4), fill=shade(scr, 1.6) if state != "off" else (30, 34, 38))
    stack(base, mons, 3, 3, 130)
    return base


def make_console():
    r = Rsi("console", 96, 32)
    r.add("console_off", console_frame("off", 0))
    r.add("console_on", [console_frame("on", i) for i in range(4)], 0.3)
    r.add("console_alarm", [console_frame("alarm", i) for i in range(2)], 0.3)
    r.save()


def pool_frame(flags, f):
    n, so, e, w = flags & 1, flags & 2, flags & 4, flags & 8
    im = Image.new("RGBA", (S, S), CONCRETE + (255,))
    px_ = im.load()
    left = 0 if w else 4
    right = S - 1 if e else S - 5
    top = 0 if n else 5
    bottom = S - 1 if so else S - 4
    two_pi = 2 * math.pi
    for y in range(S):
        for x in range(S):
            if not (left <= x <= right and top <= y <= bottom):
                continue
            dl = (x - left) if not w else 99
            dr = (right - x) if not e else 99
            dt = (y - top) if not n else 99
            db = (bottom - y) if not so else 99
            wall = min(dl, dr, dt, db)
            depth = min(1.0, wall / 9.0) if wall < 99 else 1.0
            tile = (24, 120, 172) if (x // 8 + y // 8) % 2 == 0 else (28, 130, 182)
            col = mix((86, 190, 220), tile, 0.35 + 0.65 * depth)
            col = mix(col, (14, 72, 124), 0.55 * depth)
            if x % 8 == 0 or y % 8 == 0:
                col = shade(col, 0.82)
            c1 = math.sin(two_pi * (x * 2 + f * 8) / 32.0 + 1.3 * math.sin(two_pi * y / 32.0))
            c2 = math.sin(two_pi * (y * 2 - f * 8) / 32.0 + 1.3 * math.sin(two_pi * x / 32.0))
            caustic = c1 * c2
            if caustic > 0.72:
                col = mix(col, (150, 215, 240), (caustic - 0.72) * 0.9)
            if wall < 99:
                if wall == 0:
                    col = (200, 236, 250)
                elif wall == 1:
                    col = mix(col, (150, 214, 238), 0.7)
                elif wall == 2:
                    col = shade(col, 0.78)
            px_[x, y] = col + (255,)
    d = ImageDraw.Draw(im)
    if not n:
        d.rectangle((0, 0, S - 1, 2), fill=CONCRETE_L)
        d.line((0, 3, S - 1, 3), fill=CONCRETE_D)
        d.line((0, 4, S - 1, 4), fill=shade(CONCRETE_D, 0.6))
    if not so:
        d.rectangle((0, S - 3, S - 1, S - 1), fill=CONCRETE_D)
        d.line((0, S - 4, S - 1, S - 4), fill=CONCRETE_L)
    if not w:
        d.rectangle((0, 0, 2, S - 1), fill=CONCRETE_L if n else CONCRETE)
        d.line((3, 0, 3, S - 1), fill=CONCRETE_D)
    if not e:
        d.rectangle((S - 4, 0, S - 1, S - 1), fill=CONCRETE_D)
        d.line((S - 5, 0, S - 5, S - 1), fill=CONCRETE_L)
    rim = []
    if not n and not w:
        rim.append((1, 1))
    if not n and not e:
        rim.append((S - 3, 1))
    if not so and not w:
        rim.append((1, S - 3))
    if not so and not e:
        rim.append((S - 3, S - 3))
    for x, y in rim:
        d.rectangle((x, y, x + 1, y + 1), fill=STEEL_L, outline=OUTLINE)
    if not n and (flags & 12) == 12:
        for x in (8, 24):
            d.point((x, 1), fill=CONCRETE_D)
    if not so:
        for x in range(4, S - 4, 8):
            d.point((x, S - 2), fill=shade(CONCRETE_D, 0.6))
    return im


def steam_frame(f):
    im = new()
    d = ImageDraw.Draw(im)
    for k in range(7):
        x = (k * 5 + 3) % 30
        y = 26 - ((f * 4 + k * 5) % 24)
        r = 2 + (k % 2)
        d.ellipse((x, y, x + r, y + r), fill=(240, 244, 248, 150))
    return im


def make_pool():
    r = Rsi("pool")
    for flags in range(16):
        r.add("pool%d" % flags, [pool_frame(flags, i) for i in range(4)], 0.4)
    r.add("steam", [steam_frame(i) for i in range(4)], 0.25)
    r.save()


def pipe_frame(kind, flags):
    pad = 12
    size = 32 + 2 * pad
    half = PIPE_W[kind] / 2.0
    c = pad + 16
    lo, hi = int(c - half), int(c + half - 1)
    mask = Image.new("L", (size, size), 0)
    md = ImageDraw.Draw(mask)
    arms = []
    if flags & 1:
        arms.append("N")
        md.rectangle((lo, 0, hi, c), fill=255)
    if flags & 2:
        arms.append("S")
        md.rectangle((lo, c, hi, size - 1), fill=255)
    if flags & 4:
        arms.append("E")
        md.rectangle((c, lo, size - 1, hi), fill=255)
    if flags & 8:
        arms.append("W")
        md.rectangle((0, lo, c, hi), fill=255)
    straight = ("N" in arms and "S" in arms and len(arms) == 2) or ("E" in arms and "W" in arms and len(arms) == 2)
    elbow = len(arms) == 2 and not straight
    if elbow or not arms:
        md.ellipse((c - half - 0.5, c - half - 0.5, c + half + 0.5, c + half + 0.5), fill=255)
    else:
        md.rectangle((lo, lo, hi, hi), fill=255)

    levels = [mask.point(lambda v: 255 if v > 0 else 0)]
    for _ in range(int(half) + 2):
        levels.append(levels[-1].filter(ImageFilter.MinFilter(3)))
    dist = [[0] * size for _ in range(size)]
    for lvl in levels:
        lp = lvl.load()
        for y in range(size):
            for x in range(size):
                if lp[x, y] > 0:
                    dist[y][x] += 1

    body = mix(STEEL, MEDIUM[kind], 0.65)
    accent = ACCENT[kind]
    radius = max(2.0, half)
    height = [[0.0] * size for _ in range(size)]
    for y in range(size):
        for x in range(size):
            if dist[y][x] > 0:
                dd = min(dist[y][x] - 0.5, radius)
                t = 1 - dd / radius
                height[y][x] = math.sqrt(max(0.0, 1 - t * t)) * radius * 0.9

    light = (-0.5, -0.6, 0.62)
    ln = math.sqrt(sum(v * v for v in light))
    light = tuple(v / ln for v in light)
    hv = (light[0], light[1], light[2] + 1)
    hn = math.sqrt(sum(v * v for v in hv))
    hv = tuple(v / hn for v in hv)
    mp = mask.load()
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ip = img.load()
    for y in range(1, size - 1):
        for x in range(1, size - 1):
            if mp[x, y] == 0:
                continue
            gx = (height[y][x + 1] - height[y][x - 1]) / 2.0
            gy = (height[y + 1][x] - height[y - 1][x]) / 2.0
            nx, ny, nz = -gx, -gy, 1.0
            nn = math.sqrt(nx * nx + ny * ny + nz * nz)
            nx, ny, nz = nx / nn, ny / nn, nz / nn
            lam = max(0.0, nx * light[0] + ny * light[1] + nz * light[2])
            spec = max(0.0, nx * hv[0] + ny * hv[1] + nz * hv[2]) ** 24
            col = body
            lx, ly = x - pad, y - pad
            horizontal = abs(y - c) <= half
            vertical = abs(x - c) <= half
            away = max(abs(x - c), abs(y - c))
            if kind != "control" and away > half + 1:
                along = lx if horizontal and not vertical else ly
                if 12 <= along <= 14 or 18 <= along <= 20:
                    col = accent if kind != "steam" else mix(body, accent, 0.8)
            k = 0.32 + 0.98 * lam
            col = shade(col, k)
            if spec > 0.02:
                col = mix(col, (255, 255, 255), min(0.5, spec * (0.5 if kind != "steam" else 0.7)))
            if dist[y][x] == 1:
                col = shade(col, 0.45)
            ip[x, y] = col + (255,)

    d = ImageDraw.Draw(img)
    if kind in ("steam", "coolant"):
        for p_ in (pad + 6, pad + 25):
            if "E" in arms and p_ >= c or "W" in arms and p_ <= c:
                d.line((p_, lo + 1, p_, hi - 1), fill=shade(body, 0.45))
                d.line((p_ + 1, lo + 1, p_ + 1, hi - 1), fill=shade(body, 1.15))
            if "S" in arms and p_ >= c or "N" in arms and p_ <= c:
                d.line((lo + 1, p_, hi - 1, p_), fill=shade(body, 0.45))
                d.line((lo + 1, p_ + 1, hi - 1, p_ + 1), fill=shade(body, 1.15))
    collar = int(half) + 2
    cc = shade(body, 0.85)
    for arm in arms:
        if arm == "W":
            d.rectangle((pad, c - collar, pad + 1, c + collar - 1), fill=cc, outline=shade(body, 0.35))
            d.point((pad + 1, c - collar + 1), fill=STEEL_XL)
        elif arm == "E":
            d.rectangle((pad + 30, c - collar, pad + 31, c + collar - 1), fill=cc, outline=shade(body, 0.35))
            d.point((pad + 30, c - collar + 1), fill=STEEL_XL)
        elif arm == "N":
            d.rectangle((c - collar, pad, c + collar - 1, pad + 1), fill=cc, outline=shade(body, 0.35))
            d.point((c - collar + 1, pad + 1), fill=STEEL_XL)
        elif arm == "S":
            d.rectangle((c - collar, pad + 30, c + collar - 1, pad + 31), fill=cc, outline=shade(body, 0.35))
            d.point((c - collar + 1, pad + 30), fill=STEEL_XL)
    if len(arms) >= 3 or not arms:
        circle(d, c, c, 2, shade(body, 0.6), OUTLINE)
        d.point((c - 1, c - 1), fill=STEEL_XL)

    out = img.crop((pad, pad, pad + 32, pad + 32))
    final = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    stack(final, out, 2, 3, 105, clip=False)
    return final


def make_pipes():
    for kind in ("coolant", "steam", "feed", "cooling", "control"):
        r = Rsi("pipe_" + kind)
        for flags in range(16):
            r.add("%s%d" % (kind, flags), pipe_frame(kind, flags))
        r.save()


def fuel_frame(heu):
    im = new()
    tag = (176, 60, 54) if heu else (70, 160, 96)
    base = new()
    body = new()
    d = ImageDraw.Draw(body)
    d.rectangle((6, 2, 25, 5), fill=(120, 128, 140), outline=OUTLINE)
    d.line((7, 3, 24, 3), fill=STEEL_XL)
    for x in (9, 15, 21):
        d.line((x, 5, x, 7), fill=STEEL_XD)
    d.rectangle((6, 26, 25, 29), fill=(104, 112, 124), outline=OUTLINE)
    d.rectangle((7, 29, 9, 30), fill=STEEL_D)
    d.rectangle((22, 29, 24, 30), fill=STEEL_D)
    for i, x in enumerate((8, 11, 14, 17, 20, 23)):
        cyl_v(d, (x - 1, 7, x + 1, 25), (150, 156, 168), 0.45, 1.4)
        d.rectangle((x - 1, 11, x + 1, 22), fill=mix((176, 168, 90), tag, 0.2))
        d.line((x - 1, 11, x - 1, 22), fill=(214, 206, 130))
    for y in (9, 16, 24):
        d.rectangle((6, y, 25, y + 1), fill=(84, 90, 102), outline=OUTLINE)
        d.line((7, y, 24, y), fill=STEEL_L)
    d.rectangle((7, 13, 24, 15), fill=tag, outline=OUTLINE)
    d.line((8, 13, 23, 13), fill=shade(tag, 1.4))
    stack(base, body, 2, 2, 110, clip=False)
    return base


def make_fuel():
    r = Rsi("fuel")
    r.add("fuel", fuel_frame(False))
    r.add("fuel_heu", fuel_frame(True))
    r.save()


def wreck_frame(f):
    W = H = 160
    im = new(W, H)
    k = 160 / 96.0

    def sc(pts):
        return [(x * k, y * k) for x, y in pts]

    d = ImageDraw.Draw(im)
    d.polygon(sc([(8, 70), (14, 36), (36, 14), (62, 16), (84, 38), (88, 72), (66, 88), (28, 88)]), fill=(38, 36, 34), outline=OUTLINE)
    d.polygon(sc([(22, 66), (28, 42), (46, 28), (66, 34), (76, 56), (64, 76), (36, 78)]), fill=(78, 70, 62))
    rnd = random.Random(3)
    for _ in range(260):
        x, y = rnd.randint(14, 146), rnd.randint(20, 146)
        if im.getpixel((x, y))[3] > 0:
            d.point((x, y), fill=shade((70, 64, 58), rnd.choice((0.6, 0.8, 1.2, 1.4))))
    gl = [(255, 150, 50), (255, 190, 80)][f % 2]
    glow(im, 80, 90, 44, gl, 130)
    d = ImageDraw.Draw(im)
    d.polygon(sc([(34, 58), (40, 44), (56, 42), (66, 56), (54, 68)]), fill=gl)
    d.point((80, 86), fill=(255, 255, 200))
    d.line((23, 73, 57, 100), fill=(110, 112, 118), width=3)
    d.line((117, 43, 137, 90), fill=(110, 112, 118), width=3)
    for x, y in ((20, 133), (133, 120), (33, 40), (123, 30)):
        d.point((x, y), fill=GREEN)
    d.polygon(sc([(6, 60), (2, 74), (14, 78)]), fill=STEEL_D, outline=OUTLINE)
    d.polygon(sc([(80, 82), (90, 76), (88, 90)]), fill=STEEL, outline=OUTLINE)
    return im


def make_wreck():
    r = Rsi("wreck", 160, 160)
    r.add("wreck", [wreck_frame(i) for i in range(2)], 0.6)
    r.save()


if __name__ == "__main__":
    make_core()
    make_pumps()
    make_sg()
    make_turbine()
    make_gen()
    make_cond()
    make_pool()
    make_pipes()
    make_console()
    make_fuel()
    make_wreck()
