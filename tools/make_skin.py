"""
Builds the overlay's photo skin from a set of photos of the real OCU taken from one camera position,
looking down at it as the operator does (Reference Pictures/new/IMG_0391-0415, HEIC). The photos
aren't in the repository; the generated skin in src/Beltpack.Overlay/Skin/ is.

One photo, with every control at rest, makes the base. Each of the others shows a control in another
position: it is lined up with the base on the printed face, and the control is cut out of it. The
overlay draws each control's cut-out for its current position over (or, for the knobs, behind) the
base.

Outputs to src/Beltpack.Overlay/Skin/:
  base.png              the case and fixed hardware cut out from the background, without the parts
                        of the paddles, toggles and knobs that stand clear of the case
  <control>_<pose>.png  each control in each position
  skin.json             where those sit, and the lamps, buttons and display window, in overlay units

Coordinates below are pixels in the base photo. Run with --preview to also write art/preview_*.png
for checking the cut-outs against the photos.

Requires: pip install numpy opencv-python pillow pillow-heif
"""
import glob
import json
import os
import sys

import cv2
import numpy as np
import pillow_heif
from PIL import Image, ImageOps

import make_legend_text

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PHOTOS = os.path.join(ROOT, "Reference Pictures", "new")
OUT = os.path.join(ROOT, "src", "Beltpack.Overlay", "Skin")
ART = os.path.join(ROOT, "art")

BASE_PHOTO = "IMG_0391"
# Overlay units per photo pixel; keeps the unit about as wide on screen as it has always been.
UNITS = 0.23
# Bitmap resolution per photo pixel. About twice the overlay's 100% size, so it stays sharp when enlarged.
BITMAP = 0.5

# The printed face, which every photo is lined up on.
FACE = (950, 820, 3500, 1580)

# The case, including its clear side bevels and the metal rim along the bottom. Along the top it
# follows the back edge of the yellow top strip; the hardware standing above that is listed below.
SHELL = [
    (1025, 705), (1060, 668), (1100, 645), (1345, 618), (1660, 607), (2620, 602), (2705, 598),
    (3310, 594), (3350, 605), (3380, 625), (3430, 660), (3470, 700), (3495, 750), (3510, 800),
    (3530, 850), (3550, 900), (3562, 950), (3575, 1000), (3597, 1100), (3620, 1200), (3645, 1300),
    (3660, 1400), (3668, 1500), (3670, 1560), (3665, 1588), (3645, 1620), (3610, 1648), (3400, 1660),
    (3000, 1645), (2600, 1635), (2200, 1628), (1800, 1628), (1500, 1640), (1000, 1652), (880, 1652),
    (850, 1640), (835, 1605), (790, 1578), (782, 1550), (795, 1500), (800, 1400), (805, 1350),
    (815, 1300), (828, 1225), (842, 1150), (858, 1075), (875, 1000), (895, 950), (915, 900),
    (935, 850), (960, 800), (990, 755),
]
# Fixed hardware above the top strip: the two fins, the antenna bracket (x0, y0, x1, y1, corner
# radius) and the rings round the corner buttons (centre, radii).
FINS = [
    [(1668, 790), (1668, 470), (1680, 440), (1700, 425), (1722, 430), (1740, 460), (1755, 520), (1762, 790)],
    [(2618, 790), (2625, 560), (2640, 480), (2660, 435), (2680, 420), (2695, 430), (2700, 470), (2705, 790)],
]
BRACKET = (2060, 393, 2316, 730, 28)
BEZELS = [((1220, 680), (125, 72)), ((3195, 650), (117, 75))]

# Paddles and toggles: for each position, the photo showing it and a rough outline of the moving
# part, from the top of the head down to where it goes into its nut. The outline only has to be
# within a few pixels; GrabCut finds the edge against the background. Below the top strip the cut-out
# keeps the photo's own pixels only inside the outlines, and takes the rest from the base photo, so
# strings and hands used to hold a position for the photo drop out.
LEVERS = {
    "auto": {
        "centre": ("IMG_0391", [(1425, 615), (1428, 560), (1440, 515), (1460, 485), (1480, 470), (1595, 470),
                                (1612, 480), (1622, 495), (1626, 510), (1627, 615), (1570, 615), (1570, 650),
                                (1480, 650), (1480, 615)]),
        "away": ("IMG_0405", [(1424, 590), (1428, 540), (1440, 500), (1460, 478), (1580, 470), (1605, 482),
                              (1622, 497), (1630, 515), (1631, 550), (1626, 580), (1614, 598), (1600, 604),
                              (1560, 604), (1560, 650), (1480, 650), (1480, 592)]),
        "toward": ("IMG_0409", [(1421, 672), (1422, 630), (1443, 605), (1444, 540), (1450, 505), (1465, 490),
                                (1580, 490), (1600, 492), (1612, 497), (1619, 515), (1620, 560), (1623, 672)]),
    },
    "dir": {
        "centre": ("IMG_0391", [(2755, 600), (2752, 530), (2765, 470), (2790, 440), (2900, 437), (2930, 480),
                                (2955, 540), (2962, 600), (2890, 600), (2890, 645), (2805, 645), (2805, 600)]),
        "away": ("IMG_0404", [(2752, 545), (2755, 480), (2790, 433), (2905, 433), (2930, 478), (2962, 530),
                              (2958, 568), (2890, 575), (2890, 645), (2805, 645), (2805, 575), (2775, 572)]),
        "toward": ("IMG_0403", [(2765, 655), (2760, 500), (2775, 465), (2800, 453), (2915, 453), (2940, 475),
                                (2962, 540), (2968, 655)]),
    },
    "bell": {
        "toward": ("IMG_0391", [(1833, 470), (1845, 440), (1871, 428), (1897, 440), (1910, 470), (1915, 560),
                                (1922, 630), (1863, 630), (1838, 560)]),
        "centre": ("IMG_0411", [(1834, 455), (1845, 428), (1874, 418), (1902, 428), (1914, 455), (1918, 560),
                                (1926, 630), (1870, 630), (1838, 560)]),
        "away": ("IMG_0412", [(1846, 425), (1858, 398), (1885, 388), (1912, 398), (1923, 425), (1925, 560),
                              (1927, 630), (1864, 630), (1848, 560)]),
    },
    "time": {
        "centre": ("IMG_0391", [(2458, 440), (2470, 410), (2500, 398), (2530, 410), (2545, 440), (2540, 560),
                                (2530, 630), (2463, 630), (2458, 560)]),
        "away": ("IMG_0413", [(2461, 410), (2473, 382), (2502, 370), (2531, 382), (2544, 410), (2542, 560),
                              (2537, 630), (2463, 630), (2460, 560)]),
        "toward": ("IMG_0415", [(2458, 490), (2470, 460), (2500, 446), (2530, 460), (2543, 490), (2540, 560),
                                (2534, 630), (2463, 630), (2458, 560)]),
    },
}
# String loops round a lever, used to hold it for the photo, painted out: (x0, y0, x1, y1).
RETOUCH = {
    ("bell", "away"): (1858, 598, 1932, 626),
    ("time", "away"): (2450, 583, 2538, 612),
}

# Side knobs: for each detent, counted from the first position (Rel, Stop), the photo showing it and
# a rough outline as for the levers. The knobs sit behind the case, so only what shows outside the
# case is cut out; the outlines run on into the case to close them.
KNOBS = {
    "ind": [
        ("IMG_0391", [(815, 948), (980, 945), (900, 1300), (815, 1328), (790, 1330), (760, 1320), (740, 1300),
                      (730, 1270), (725, 1230), (718, 1200), (700, 1178), (694, 1150), (700, 1120), (715, 1100),
                      (735, 1060), (760, 1010), (790, 965)]),
        ("IMG_0399", [(790, 950), (980, 948), (900, 1300), (810, 1330), (780, 1332), (750, 1315), (730, 1270),
                      (724, 1200), (722, 1120), (712, 1080), (712, 1045), (725, 1032), (750, 1025), (770, 990)]),
        ("IMG_0400", [(760, 938), (980, 935), (900, 1300), (805, 1332), (770, 1330), (740, 1302), (728, 1260),
                      (724, 1200), (724, 1100), (728, 1000), (738, 962)]),
        ("IMG_0401", [(770, 935), (790, 910), (820, 900), (880, 896), (980, 894), (900, 1300), (790, 1328),
                      (750, 1312), (733, 1280), (724, 1220), (728, 1150), (738, 1080), (758, 1000)]),
        ("IMG_0402", [(845, 850), (875, 834), (905, 826), (980, 822), (900, 1300), (790, 1328), (755, 1312),
                      (735, 1280), (724, 1230), (728, 1170), (742, 1100), (755, 1050), (776, 1000), (800, 950),
                      (825, 900)]),
    ],
    "speed": [
        ("IMG_0391", [(3555, 918), (3630, 915), (3670, 935), (3700, 965), (3725, 1000), (3745, 1040), (3758, 1062),
                      (3790, 1082), (3800, 1100), (3802, 1150), (3795, 1165), (3776, 1178), (3770, 1220),
                      (3765, 1260), (3755, 1300), (3740, 1330), (3720, 1345), (3690, 1352), (3650, 1340),
                      (3600, 1200), (3560, 1000)]),
        ("IMG_0392", [(3538, 862), (3600, 852), (3680, 855), (3720, 885), (3737, 930), (3740, 975), (3745, 1040),
                      (3756, 1080), (3766, 1130), (3771, 1180), (3770, 1230), (3760, 1280), (3745, 1320),
                      (3720, 1342), (3690, 1352), (3655, 1345), (3600, 1200), (3560, 1000)]),
        ("IMG_0393", [(3500, 805), (3520, 788), (3610, 785), (3645, 812), (3676, 857), (3697, 910), (3712, 960),
                      (3720, 1000), (3735, 1050), (3750, 1100), (3760, 1150), (3765, 1200), (3760, 1250),
                      (3750, 1290), (3730, 1320), (3700, 1337), (3670, 1332), (3620, 1250), (3560, 1000)]),
        ("IMG_0394", [(3500, 765), (3515, 762), (3550, 770), (3600, 786), (3630, 808), (3655, 840), (3670, 875),
                      (3680, 905), (3690, 935), (3700, 965), (3712, 1000), (3730, 1050), (3745, 1100), (3760, 1150), (3766, 1200), (3760, 1250),
                      (3745, 1300), (3720, 1330), (3690, 1347), (3660, 1340), (3620, 1250), (3560, 1000)]),
        ("IMG_0395", [(3502, 758), (3550, 765), (3590, 800), (3620, 840), (3640, 890), (3660, 935), (3685, 975),
                      (3710, 1000), (3730, 1030), (3750, 1100), (3760, 1150), (3760, 1200), (3755, 1250),
                      (3740, 1300), (3720, 1330), (3690, 1347), (3660, 1340), (3620, 1250), (3560, 1000)]),
        ("IMG_0396", [(3512, 795), (3546, 800), (3576, 840), (3606, 890), (3630, 918), (3665, 945), (3700, 960),
                      (3735, 1000), (3750, 1050), (3760, 1100), (3765, 1200), (3760, 1250), (3745, 1300),
                      (3720, 1340), (3680, 1350), (3620, 1250), (3560, 1000)]),
        ("IMG_0397", [(3560, 905), (3600, 905), (3640, 912), (3665, 928), (3690, 960), (3710, 1000), (3730, 1050),
                      (3746, 1100), (3760, 1150), (3766, 1200), (3760, 1250), (3750, 1290), (3730, 1322),
                      (3700, 1337), (3665, 1332), (3620, 1250), (3560, 1000)]),
        ("IMG_0398", [(3562, 926), (3600, 917), (3640, 920), (3665, 930), (3690, 945), (3710, 970), (3725, 1000),
                      (3740, 1045), (3755, 1100), (3765, 1150), (3770, 1200), (3765, 1250), (3755, 1290), (3735, 1320), (3705, 1340),
                      (3675, 1340), (3620, 1250), (3560, 1000)]),
    ],
}

# Push buttons: centre, dome radius across and down, bezel radius. The photo sees them at a slant, so
# the domes are wide ellipses. Both top corners are unlabelled; they're taken to be RSC, one for each
# hand.
BUTTONS = {"rsc_left": (1220, 675, 82, 48, 125), "rsc": (3197, 648, 85, 52, 117)}

# Lamps in control order (first = first enum value), and how far the legend extends either side of
# each lamp for click targets.
LAMPS = {
    "ind": ([(962, 1300), (980, 1221), (1000, 1142), (1018, 1063), (1040, 984)], (-30, 268)),
    "auto": ([(1623, 1300), (1623, 1222), (1622, 1142), (1621, 1063), (1620, 981), (1618, 901)], (-323, 30)),
    "dir": ([(2797, 1053), (2800, 971), (2799, 889)], (-30, 237)),
    "speed": ([(3495, 1460), (3477, 1378), (3456, 1295), (3435, 1212), (3414, 1130), (3393, 1047), (3372, 963),
               (3348, 883)], (-319, 29)),
}
LAMP_RADIUS = 17

DISPLAY = (2260, 1088, 2539, 1229)
ICONS = {"headlight": (2671, 1307, 29), "battery": (2822, 1307, 39)}
# The antenna jack on the bracket, and the radius of the antenna that screws onto it (photos with it
# fitted show it about 0.72 of the bracket's width). The overlay draws the antenna there.
ANTENNA = (2190, 545, 92)

# Legend boxes for the toggles, used as extra click targets, and the printed sun key (panel
# brightness): (x0, y0, x1, y1).
LEGENDS = {
    "bell_horn": (1675, 803, 2130, 889),
    "bell": (1678, 889, 2129, 972),
    "bell_off": (1680, 972, 2128, 1059),
    "time": (2339, 818, 2685, 917),
    "status": (2336, 917, 2679, 1016),
    "brightness": (1747, 1293, 1934, 1471),
}

# The face's top edge in the base photo rises about 27 px toward the right while the bottom edge is
# near level, which squeezes the right-hand end. The skin is redrawn with the top edge level about
# its middle and the bottom edge left as it is: (x, y) at each end of each edge.
FACE_TOP_EDGE = ((1000, 813.6), (3450, 786.1))
FACE_BOTTOM_EDGE = ((1000, 1565.8), (3450, 1579.4))
FACE_LEFT_EDGE = ((998, 900), (856, 1450))
FACE_RIGHT_EDGE = ((3430, 900), (3572, 1450))
# The light reflects off the satin face as a broad sheen, brightest round the display and down the
# SPEED side, which washes the print out. Wherever the plain face is brighter than this percentile of
# its grey, the excess is taken off the background (not the print or lamps).
GLARE_TARGET = 20
# Bright marks smaller than this (photo pixels) are flecks in the finish, not print.
PRINT_AREA = 80

# How far GrabCut may move a rough outline's edge, in photo pixels. The AUTO paddle's shaded side is
# about as dark as the mat behind it, so there GrabCut only trims and the outline is traced closely.
BAND = 14
BANDS = {"auto": 4}


def load(name):
    pillow_heif.register_heif_opener()
    im = ImageOps.exif_transpose(Image.open(os.path.join(PHOTOS, name + ".HEIC"))).convert("RGB")
    return cv2.cvtColor(np.asarray(im), cv2.COLOR_RGB2BGR)


class Aligner:
    """Lines photos up with the base photo by matching features on the printed face."""

    def __init__(self, base):
        self.sift = cv2.SIFT_create(8000)
        self.size = (base.shape[1], base.shape[0])
        self.kp, self.des = self.features(base)

    def features(self, im):
        mask = np.zeros(im.shape[:2], np.uint8)
        x0, y0, x1, y1 = FACE
        mask[y0:y1, x0:x1] = 255
        return self.sift.detectAndCompute(cv2.cvtColor(im, cv2.COLOR_BGR2GRAY), mask)

    def align(self, im):
        kp, des = self.features(im)
        matches = [a for a, b in cv2.BFMatcher().knnMatch(des, self.des, k=2) if a.distance < 0.7 * b.distance]
        src = np.float32([kp[m.queryIdx].pt for m in matches])
        dst = np.float32([self.kp[m.trainIdx].pt for m in matches])
        h, inliers = cv2.findHomography(src, dst, cv2.RANSAC, 2.0)
        if h is None or inliers.sum() < 100:
            sys.exit("A photo didn't line up with the base photo")
        return cv2.warpPerspective(im, h, self.size, flags=cv2.INTER_CUBIC)


def poly_mask(shape, pts):
    m = np.zeros(shape, np.uint8)
    cv2.fillPoly(m, [np.array(pts, np.int32)], 255)
    return m


def rounded_rect(mask, x0, y0, x1, y1, r):
    cv2.rectangle(mask, (x0 + r, y0), (x1 - r, y1), 255, -1)
    cv2.rectangle(mask, (x0, y0 + r), (x1, y1 - r), 255, -1)
    for cx, cy in ((x0 + r, y0 + r), (x1 - r, y0 + r), (x0 + r, y1 - r), (x1 - r, y1 - r)):
        cv2.circle(mask, (cx, cy), r, 255, -1)


def static_mask(shape):
    m = poly_mask(shape, SHELL)
    for fin in FINS:
        m |= poly_mask(shape, fin)
    rounded_rect(m, *BRACKET)
    for centre, axes in BEZELS:
        cv2.ellipse(m, centre, axes, 0, 0, 360, 255, -1)
    return m


def disc(r):
    return cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))


def bbox(mask, pad=4):
    ys, xs = np.nonzero(mask)
    return int(xs.min()) - pad, int(ys.min()) - pad, int(xs.max()) + pad + 1, int(ys.max()) + pad + 1


def grabcut(im, rough, static, band):
    """Moves the edge of a rough mask up to <band> pixels to fit the object against the background.
    Returns the object's pixels outside the case."""
    x0, y0, x1, y1 = bbox(rough, pad=band + 30)
    crop = im[y0:y1, x0:x1]
    r, s = rough[y0:y1, x0:x1], static[y0:y1, x0:x1]
    near = cv2.dilate(r, disc(band))
    gc = np.full(r.shape, cv2.GC_BGD, np.uint8)
    gc[near > 0] = cv2.GC_PR_BGD
    gc[r > 0] = cv2.GC_PR_FGD
    gc[cv2.erode(r, disc(band)) > 0] = cv2.GC_FGD
    gc[s > 0] = cv2.GC_FGD
    bgd, fgd = np.zeros((1, 65), np.float64), np.zeros((1, 65), np.float64)
    cv2.grabCut(crop, gc, None, bgd, fgd, 6, cv2.GC_INIT_WITH_MASK)
    fg = (np.isin(gc, (cv2.GC_FGD, cv2.GC_PR_FGD)) & (near > 0)).astype(np.uint8) * 255
    fg = cv2.morphologyEx(fg, cv2.MORPH_OPEN, disc(2))
    fg = cv2.morphologyEx(fg, cv2.MORPH_CLOSE, disc(4))
    # Keep what joins the case or the outline's core; drop specks of background.
    n, labels = cv2.connectedComponents(fg | s)
    keep = set(np.unique(labels[(s > 0) | (cv2.erode(r, disc(band)) > 0)])) - {0}
    fg = np.isin(labels, list(keep)).astype(np.uint8) * 255
    out = np.zeros_like(rough)
    out[y0:y1, x0:x1] = fg & ~s
    return out


def retouch(im, box):
    """Paints out the string loop inside <box>: dark and bluish against the warm chrome."""
    x0, y0, x1, y1 = box
    crop = im[y0:y1, x0:x1].astype(np.int16)
    blue = (crop[..., 0] - crop[..., 2] > -10) & (crop.max(axis=2) < 170)
    mask = np.zeros(im.shape[:2], np.uint8)
    mask[y0:y1, x0:x1] = blue * 255
    return cv2.inpaint(im, cv2.dilate(mask, disc(3)), 6, cv2.INPAINT_TELEA)


def face_mask(shape, inset=6):
    """The printed face, inside its bevel."""
    def x_at(edge, y):
        (x0, y0), (x1, y1) = edge
        return x0 + (y - y0) * (x1 - x0) / (y1 - y0)

    def y_at(edge, x):
        (x0, y0), (x1, y1) = edge
        return y0 + (x - x0) * (y1 - y0) / (x1 - x0)

    corners = []
    for across, side in ((FACE_TOP_EDGE, FACE_LEFT_EDGE), (FACE_TOP_EDGE, FACE_RIGHT_EDGE),
                         (FACE_BOTTOM_EDGE, FACE_RIGHT_EDGE), (FACE_BOTTOM_EDGE, FACE_LEFT_EDGE)):
        y = across[0][1]
        for _ in range(3):
            x = x_at(side, y)
            y = y_at(across, x)
        corners.append((x, y))
    return cv2.erode(poly_mask(shape, corners), disc(inset))


def deglare(im):
    """Takes the reflected sheen off the plain face (see GLARE_TARGET)."""
    face = face_mask(im.shape[:2]) > 0
    lum = cv2.cvtColor(im, cv2.COLOR_BGR2LAB)[..., 0].astype(np.float32)

    def background(plain):
        w = plain.astype(np.float32)
        return cv2.GaussianBlur(lum * w, (0, 0), 70) / np.maximum(cv2.GaussianBlur(w, (0, 0), 70), 1e-3)

    # The plain face is what's close to its surroundings; refined twice so large print drops out.
    bg = background((np.abs(lum - cv2.medianBlur(lum.astype(np.uint8), 41)) < 6) & face)
    for _ in range(2):
        bg = background((np.abs(lum - bg) < 10) & face)
    excess = np.clip(bg - np.percentile(bg[face], GLARE_TARGET), 0, None) * face
    excess = cv2.GaussianBlur(excess, (0, 0), 6)
    # Print and lamps are left as they are; the satin finish's bright flecks are too small to count.
    marks = ((lum - bg > 18) & face).astype(np.uint8)
    n, labels, stats, _ = cv2.connectedComponentsWithStats(marks, connectivity=8)
    large = np.isin(labels, np.nonzero(stats[:, cv2.CC_STAT_AREA] >= PRINT_AREA)[0][1:])
    near = cv2.dilate(large.astype(np.uint8), disc(3)) > 0
    printed = cv2.GaussianBlur(np.clip((lum - bg - 10) / 18, 0, 1) * near, (0, 0), 1.0)
    return np.clip(im - (excess * (1 - printed))[..., None], 0, 255).astype(np.uint8)


def feather(mask, sigma=1.6):
    return cv2.GaussianBlur(mask, (0, 0), sigma)


def save_layer(name, rgb, alpha, box):
    x0, y0, x1, y1 = box
    rgba = np.dstack([rgb[y0:y1, x0:x1], alpha[y0:y1, x0:x1]])
    size = (max(1, round((x1 - x0) * BITMAP)), max(1, round((y1 - y0) * BITMAP)))
    # Premultiply before shrinking so transparent pixels don't bleed their colour into the edges.
    a = rgba[..., 3:4].astype(np.float32) / 255
    pre = np.dstack([rgba[..., :3] * a, rgba[..., 3:4]]).astype(np.float32)
    small = cv2.resize(pre, size, interpolation=cv2.INTER_AREA)
    sa = small[..., 3:4] / 255
    rgb_out = np.where(sa > 0, small[..., :3] / np.maximum(sa, 1e-6), 0)
    out = np.dstack([np.clip(rgb_out, 0, 255), small[..., 3:4]]).astype(np.uint8)
    cv2.imwrite(os.path.join(OUT, f"{name}.png"), out, [cv2.IMWRITE_PNG_COMPRESSION, 9])


def build():
    print("loading photos")
    base = load(BASE_PHOTO)
    aligner = Aligner(base)
    base = deglare(base)
    photos = {BASE_PHOTO: base}

    def photo(name):
        if name not in photos:
            photos[name] = aligner.align(load(name))
        return photos[name]

    static = static_mask(base.shape[:2])
    soft_static = feather(static).astype(np.float32) / 255

    # Each pose: (rgb, alpha) over the whole photo frame, and the box worth saving.
    layers = {}
    for control, poses in LEVERS.items():
        print(control)
        outlines = {pose: poly_mask(static.shape, pts) for pose, (_, pts) in poses.items()}
        moving = np.zeros_like(static)
        for m in outlines.values():
            moving |= m
        # Inside the outlines, the case comes from the pose's photo, blending into the base photo.
        grown = cv2.dilate(moving, disc(4))
        own = cv2.GaussianBlur(grown, (0, 0), 3).astype(np.float32) / 255
        for pose, (name, _) in poses.items():
            im = photo(name)
            if (control, pose) in RETOUCH:
                im = retouch(im, RETOUCH[(control, pose)])
            clear = grabcut(im, outlines[pose], static, BANDS.get(control, BAND))
            # Run the silhouette on into the case so its feathered edge doesn't fade along the case edge.
            sil = feather(clear | (grown & static)).astype(np.float32) / 255
            alpha = soft_static * own + (1 - soft_static) * sil
            layers[(control, pose)] = (im, alpha, clear | (moving & static))
    for control, detents in KNOBS.items():
        print(control)
        for i, (name, pts) in enumerate(detents):
            im = photo(name)
            knob = grabcut(im, poly_mask(static.shape, pts), static, BAND)
            alpha = (1 - soft_static) * feather(knob).astype(np.float32) / 255
            layers[(control, str(i))] = (im, alpha, knob)
    return base, static, layers


def level(base, static, layers):
    """Redraws everything with the face's top edge level (see FACE_TOP_EDGE), and moves the measured
    positions (lamps, buttons and so on) to match."""
    global BUTTONS, LAMPS, LAMP_RADIUS, DISPLAY, ICONS, ANTENNA, LEGENDS
    src = np.float32([*FACE_TOP_EDGE, *FACE_BOTTOM_EDGE])
    dst = src.copy()
    dst[:2, 1] = src[:2, 1].mean()
    h = cv2.getPerspectiveTransform(src, dst)
    size = (base.shape[1], base.shape[0])

    def warp(im, flags=cv2.INTER_CUBIC):
        return cv2.warpPerspective(im, h, size, flags=flags)

    def p(x, y):
        return cv2.perspectiveTransform(np.float32([[[x, y]]]), h)[0, 0]

    def P(x, y):
        return tuple(int(round(v)) for v in p(x, y))

    def across(x, y, r):
        return int(round(np.linalg.norm(p(x + r, y) - p(x - r, y)) / 2))

    def down(x, y, r):
        return int(round(np.linalg.norm(p(x, y + r) - p(x, y - r)) / 2))

    def box(x0, y0, x1, y1):
        c = np.array([p(x0, y0), p(x1, y0), p(x0, y1), p(x1, y1)])
        return (*np.round(c.min(axis=0)).astype(int), *np.round(c.max(axis=0)).astype(int))

    BUTTONS = {k: (*P(x, y), across(x, y, rx), down(x, y, ry), across(x, y, b)) for k, (x, y, rx, ry, b) in BUTTONS.items()}
    LAMP_RADIUS = across(*LAMPS["dir"][0][1], LAMP_RADIUS)
    LAMPS = {k: ([P(x, y) for x, y in pts], (round(a * across(*pts[0], 100) / 100), round(b * across(*pts[0], 100) / 100)))
             for k, (pts, (a, b)) in LAMPS.items()}
    DISPLAY = box(*DISPLAY)
    ICONS = {k: (*P(x, y), across(x, y, r)) for k, (x, y, r) in ICONS.items()}
    ANTENNA = (*P(*ANTENNA[:2]), across(*ANTENNA))
    LEGENDS = {k: box(*v) for k, v in LEGENDS.items()}

    layers = {key: (warp(im), warp(alpha, cv2.INTER_LINEAR), warp(m, cv2.INTER_NEAREST))
              for key, (im, alpha, m) in layers.items()}
    return warp(base), warp(static, cv2.INTER_NEAREST), layers


def export(base, static, layers):
    os.makedirs(OUT, exist_ok=True)
    everything = static.copy()
    for _, _, m in layers.values():
        everything |= m
    ox, oy, ex, ey = bbox(everything, pad=8)

    def u(v, o=0):
        return round((v - o) * UNITS, 2)

    def pt(x, y):
        return [u(x, ox), u(y, oy)]

    def rect(x0, y0, x1, y1):
        return {"x": u(x0, ox), "y": u(y0, oy), "w": u(x1 - x0), "h": u(y1 - y0)}

    written = {"legend_text.png"}
    save_layer("base", base, feather(static), (ox, oy, ex, ey))
    written.add("base.png")
    layout = {
        "width": u(ex - ox),
        "height": u(ey - oy),
        "base": dict(file="base.png", **rect(ox, oy, ex, ey)),
        "controls": {},
    }
    controls = {}
    for (control, pose), (im, alpha, m) in layers.items():
        box = bbox((alpha > 0.004).astype(np.uint8), pad=2)
        name = f"{control}_{pose}"
        save_layer(name, im, (alpha * 255).round().astype(np.uint8), box)
        written.add(name + ".png")
        c = controls.setdefault(control, {"poses": {}, "area": np.zeros_like(static)})
        c["poses"][pose] = dict(file=name + ".png", **rect(*box))
        c["area"] |= m
    for control, c in controls.items():
        layout["controls"][control] = {"hit": rect(*bbox(c["area"], pad=0)), "poses": c["poses"]}
    for stale in glob.glob(os.path.join(OUT, "*.png")):
        if os.path.basename(stale) not in written:
            os.remove(stale)

    layout["buttons"] = {k: {"center": pt(x, y), "dome": u(r), "domeY": u(ry), "bezel": u(b)}
                         for k, (x, y, r, ry, b) in BUTTONS.items()}
    layout["lamps"] = {k: {"points": [pt(x, y) for x, y in pts], "legend": [u(a), u(b)]}
                       for k, (pts, (a, b)) in LAMPS.items()}
    layout["lampRadius"] = u(LAMP_RADIUS)
    layout["display"] = rect(*DISPLAY)
    layout["icons"] = {k: {"center": pt(x, y), "radius": u(r)} for k, (x, y, r) in ICONS.items()}
    layout["antenna"] = {"center": pt(*ANTENNA[:2]), "radius": u(ANTENNA[2])}
    layout["legends"] = {k: rect(*v) for k, v in LEGENDS.items()}
    with open(os.path.join(OUT, "skin.json"), "w") as f:
        json.dump(layout, f, indent=1)
    print(f"skin {layout['width']} x {layout['height']} units, {len(layers)} poses -> {OUT}")


def preview(base, static, layers):
    os.makedirs(ART, exist_ok=True)
    marked = base.copy()
    contours, _ = cv2.findContours(static, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cv2.drawContours(marked, contours, -1, (255, 0, 255), 2)
    for pts, _ in LAMPS.values():
        for x, y in pts:
            cv2.circle(marked, (x, y), LAMP_RADIUS, (0, 255, 0), 2)
    for x0, y0, x1, y1 in [DISPLAY, *LEGENDS.values()]:
        cv2.rectangle(marked, (x0, y0), (x1, y1), (0, 255, 255), 2)
    for x, y, r in ICONS.values():
        cv2.circle(marked, (x, y), r, (0, 255, 255), 2)
    for x, y, rx, ry, b in BUTTONS.values():
        cv2.ellipse(marked, (x, y), (rx, ry), 0, 0, 360, (0, 255, 0), 2)
        cv2.circle(marked, (x, y), b, (0, 160, 0), 1)
    cv2.imwrite(os.path.join(ART, "preview_base.jpg"), marked[300:1750, 500:4000])

    # Every pose composited on grey, as the overlay draws it.
    for control in list(LEVERS) + list(KNOBS):
        poses = [(p, layer) for (c, p), layer in layers.items() if c == control]
        area = np.zeros_like(static)
        for _, (_, _, m) in poses:
            area |= m
        x0, y0, x1, y1 = bbox(area, pad=40)
        tiles = []
        for pose, (im, alpha, _) in poses:
            back = np.full_like(base, 110).astype(np.float32)
            a = feather(static).astype(np.float32)[..., None] / 255
            under = alpha[..., None] if control in KNOBS else 0
            out = back * (1 - under) + im * under
            out = out * (1 - a) + base * a
            if control not in KNOBS:
                al = alpha[..., None]
                out = out * (1 - al) + im * al
            tile = out[y0:y1, x0:x1].astype(np.uint8).copy()
            cv2.putText(tile, pose, (6, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
            tiles.append(tile)
        cv2.imwrite(os.path.join(ART, f"preview_{control}.jpg"), np.hstack(tiles))


def main():
    base, static, layers = level(*build())
    if "--preview" in sys.argv:
        preview(base, static, layers)
    export(base, static, layers)
    make_legend_text.main()


if __name__ == "__main__":
    main()
