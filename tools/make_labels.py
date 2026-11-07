"""
Draws the labelled picture of the unit for the README and the manual (docs/images/ocu-labelled.png)
from the overlay's own render of the unit:

  Beltpack.Overlay.exe --render-unit <folder>
  python tools/make_labels.py <folder>/unit-off.png

Label targets are in the coordinates of the unit scaled to 1024 pixels wide; move them if the skin
changes.

Requires: pip install pillow (and Windows, for the Segoe UI Bold font)
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "images", "ocu-labelled.png")

SUPERSAMPLE = 2
WIDTH, HEIGHT = 1400, 860
UNIT_X, UNIT_Y, UNIT_WIDTH = 200, 250, 1000

# (text, label centre x, label top, target on the unit, optional arrow start)
LABELS = [
    ("IND\nBrake\nKnob", 75, 300, (22, 255), (85, 400)),
    ("RSC\nButton", 250, 160, (160, 100), None),
    ("AUTO\nBrake\nPaddle", 395, 40, (280, 70), None),
    ("Horn/Bell\nSwitch", 545, 75, (398, 60), None),
    ("Antenna", 700, 120, (505, 25), None),
    ("Time / Status\nSwitch", 850, 75, (606, 60), None),
    ("Direction\nPaddle", 1000, 110, (722, 72), None),
    ("RSC\nButton", 1150, 160, (842, 100), None),
    ("Speed\nControl\nKnob", 1325, 300, (1003, 255), (1315, 400)),
    ("Brightness\nKey", 470, 760, (385, 360), (475, 750)),
    ("Main Display", 790, 790, (572, 290), (780, 780)),
]


def main(unit_png):
    s = SUPERSAMPLE
    unit = Image.open(unit_png).convert("RGBA")
    height = round(unit.height * UNIT_WIDTH / unit.width)
    unit = unit.resize((UNIT_WIDTH * s, height * s), Image.LANCZOS)
    canvas = Image.new("RGBA", (WIDTH * s, HEIGHT * s), "white")
    canvas.alpha_composite(unit, (UNIT_X * s, UNIT_Y * s))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 23 * s)

    def arrow(start, end):
        (x0, y0), (x1, y1) = start, end
        angle = math.atan2(y1 - y0, x1 - x0)
        head, half = 26, 13
        bx, by = x1 - head * math.cos(angle), y1 - head * math.sin(angle)
        draw.line([(x0 * s, y0 * s), (bx * s, by * s)], fill="black", width=6 * s)
        px, py = -math.sin(angle) * half, math.cos(angle) * half
        draw.polygon([(x1 * s, y1 * s), ((bx + px) * s, (by + py) * s), ((bx - px) * s, (by - py) * s)],
                     fill="black")

    scale = UNIT_WIDTH / 1024
    for text, cx, top, (tx, ty), start in LABELS:
        box = draw.multiline_textbbox((0, 0), text, font=font, align="center", spacing=4 * s)
        w, h = (box[2] - box[0]) / s, (box[3] - box[1]) / s
        draw.multiline_text(((cx - w / 2) * s, top * s), text, font=font, fill="black", align="center",
                            spacing=4 * s)
        arrow(start or (cx, top + h + 16), (UNIT_X + tx * scale, UNIT_Y + ty * scale))

    canvas.resize((WIDTH, HEIGHT), Image.LANCZOS).convert("RGB").save(OUT, optimize=True)
    print("Wrote", OUT)


if __name__ == "__main__":
    main(sys.argv[1])
