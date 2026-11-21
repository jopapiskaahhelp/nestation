# Nestation CC BY-NC 3.0
import json
import math
import os
import sys

from PIL import Image, ImageDraw

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
    "coolant": (232, 128, 48),
    "steam": (226, 232, 238),
    "feed": (64, 132, 232),
    "cooling": (44, 196, 176),
    "shaft": (150, 156, 168),
    "control": (80, 200, 100),
}


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


def nozzle(d, w, h, off, side, medium, length=18, thick=8):
    cx, cy = tile_px(w, h, off)
    c = MEDIUM[medium]
    dark = shade(c, 0.5)
    light = shade(c, 1.25) if medium != "steam" else WHITE
    half = thick // 2
    cx, cy = int(cx), int(cy)
    if side == "E":
        box = (cx + 16 - length, cy - half, cx + 15, cy + half - 1)
        flange = (cx + 12, cy - half - 2, cx + 15, cy + half + 1)
    elif side == "W":
        box = (cx - 16, cy - half, cx - 16 + length - 1, cy + half - 1)
        flange = (cx - 16, cy - half - 2, cx - 13, cy + half + 1)
    elif side == "S":
        box = (cx - half, cy + 16 - length, cx + half - 1, cy + 15)
        flange = (cx - half - 2, cy + 12, cx + half + 1, cy + 15)
    else:
        box = (cx - half, cy - 16, cx + half - 1, cy - 16 + length - 1)
        flange = (cx - half - 2, cy - 16, cx + half + 1, cy - 13)
    d.rectangle(box, fill=c, outline=dark)
    d.rectangle(flange, fill=shade(c, 0.8), outline=dark)
    if side in ("E", "W"):
        d.line((box[0] + 1, box[1] + 1, box[2] - 1, box[1] + 1), fill=light)
    else:
        d.line((box[0] + 1, box[1] + 1, box[0] + 1, box[3] - 1), fill=light)


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


def core_frame(state, f):
    W = H = 96
    im = new(W, H)
    d = ImageDraw.Draw(im)
    bevel(d, (2, 2, 93, 93), CONCRETE_D, light=CONCRETE_L, dark=STEEL_XD)
    d.rectangle((6, 6, 89, 89), outline=STEEL_XD)
    for x, y in ((9, 9), (86, 9), (9, 86), (86, 86)):
        circle(d, x, y, 2, STEEL_L, OUTLINE)
    circle(d, 48, 48, 42, STEEL_XD, OUTLINE)
    for r in range(40, 30, -1):
        t = (40 - r) / 10
        circle(d, 48, 48, r, mix(STEEL_D, STEEL_L, 0.5 + 0.5 * math.sin(math.pi * t)), None)
    d.arc((9, 9, 87, 87), 200, 280, fill=STEEL_XL)
    for k in range(24):
        a = math.radians(k * 15)
        circle(d, 48 + 35 * math.cos(a), 48 - 35 * math.sin(a), 1, STEEL_XD, OUTLINE)
    circle(d, 48, 48, 29, STEEL_XD, STEEL_L)
    circle(d, 48, 48, 27, shade(STEEL_D, 0.8))
    pulse = [0.7, 0.85, 1.0, 0.85][f % 4]
    if state == "off":
        gl = (44, 56, 66)
    elif state == "run":
        gl = shade(CYAN, pulse)
    elif state == "hot":
        gl = mix((255, 80, 30), (255, 210, 80), (pulse - 0.7) / 0.3)
    elif state == "alarm":
        gl = RED if f % 2 == 0 else shade(RED, 0.35)
    else:
        gl = (12, 12, 14)
    if state in ("run", "hot", "alarm"):
        glow(im, 48, 48, 26, gl, 110)
    d = ImageDraw.Draw(im)
    for gx in range(-4, 5):
        for gy in range(-4, 5):
            x, y = 48 + gx * 6, 48 + gy * 6
            rr = math.hypot(gx, gy) * 6
            if rr > 23 or rr < 9:
                continue
            lit = state in ("run", "hot") and (gx + gy + f) % 5 == 0
            circle(d, x, y, 2, gl if lit else (26, 30, 38), STEEL_L)
    circle(d, 48, 48, 8, shade(gl, 0.55), gl)
    circle(d, 48, 48, 5, gl)
    if state == "broken":
        for pts in (((22, 24), (40, 42), (48, 48)), ((48, 48), (64, 40), (74, 28)), ((48, 48), (44, 66), (52, 78))):
            d.line(pts, fill=(88, 88, 92), width=2)
        d.polygon([(70, 60), (78, 66), (72, 74), (66, 68)], fill=(20, 20, 22))
    trefoil(d, 15, 80, 6)
    lit = state in ("run", "hot")
    for i, (col, on) in enumerate(((GREEN, lit), (YELLOW, state == "hot"), (RED, state == "alarm" and f % 2 == 0))):
        lamp(d, 70 + i * 6, 82, col, on)
        d.rectangle((69 + i * 6, 81, 72 + i * 6, 84), outline=OUTLINE)
    if state == "alarm":
        for i in range(0, 40, 8):
            d.polygon([(28 + i, 91), (32 + i, 91), (36 + i, 87), (32 + i, 87)], fill=YELLOW)
    nozzle(d, 3, 3, (1, 0), "E", "coolant", 26)
    nozzle(d, 3, 3, (-1, 0), "W", "coolant", 26)
    return im


def make_core():
    r = Rsi("core", 96, 96)
    r.add("core_off", core_frame("off", 0))
    r.add("core_run", [core_frame("run", i) for i in range(4)], 0.25)
    r.add("core_hot", [core_frame("hot", i) for i in range(4)], 0.18)
    r.add("core_alarm", [core_frame("alarm", i) for i in range(2)], 0.3)
    r.add("core_broken", core_frame("broken", 0))
    r.save()


def pump_frame(kind, on, f):
    medium = {"primary": "coolant", "feed": "feed", "circ": "cooling"}[kind]
    flow = "W"
    c = MEDIUM[medium]
    im = new(96, 96)
    d = ImageDraw.Draw(im)
    bevel(d, (4, 30, 91, 92), STEEL_D)
    d.rectangle((8, 34, 87, 88), outline=STEEL_XD)
    for x in (10, 85):
        for y in (36, 86):
            circle(d, x, y, 2, STEEL_L, OUTLINE)
    circle(d, 48, 60, 24, STEEL_D, OUTLINE)
    for r in range(22, 14, -1):
        circle(d, 48, 60, r, mix(STEEL, STEEL_XL, (22 - r) / 8 * 0.4), None)
    circle(d, 48, 60, 14, STEEL_XD, STEEL_L)
    ang = math.radians(f * 30) if on else 0.0
    for k in range(4):
        a = ang + k * math.pi / 2
        d.line((48, 60, 48 + 12 * math.cos(a), 60 + 12 * math.sin(a)), fill=c if on else shade(c, 0.5), width=3)
    circle(d, 48, 60, 4, STEEL_XL, OUTLINE)
    cyl_v(d, (30, 4, 66, 34), STEEL, 0.5, 1.2)
    for x in range(33, 66, 4):
        d.line((x, 6, x, 32), fill=shade(STEEL, 0.65))
    d.rectangle((30, 16, 66, 22), fill=shade(c, 0.9 if on else 0.45), outline=OUTLINE)
    lamp(d, 36, 8, GREEN, on)
    lamp(d, 58, 8, RED, False)
    if flow == "W":
        d.polygon([(16, 78), (26, 71), (26, 85)], fill=c, outline=OUTLINE)
        d.rectangle((26, 76, 44, 80), fill=c, outline=OUTLINE)
    else:
        d.polygon([(80, 78), (70, 71), (70, 85)], fill=c, outline=OUTLINE)
        d.rectangle((52, 76, 70, 80), fill=c, outline=OUTLINE)
    nozzle(d, 3, 3, (-1, 0), "W", medium, 26, 10)
    nozzle(d, 3, 3, (1, 0), "E", medium, 26, 10)
    return im


def make_pumps():
    r = Rsi("pumps", 96, 96)
    for kind in ("primary", "feed", "circ"):
        r.add(kind + "_off", pump_frame(kind, False, 0))
        r.add(kind + "_on", [pump_frame(kind, True, i) for i in range(4)], 0.08)
    r.save()


def sg_frame(state, f):
    W, H = 96, 160
    im = new(W, H)
    d = ImageDraw.Draw(im)
    nozzle(d, 3, 5, (-1, 1), "W", "coolant", 24, 10)
    nozzle(d, 3, 5, (-1, -1), "W", "coolant", 24, 10)
    nozzle(d, 3, 5, (0, 2), "N", "steam", 26, 10)
    nozzle(d, 3, 5, (0, -2), "S", "feed", 26, 10)
    for y in (34, 124):
        bevel(d, (12, y, 84, y + 8), STEEL_XD, light=STEEL, dark=OUTLINE)
    cyl_v(d, (20, 16, 76, 144), STEEL, 0.5, 1.25)
    d.ellipse((20, 8, 76, 26), fill=STEEL_L, outline=OUTLINE)
    d.ellipse((20, 134, 76, 152), fill=STEEL_D, outline=OUTLINE)
    d.arc((22, 10, 74, 24), 200, 340, fill=STEEL_XL)
    for y in (42, 76, 112):
        d.line((20, y, 76, y), fill=STEEL_XD)
        d.line((20, y + 1, 76, y + 1), fill=STEEL_L)
        for x in range(24, 74, 6):
            d.point((x, y + 3), fill=STEEL_XD)
    d.line((26, 18, 26, 142), fill=STEEL_XL)
    circle(d, 48, 60, 9, STEEL_D, OUTLINE)
    circle(d, 48, 60, 6, STEEL_XD, STEEL_L)
    for k in range(8):
        a = math.radians(k * 45)
        d.point((48 + 8 * math.cos(a), 60 + 8 * math.sin(a)), fill=STEEL_XL)
    circle(d, 48, 98, 8, (20, 24, 30), STEEL_L)
    ang = {"off": -2.2, "run": -0.9 + 0.1 * (f % 4), "hot": 0.5 + 0.08 * (f % 4)}[state]
    d.line((48, 98, 48 + 6 * math.cos(ang), 98 + 6 * math.sin(ang)), fill=RED if state == "hot" else WHITE)
    d.rectangle((34, 120, 62, 130), fill=(20, 28, 36), outline=STEEL_L)
    fill = {"off": (46, 60, 78), "run": (90, 150, 210), "hot": (240, 120, 60)}[state]
    d.rectangle((36, 124, 60, 128), fill=fill)
    if state != "off":
        for k in range(5):
            by = 118 - ((f * 3 + k * 5) % 18)
            d.point((36 + k * 5, by), fill=WHITE)
    if state == "hot":
        glow(im, 48, 80, 40, (255, 90, 40), 70)
    d = ImageDraw.Draw(im)
    lamp(d, 28, 20, GREEN, state != "off")
    lamp(d, 64, 20, RED, state == "hot")
    return im


def make_sg():
    r = Rsi("steamgen", 96, 160)
    r.add("sg_off", sg_frame("off", 0))
    r.add("sg_run", [sg_frame("run", i) for i in range(4)], 0.2)
    r.add("sg_hot", [sg_frame("hot", i) for i in range(4)], 0.15)
    r.save()


def turbine_frame(on, hot, f):
    W, H = 224, 96
    im = new(W, H)
    d = ImageDraw.Draw(im)
    nozzle(d, 7, 3, (-2, -1), "S", "steam", 34, 8)
    nozzle(d, 7, 3, (2, -1), "S", "steam", 28, 12)
    d.rectangle((188, 44, 223, 51), fill=STEEL_L, outline=OUTLINE)
    for x in (196, 208, 218):
        d.rectangle((x, 42, x + 2, 53), fill=YELLOW if x != 208 else STEEL_XD, outline=OUTLINE)
    bevel(d, (186, 34, 204, 62), STEEL_D)
    cyl_h(d, (22, 30, 92, 66), STEEL, 0.5, 1.2)
    cyl_h(d, (92, 20, 188, 76), STEEL, 0.5, 1.2)
    d.polygon([(92, 30), (104, 20), (104, 76), (92, 66)], fill=STEEL_D, outline=OUTLINE)
    d.rectangle((14, 38, 24, 58), fill=STEEL_D, outline=OUTLINE)
    d.line((22, 48, 186, 48), fill=shade(STEEL_XD, 0.8))
    for x in range(28, 186, 10):
        d.point((x, 33 if x < 92 else 23), fill=STEEL_XL)
        d.point((x, 63 if x < 92 else 73), fill=STEEL_XD)
    for cx, cy, r in ((56, 48, 9), (122, 48, 13), (156, 48, 13)):
        circle(d, cx, cy, r + 2, STEEL_D, OUTLINE)
        circle(d, cx, cy, r, (14, 18, 24), STEEL_L)
        ang = math.radians(f * 15) if on else 0.0
        col = ORANGE if hot else STEEL_XL
        for k in range(8):
            a = ang + k * math.pi / 4
            d.line((cx, cy, cx + (r - 1) * math.cos(a), cy + (r - 1) * math.sin(a)), fill=col)
        circle(d, cx, cy, 2, STEEL_XL, OUTLINE)
    lamp(d, 26, 24, GREEN, on)
    lamp(d, 32, 24, RED, hot)
    return im


def make_turbine():
    r = Rsi("turbine", 224, 96)
    r.add("turbine_off", turbine_frame(False, False, 0))
    r.add("turbine_run", [turbine_frame(True, False, i) for i in range(6)], 0.06)
    r.add("turbine_hot", [turbine_frame(True, True, i) for i in range(6)], 0.04)
    r.save()


def gen_frame(on, f):
    W, H = 160, 96
    im = new(W, H)
    d = ImageDraw.Draw(im)
    d.rectangle((0, 44, 36, 51), fill=STEEL_L, outline=OUTLINE)
    for x in (4, 14):
        d.rectangle((x, 42, x + 2, 53), fill=YELLOW, outline=OUTLINE)
    bevel(d, (22, 30, 40, 66), STEEL_D)
    cyl_h(d, (38, 18, 138, 78), (150, 156, 168), 0.45, 1.2)
    for x in range(44, 136, 6):
        d.line((x, 20, x, 76), fill=shade(STEEL, 0.55))
    d.rectangle((38, 44, 138, 52), fill=COPPER, outline=shade(COPPER, 0.5))
    d.line((38, 46, 138, 46), fill=shade(COPPER, 1.3))
    bevel(d, (138, 28, 154, 68), STEEL_D)
    for y in (34, 44, 54, 62):
        d.line((140, y, 152, y), fill=STEEL_XD)
    bevel(d, (66, 4, 112, 20), STEEL, light=STEEL_XL)
    for x in (74, 88, 102):
        d.rectangle((x - 2, 8, x + 2, 24), fill=WHITE, outline=OUTLINE)
        d.line((x, 24, x, 30), fill=COPPER, width=2)
    d.polygon([(89, 6), (82, 17), (96, 17)], fill=YELLOW, outline=OUTLINE)
    d.point((89, 13), fill=OUTLINE)
    lamp(d, 44, 22, GREEN, on)
    lamp(d, 50, 22, YELLOW, on and f % 2 == 0)
    lamp(d, 56, 22, RED, False)
    if on:
        sx = 40 + (f * 23) % 96
        d.line((sx, 70, sx + 4, 72), fill=(255, 245, 150))
        d.point((sx + 2, 71), fill=CYAN)
    return im


def make_gen():
    r = Rsi("generator", 160, 96)
    r.add("gen_off", gen_frame(False, 0))
    r.add("gen_on", [gen_frame(True, i) for i in range(4)], 0.12)
    r.save()


def cond_frame(on, f):
    W, H = 160, 96
    im = new(W, H)
    d = ImageDraw.Draw(im)
    nozzle(d, 5, 3, (0, 1), "N", "steam", 22, 12)
    nozzle(d, 5, 3, (-2, -1), "S", "feed", 22, 8)
    nozzle(d, 5, 3, (2, 1), "E", "cooling", 20, 8)
    nozzle(d, 5, 3, (2, -1), "E", "cooling", 20, 8)
    bevel(d, (8, 12, 146, 84), STEEL_D)
    cyl_h(d, (24, 16, 130, 80), STEEL, 0.5, 1.2)
    for x in (24, 130):
        d.rectangle((x - 2, 14, x + 2, 82), fill=STEEL_D, outline=OUTLINE)
    bevel(d, (10, 14, 22, 82), STEEL_XD, light=STEEL)
    bevel(d, (132, 14, 144, 82), STEEL_XD, light=STEEL)
    d.rectangle((36, 28, 118, 68), fill=(16, 28, 38), outline=STEEL_XL)
    water = (40, 150, 170) if on else (30, 70, 84)
    d.rectangle((37, 50, 117, 67), fill=water)
    for row in range(5):
        for col in range(20):
            x, y = 40 + col * 4, 31 + row * 4
            d.rectangle((x, y, x + 1, y + 1), fill=STEEL_XL if (row + col) % 2 else STEEL_L)
    if on:
        for k in range(6):
            d.point((42 + k * 13, 66 - ((f * 3 + k * 3) % 14)), fill=WHITE)
    for x in (14, 138):
        for y in (22, 74):
            circle(d, x, y, 1, STEEL_L, OUTLINE)
    lamp(d, 28, 18, GREEN, on)
    lamp(d, 34, 18, RED, False)
    return im


def make_cond():
    r = Rsi("condenser", 160, 96)
    r.add("cond_off", cond_frame(False, 0))
    r.add("cond_run", [cond_frame(True, i) for i in range(4)], 0.2)
    r.save()


def console_frame(state, f):
    im = new(96, 32)
    d = ImageDraw.Draw(im)
    bevel(d, (1, 14, 94, 30), STEEL_D)
    for i in range(3):
        x0 = 4 + i * 30
        bevel(d, (x0, 1, x0 + 26, 17), STEEL, light=STEEL_XL)
        scr = (8, 38, 30)
        if state == "off":
            scr = (14, 18, 20)
        elif state == "alarm" and f % 2 == 0:
            scr = (70, 14, 12)
        d.rectangle((x0 + 2, 3, x0 + 24, 15), fill=scr, outline=OUTLINE)
        if state == "on":
            if i == 0:
                pts = [(x0 + 3 + j * 2, 10 + int(2.5 * math.sin((j + f) * 0.9))) for j in range(10)]
                d.line(pts, fill=GREEN)
            elif i == 1:
                for j in range(4):
                    d.line((x0 + 4, 5 + j * 3, x0 + 4 + (6 + (f * 3 + j * 5) % 14), 5 + j * 3), fill=(CYAN, YELLOW)[j % 2])
            else:
                circle(d, x0 + 13, 9, 4, None, GREEN)
                d.line((x0 + 13, 9, x0 + 13 + 4 * math.cos(f), 9 + 4 * math.sin(f)), fill=GREEN)
        elif state == "alarm":
            d.line((x0 + 4, 5, x0 + 22, 14), fill=RED if f % 2 == 0 else YELLOW)
            d.line((x0 + 22, 5, x0 + 4, 14), fill=RED if f % 2 == 0 else YELLOW)
    for row in range(2):
        for col in range(18):
            d.rectangle((6 + col * 4, 18 + row * 4, 8 + col * 4, 19 + row * 4), fill=STEEL_XL if (row + col) % 2 else STEEL_L)
    circle(d, 86, 22, 4, RED, shade(RED, 0.5))
    d.point((85, 21), fill=(255, 170, 160))
    return im


def make_console():
    r = Rsi("console", 96, 32)
    r.add("console_off", console_frame("off", 0))
    r.add("console_on", [console_frame("on", i) for i in range(4)], 0.3)
    r.add("console_alarm", [console_frame("alarm", i) for i in range(2)], 0.3)
    r.save()


def pool_frame(flags, f):
    n, s, e, w = flags & 1, flags & 2, flags & 4, flags & 8
    im = Image.new("RGBA", (S, S), CONCRETE + (255,))
    d = ImageDraw.Draw(im)
    x0 = 0 if w else 3
    x1 = S - 1 if e else S - 4
    y0 = 0 if n else 3
    y1 = S - 1 if s else S - 4
    for y in range(y0, y1 + 1):
        t = (y - y0) / max(1, y1 - y0)
        base = mix((54, 138, 196), (28, 92, 150), t)
        d.line((x0, y, x1, y), fill=base)
    for k in range(5):
        px = (k * 7 + f * 3) % (x1 - x0 + 1) + x0
        py = (k * 11 + f * 2) % (y1 - y0 + 1) + y0
        d.line((px, py, min(x1, px + 3), py), fill=(130, 200, 236))
    if not n:
        d.line((0, 0, S - 1, 0), fill=CONCRETE_L)
        d.line((0, 2, S - 1, 2), fill=CONCRETE_D)
    if not s:
        d.line((0, S - 1, S - 1, S - 1), fill=CONCRETE_D)
        d.line((0, S - 3, S - 1, S - 3), fill=CONCRETE_L)
    if not w:
        d.line((0, 0, 0, S - 1), fill=CONCRETE_L)
        d.line((2, 0, 2, S - 1), fill=CONCRETE_D)
    if not e:
        d.line((S - 1, 0, S - 1, S - 1), fill=CONCRETE_D)
        d.line((S - 3, 0, S - 3, S - 1), fill=CONCRETE_L)
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
        r.add("pool%d" % flags, [pool_frame(flags, i) for i in range(3)], 0.45)
    r.add("steam", [steam_frame(i) for i in range(4)], 0.25)
    r.save()


def pipe_frame(kind, flags):
    c = MEDIUM[kind]
    thick = kind != "control"
    half = 4 if thick else 2
    im = new()
    d = ImageDraw.Draw(im)
    lo, hi = 16 - half, 16 + half - 1
    dark = shade(c, 0.45)
    light = shade(c, 1.3) if kind != "steam" else WHITE
    arms = []
    if flags & 1:
        arms.append((lo, 0, hi, 16))
    if flags & 2:
        arms.append((lo, 16, hi, S - 1))
    if flags & 4:
        arms.append((16, lo, S - 1, hi))
    if flags & 8:
        arms.append((0, lo, 16, hi))
    for a in arms:
        d.rectangle(a, fill=c, outline=dark)
    d.rectangle((lo, lo, hi, hi), fill=c, outline=dark)
    for a in arms:
        d.rectangle((a[0] + 1, a[1] + 1, a[2] - 1, a[3] - 1), fill=c)
    d.rectangle((lo + 1, lo + 1, hi - 1, hi - 1), fill=c)
    if thick:
        if flags & 3:
            d.line((lo + 1, 0 if flags & 1 else lo, lo + 1, S - 1 if flags & 2 else hi), fill=light)
        if flags & 12:
            d.line((0 if flags & 8 else lo, lo + 1, S - 1 if flags & 4 else hi, lo + 1), fill=light)
        if kind == "steam":
            for a in arms:
                if a[0] == lo:
                    for yy in (6, 25):
                        if a[1] <= yy <= a[3]:
                            d.line((lo, yy, hi, yy), fill=RED)
                else:
                    for xx in (6, 25):
                        if a[0] <= xx <= a[2]:
                            d.line((xx, lo, xx, hi), fill=RED)
        d.rectangle((lo - 1, lo - 1, hi + 1, hi + 1), outline=dark)
        d.point((lo + 1, lo + 1), fill=light)
    else:
        d.point((lo + 1, lo), fill=light)
    return im


def make_pipes():
    for kind in ("coolant", "steam", "feed", "cooling", "control"):
        r = Rsi("pipe_" + kind)
        for flags in range(16):
            r.add("%s%d" % (kind, flags), pipe_frame(kind, flags))
        r.save()


def fuel_frame(heu):
    im = new()
    d = ImageDraw.Draw(im)
    tag = RED if heu else GREEN
    d.rectangle((5, 2, 26, 4), fill=STEEL_L, outline=OUTLINE)
    d.rectangle((5, 27, 26, 29), fill=STEEL_L, outline=OUTLINE)
    for x in (8, 12, 16, 20, 24):
        d.rectangle((x - 1, 5, x + 1, 26), fill=STEEL, outline=OUTLINE)
        d.rectangle((x, 6, x, 25), fill=STEEL_XL)
        d.rectangle((x - 1, 9, x + 1, 21), fill=mix(YELLOW, tag, 0.4), outline=shade(tag, 0.5))
    d.rectangle((7, 13, 25, 16), fill=tag, outline=OUTLINE)
    d.line((8, 14, 24, 14), fill=WHITE)
    return im


def make_fuel():
    r = Rsi("fuel")
    r.add("fuel", fuel_frame(False))
    r.add("fuel_heu", fuel_frame(True))
    r.save()


def wreck_frame(f):
    im = new(96, 96)
    d = ImageDraw.Draw(im)
    d.polygon([(8, 70), (14, 36), (36, 14), (62, 16), (84, 38), (88, 72), (66, 88), (28, 88)], fill=(38, 36, 34), outline=OUTLINE)
    d.polygon([(22, 66), (28, 42), (46, 28), (66, 34), (76, 56), (64, 76), (36, 78)], fill=(78, 70, 62))
    gl = [(255, 150, 50), (255, 190, 80)][f % 2]
    glow(im, 48, 54, 26, gl, 120)
    d = ImageDraw.Draw(im)
    d.polygon([(34, 58), (40, 44), (56, 42), (66, 56), (54, 68)], fill=gl)
    d.point((48, 52), fill=(255, 255, 200))
    d.line((14, 44, 34, 60), fill=(110, 112, 118), width=2)
    d.line((70, 26, 82, 54), fill=(110, 112, 118), width=2)
    for x, y in ((12, 80), (80, 72), (20, 24), (74, 18)):
        d.point((x, y), fill=GREEN)
    d.polygon([(6, 60), (2, 74), (14, 78)], fill=STEEL_D, outline=OUTLINE)
    return im


def make_wreck():
    r = Rsi("wreck", 96, 96)
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
