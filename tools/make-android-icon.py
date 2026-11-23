"""Draws Fettle (the desktop's mascot: MainWindow.axaml's MascotCritter, same colours and face) as the Android app
icon (AND-0009): an adaptive icon (foreground layer on the Dark theme's Bg) plus legacy PNGs for old launchers.
Writes into src/Darg.Android/Resources. Run: python tools/make-android-icon.py"""
import os
from PIL import Image, ImageDraw, ImageChops

OUT = os.path.join(os.path.dirname(__file__), '..', 'src', 'Darg.Android', 'Resources')
BG = (0x12, 0x11, 0x1a, 255)
LIGHT, DARK = (0xf5, 0xc5, 0x6b), (0xd0, 0x8a, 0x2e)
SHAVING, EYE, CHEEK = (0xb8, 0x74, 0x1f), (0x2a, 0x1a, 0x08), (0xe0, 0x7a, 0x4a)
S = 4  # supersample

def fettle(size, fill):
    """Fettle filling `fill` of a size x size transparent canvas, centred."""
    n = size * S
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    d = int(n * fill)            # body diameter
    x0 = (n - d) // 2; y0 = (n - d) // 2 + int(d * 0.06)
    # radial-ish gradient: lerp light to dark by distance from the highlight at (35%,30%)
    body = Image.new('RGBA', (d, d))
    px = body.load(); cx, cy = d * .35, d * .30
    for y in range(d):
        for x in range(d):
            t = min(1.0, (((x - cx) ** 2 + (y - cy) ** 2) ** .5) / (d * .75))
            px[x, y] = tuple(int(LIGHT[i] + (DARK[i] - LIGHT[i]) * t) for i in range(3)) + (255,)
    mask = Image.new('L', (d, d), 0); ImageDraw.Draw(mask).ellipse((0, 0, d - 1, d - 1), fill=255)
    img.paste(body, (x0, y0), mask)
    g = ImageDraw.Draw(img); u = d / 58
    def ell(cx_, cy_, rx, ry, col): g.ellipse((x0 + cx_ * u - rx * u, y0 + cy_ * u - ry * u, x0 + cx_ * u + rx * u, y0 + cy_ * u + ry * u), fill=col)
    cheek = (0xdf, 0x93, 0x4b, 255)  # the desktop's cheek colour at 45% over the body
    ell(29 - 15, 29 + 6, 4.5, 2.5, cheek); ell(29 + 15, 29 + 6, 4.5, 2.5, cheek)
    for ex in (29 - 9, 29 + 9):
        ell(ex, 29 - 3, 4, 5, EYE + (255,)); ell(ex + 1.2, 29 - 5.2, 1.3, 1.3, (255, 255, 255, 255))
    g.pieslice((x0 + (29 - 7) * u, y0 + (29 + 8) * u, x0 + (29 + 7) * u, y0 + (29 + 14) * u), 0, 180, fill=EYE + (255,))
    # the wood shaving curl on its head
    pts = [(26, 15), (24, 8), (30, 3), (37, 6), (38, 10), (35, 13), (32, 11)]
    g.line([(x0 + px_ * u, y0 + (py - 2) * u) for px_, py in pts], fill=SHAVING + (255,), width=int(3 * u), joint='curve')
    return img.resize((size, size), Image.LANCZOS)

def save(img, *path):
    p = os.path.join(OUT, *path); os.makedirs(os.path.dirname(p), exist_ok=True); img.save(p)

def rounded(size, radius_frac, fill):
    base = Image.new('RGBA', (size, size), BG)
    base.alpha_composite(fettle(size, fill))
    m = Image.new('L', (size * S, size * S), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, size * S - 1, size * S - 1), radius=int(size * S * radius_frac), fill=255)
    m = m.resize((size, size), Image.LANCZOS)
    out = Image.new('RGBA', (size, size), (0, 0, 0, 0)); out.paste(base, (0, 0), m); return out

for name, px in [('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)]:
    save(rounded(px, .22, .78), f'mipmap-{name}', 'ic_launcher.png')
    r = Image.new('RGBA', (px, px), BG); r.alpha_composite(fettle(px, .78))
    m = Image.new('L', (px * S, px * S), 0); ImageDraw.Draw(m).ellipse((0, 0, px * S - 1, px * S - 1), fill=255); m = m.resize((px, px), Image.LANCZOS)
    o = Image.new('RGBA', (px, px), (0, 0, 0, 0)); o.paste(r, (0, 0), m); save(o, f'mipmap-{name}', 'ic_launcher_round.png')
# adaptive: 108dp layer, the visible 72dp circle in the middle, so the body fills 46/108 to 60/108 of it
for name, px in [('mdpi', 108), ('hdpi', 162), ('xhdpi', 216), ('xxhdpi', 324), ('xxxhdpi', 432)]:
    save(fettle(px, .56), f'mipmap-{name}', 'ic_launcher_foreground.png')
os.makedirs(os.path.join(OUT, 'mipmap-anydpi-v26'), exist_ok=True)
for n in ('ic_launcher', 'ic_launcher_round'):
    with open(os.path.join(OUT, 'mipmap-anydpi-v26', n + '.xml'), 'w', newline='\n') as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n'
                '  <background android:drawable="@color/darg_bg"/>\n  <foreground android:drawable="@mipmap/ic_launcher_foreground"/>\n</adaptive-icon>\n')
save(rounded(512, .22, .78), '..', '..', '..', 'assets', 'fettle-icon-512.png')
print('icons written')
save(fettle(256, .9), 'drawable', 'icon.png')  # the splash screen's Fettle (drawable/splash.xml)
