"""
Finds the printed word beside each lamp in the skin (Emerg, Full, ... Stop), which a real OCU lights
from behind in the lamp's colour.

Reads src/Beltpack.Overlay/Skin/base.png and skin.json (from make_skin.py, which runs this at the
end) and writes:
  legend_text.png   white, with the words' letters opaque and everything else clear
  skin.json         "legendText": each lamp's word box, in overlay units, in lamp order

Requires: pip install numpy opencv-python
"""
import json
import os

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIN = os.path.join(ROOT, "src", "Beltpack.Overlay", "Skin")

# Printed white stands well above the grey face around it. The face is lit unevenly, so each pixel
# is compared with its surroundings (over BACKGROUND bitmap pixels); anti-aliased edges fade between
# these.
WHITE_FROM, WHITE_TO = 25, 55
BACKGROUND = 51
# Letters closer than this (bitmap pixels) belong to the same word; "Coast B" has the widest gap.
LETTER_GAP = 16
# How much further than the click target a word may run, as a share of its width ("Emerg").
OVERRUN = 0.5


def main():
    with open(os.path.join(SKIN, "skin.json")) as f:
        layout = json.load(f)
    base = cv2.imread(os.path.join(SKIN, "base.png"), cv2.IMREAD_UNCHANGED)
    k = base.shape[1] / layout["base"]["w"]
    whiteness = base[..., :3].min(axis=2)
    lift = whiteness.astype(float) - cv2.medianBlur(whiteness, BACKGROUND)
    soft = np.clip((lift - WHITE_FROM) / (WHITE_TO - WHITE_FROM), 0, 1) * (base[..., 3] > 200)
    letters = (lift > (WHITE_FROM + WHITE_TO) / 2) & (base[..., 3] > 200)
    out = np.zeros(base.shape[:2], float)
    lamp_r = layout["lampRadius"]
    boxes = {}

    for group, lamps in layout["lamps"].items():
        pts = lamps["points"]
        a, b = lamps["legend"]
        right = b > -a
        spacing = abs(pts[-1][1] - pts[0][1]) / (len(pts) - 1)
        boxes[group] = []
        for px, py in pts:
            x0, x1 = px + a, px + b
            if right:
                x1 += (b - a) * OVERRUN
            else:
                x0 -= (b - a) * OVERRUN
            X0, X1 = int(x0 * k), int(x1 * k)
            Y0, Y1 = int((py - spacing / 2) * k), int((py + spacing / 2) * k)
            region = letters[Y0:Y1, X0:X1].copy()
            lamp_x0, lamp_x1 = int((px - 2 * lamp_r) * k) - X0, int((px + 2 * lamp_r) * k) - X0
            region[:, max(lamp_x0, 0):max(lamp_x1, 0)] = False

            n, labels, stats, _ = cv2.connectedComponentsWithStats(region.astype(np.uint8), connectivity=8)
            # Divider lines run the full height of a row; letters don't.
            comps = [i for i in range(1, n) if stats[i, cv2.CC_STAT_HEIGHT] < 0.85 * (Y1 - Y0)
                     and stats[i, cv2.CC_STAT_AREA] > 4]
            lamp_edge = int(px * k) - X0
            near = (lambda i: stats[i, cv2.CC_STAT_LEFT] - lamp_edge) if right else \
                   (lambda i: lamp_edge - (stats[i, cv2.CC_STAT_LEFT] + stats[i, cv2.CC_STAT_WIDTH]))
            comps.sort(key=near)
            word, extent = [], None
            for i in comps:
                left = stats[i, cv2.CC_STAT_LEFT]
                end = left + stats[i, cv2.CC_STAT_WIDTH]
                if extent is not None:
                    gap = left - extent[1] if right else extent[0] - end
                    if gap > LETTER_GAP:
                        break
                word.append(i)
                extent = (left, end) if extent is None else (min(extent[0], left), max(extent[1], end))

            mask = np.isin(labels, word).astype(np.uint8)
            mask = cv2.dilate(mask, np.ones((5, 5), np.uint8)) > 0
            ys, xs = np.nonzero(mask)
            bx0, bx1, by0, by1 = xs.min() + X0, xs.max() + X0 + 1, ys.min() + Y0, ys.max() + Y0 + 1
            out[Y0:Y1, X0:X1] = np.maximum(out[Y0:Y1, X0:X1], soft[Y0:Y1, X0:X1] * mask)
            boxes[group].append({"x": round(bx0 / k, 2), "y": round(by0 / k, 2),
                                 "w": round((bx1 - bx0) / k, 2), "h": round((by1 - by0) / k, 2)})

    rgba = np.zeros(base.shape[:2] + (4,), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = (out * 255).round().astype(np.uint8)
    cv2.imwrite(os.path.join(SKIN, "legend_text.png"), rgba)
    layout["legendText"] = boxes
    with open(os.path.join(SKIN, "skin.json"), "w") as f:
        json.dump(layout, f, indent=1)
    print(f"legend text for {sum(len(v) for v in boxes.values())} lamps -> {SKIN}")


if __name__ == "__main__":
    main()
