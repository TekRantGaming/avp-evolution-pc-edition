# Draws the controller glyphs for the PC port (original art, no third-party images).
#   PCGlyphs.png       128 px cells, for the HUD prompts and the F1 screen
#   PCGlyphsSmall.png   24 px cells, patched into the game's font atlas for inline tips
# The order is PCGlyphs.Id in PCGlyphs.cs.
import os

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "unity", "Assets", "PCPort", "Resources")
SS = 8
CELL = 128
BIG = CELL * SS
COLUMNS = 8

DARK = (24, 26, 32, 240)
RIM = (214, 220, 230, 255)
LIGHT = (235, 238, 244, 255)
GREEN = (102, 196, 74, 255)
RED = (232, 74, 66, 255)
BLUE = (66, 142, 240, 255)
YELLOW = (248, 200, 56, 255)
PS_BLUE = (120, 170, 245, 255)
PS_PINK = (236, 130, 196, 255)
PS_GREEN = (80, 210, 170, 255)


def font(size):
    return ImageFont.load_default(size=size)


def label(d, text, size, fill, cx=BIG / 2, cy=BIG / 2):
    f = font(size)
    box = d.textbbox((0, 0), text, font=f, stroke_width=size // 22)
    x = cx - (box[0] + box[2]) / 2
    y = cy - (box[1] + box[3]) / 2
    d.text((x, y), text, font=f, fill=fill, stroke_width=size // 22, stroke_fill=fill)


def disc(d, ring=LIGHT):
    m = BIG * 0.05
    d.ellipse((m, m, BIG - m, BIG - m), fill=DARK, outline=ring, width=int(BIG * 0.045))


def face(letter, colour):
    def draw(d):
        disc(d, RIM)
        label(d, letter, int(BIG * 0.56), colour)
    return draw


def shoulder(text):
    def draw(d):
        d.rounded_rectangle((BIG * 0.05, BIG * 0.2, BIG * 0.95, BIG * 0.8), BIG * 0.2, fill=DARK, outline=RIM, width=int(BIG * 0.04))
        label(d, text, int(BIG * 0.38), LIGHT)
    return draw


def trigger(text):
    def draw(d):
        d.rounded_rectangle((BIG * 0.16, BIG * 0.06, BIG * 0.84, BIG * 0.94), BIG * 0.26, fill=DARK, outline=RIM, width=int(BIG * 0.04))
        d.rectangle((BIG * 0.22, BIG * 0.66, BIG * 0.78, BIG * 0.9), fill=(48, 52, 62, 255))
        label(d, text, int(BIG * 0.36), LIGHT, cy=BIG * 0.42)
    return draw


def stick(letter, click=False):
    def draw(d):
        disc(d, RIM)
        m = BIG * 0.2
        d.ellipse((m, m, BIG - m, BIG - m), fill=(48, 52, 62, 255))
        label(d, letter, int(BIG * 0.42), LIGHT)
        if click:
            s = BIG * 0.1
            for sign in (-1, 1):
                x = BIG / 2 + sign * BIG * 0.385
                d.polygon([(x - s * 0.8, BIG * 0.14), (x + s * 0.8, BIG * 0.14), (x, BIG * 0.27)], fill=YELLOW)
    return draw


def dpad(arm=None):
    def draw(d):
        w = BIG * 0.3
        a = BIG * 0.05
        b = BIG * 0.95
        low = BIG / 2 - w / 2
        high = BIG / 2 + w / 2
        d.rounded_rectangle((low, a, high, b), BIG * 0.05, fill=DARK, outline=RIM, width=int(BIG * 0.035))
        d.rounded_rectangle((a, low, b, high), BIG * 0.05, fill=DARK, outline=RIM, width=int(BIG * 0.035))
        d.rectangle((low + BIG * 0.03, low + BIG * 0.03, high - BIG * 0.03, high - BIG * 0.03), fill=DARK)
        arms = {
            "up": (low + BIG * 0.04, a + BIG * 0.04, high - BIG * 0.04, low),
            "down": (low + BIG * 0.04, high, high - BIG * 0.04, b - BIG * 0.04),
            "left": (a + BIG * 0.04, low + BIG * 0.04, low, high - BIG * 0.04),
            "right": (high, low + BIG * 0.04, b - BIG * 0.04, high - BIG * 0.04),
        }
        for name, box in arms.items():
            if name == arm:
                d.rectangle(box, fill=YELLOW)
    return draw


def menu(d):
    disc(d, RIM)
    for y in (0.36, 0.5, 0.64):
        d.rounded_rectangle((BIG * 0.3, BIG * (y - 0.03), BIG * 0.7, BIG * (y + 0.03)), BIG * 0.02, fill=LIGHT)


def view(d):
    disc(d, RIM)
    d.rounded_rectangle((BIG * 0.32, BIG * 0.3, BIG * 0.6, BIG * 0.58), BIG * 0.05, outline=LIGHT, width=int(BIG * 0.04))
    d.rounded_rectangle((BIG * 0.42, BIG * 0.42, BIG * 0.7, BIG * 0.7), BIG * 0.05, fill=DARK, outline=LIGHT, width=int(BIG * 0.04))


def cross(d):
    disc(d, RIM)
    w = int(BIG * 0.075)
    d.line((BIG * 0.3, BIG * 0.3, BIG * 0.7, BIG * 0.7), fill=PS_BLUE, width=w)
    d.line((BIG * 0.7, BIG * 0.3, BIG * 0.3, BIG * 0.7), fill=PS_BLUE, width=w)


def circle(d):
    disc(d, RIM)
    d.ellipse((BIG * 0.28, BIG * 0.28, BIG * 0.72, BIG * 0.72), outline=RED, width=int(BIG * 0.07))


def square(d):
    disc(d, RIM)
    d.rectangle((BIG * 0.3, BIG * 0.3, BIG * 0.7, BIG * 0.7), outline=PS_PINK, width=int(BIG * 0.07))


def triangle(d):
    disc(d, RIM)
    d.polygon([(BIG * 0.5, BIG * 0.24), (BIG * 0.76, BIG * 0.68), (BIG * 0.24, BIG * 0.68)], outline=PS_GREEN, width=int(BIG * 0.07))


def small_text(text):
    def draw(d):
        d.rounded_rectangle((BIG * 0.05, BIG * 0.2, BIG * 0.95, BIG * 0.8), BIG * 0.2, fill=DARK, outline=RIM, width=int(BIG * 0.04))
        label(d, text, int(BIG * 0.19), LIGHT)
    return draw


GLYPHS = [
    face("A", GREEN), face("B", RED), face("X", BLUE), face("Y", YELLOW),
    shoulder("LB"), shoulder("RB"), trigger("LT"), trigger("RT"),
    stick("L"), stick("R"), stick("L", True), stick("R", True),
    dpad("up"), dpad("down"), dpad("left"), dpad("right"),
    dpad(), menu, view,
    cross, circle, square, triangle,
    shoulder("L1"), shoulder("R1"), trigger("L2"), trigger("R2"),
    stick("L", True), stick("R", True), small_text("OPTIONS"), small_text("SHARE"),
]

ROWS = (len(GLYPHS) + COLUMNS - 1) // COLUMNS


def sheet(cell, sharpen):
    im = Image.new("RGBA", (cell * COLUMNS, cell * ROWS), (0, 0, 0, 0))
    for i, draw in enumerate(GLYPHS):
        big = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
        draw(ImageDraw.Draw(big))
        small = big.resize((cell, cell), Image.LANCZOS)
        im.paste(small, ((i % COLUMNS) * cell, (i // COLUMNS) * cell))
    return im


os.makedirs(OUT, exist_ok=True)
sheet(128, False).save(OUT + "/PCGlyphs.png")
sheet(24, True).save(OUT + "/PCGlyphsSmall.png")
print(len(GLYPHS), "glyphs", ROWS, "rows")
