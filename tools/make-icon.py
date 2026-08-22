#!/usr/bin/env python3
"""
AppGeek application icon generator.

Follows the TechyGeeksHome app-icon standard:

    gradient top     #6BA3F7      light blue, top of the badge
    gradient bottom  #2563EB      deep blue, bottom of the badge
    glyph            #FFFFFF      solid white, always
    gloss            white ~12%   wide soft ellipse across the upper half

    rounded-square badge, corner radius 22% of icon size
    vertical gradient, lightest at the top
    solid white pictorial glyph filling roughly 60% of the badge
    NO LETTERING AT ANY SIZE
    optional small secondary badge bottom-right

Everything except draw_glyph() and draw_secondary_badge() is shared with the rest
of the Geek range. To make the next app's icon, copy this file and replace those
two functions — that is what keeps the range visually consistent.

AppGeek's glyph: a 2x2 grid where three cells are app tiles (says "applications")
and the fourth is a download badge (says "install / update").

Usage:  python3 make-icon.py [output_dir]      default: ../icons
"""

import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

# ---------------------------------------------------------------- brand tokens

GRADIENT_TOP = (0x6B, 0xA3, 0xF7)
GRADIENT_BOTTOM = (0x25, 0x63, 0xEB)
GLYPH = (0xFF, 0xFF, 0xFF)

CORNER_RADIUS_RATIO = 0.22      # of icon size
GLOSS_ALPHA = 0.12
GLYPH_EXTENT = 0.60             # glyph fills ~60% of the badge

# Render everything at this size then downsample, so small icons stay crisp.
SUPERSAMPLE = 8
MASTER = 1024

EXPORT_SIZES = [1024, 512, 256, 128, 96, 64, 48, 32, 16]
ICO_SIZES = [256, 128, 64, 48, 32, 16]

APP_NAME = "appgeek"


# ------------------------------------------------------------------ base badge

def rounded_badge(size):
    """The gradient rounded-square with its gloss, at the given pixel size."""
    grad = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / max(1, size - 1)
        grad.putpixel((0, y), tuple(
            round(GRADIENT_TOP[i] + (GRADIENT_BOTTOM[i] - GRADIENT_TOP[i]) * t)
            for i in range(3)
        ))
    grad = grad.resize((size, size), Image.NEAREST)

    # Gloss: a wide soft ellipse across the upper half.
    gloss = Image.new("L", (size, size), 0)
    gd = ImageDraw.Draw(gloss)
    gd.ellipse(
        [-size * 0.35, -size * 0.62, size * 1.35, size * 0.52],
        fill=int(255 * GLOSS_ALPHA),
    )
    gloss = gloss.filter(ImageFilter.GaussianBlur(size * 0.05))

    badge = grad.convert("RGBA")
    white = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    badge = Image.composite(
        Image.alpha_composite(badge, Image.new("RGBA", (size, size), (255, 255, 255, 0))),
        badge, Image.new("L", (size, size), 0),
    )
    badge.paste(white, (0, 0), gloss)

    # Clip to the rounded square.
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1],
        radius=int(size * CORNER_RADIUS_RATIO),
        fill=255,
    )
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(badge, (0, 0), mask)
    return out


# ------------------------------------------------------------------- the glyph
#
# Composition: a 2x2 grid where three cells are app tiles and the fourth
# (bottom-right) is the download badge. The badge occupying a grid cell rather
# than being pasted over the corner is what keeps it tidy at every size.

DETAIL_FULL = 3      # circle badge, separation ring, arrow, tray bar
DETAIL_MEDIUM = 2    # circle badge and arrow only
DETAIL_SMALL = 1     # bare arrow, no circle
DETAIL_TINY = 0      # four plain tiles, no badge at all


def detail_for(target_size):
    if target_size >= 48:
        return DETAIL_FULL
    if target_size >= 32:
        return DETAIL_MEDIUM
    if target_size >= 24:
        return DETAIL_SMALL
    # At 16px there is roughly 4px per element. An arrow at that scale is a
    # smudge, so the mark falls back to four clean tiles: still unmistakably
    # "applications", and crisp because the geometry is snapped to whole pixels.
    return DETAIL_TINY


def grid_geometry(size):
    """Returns (tile_size, cell_centres) for the 2x2 layout."""
    extent = size * GLYPH_EXTENT
    gap = extent * 0.16
    tile = (extent - gap) / 2

    left = size / 2 - extent / 2
    top = size / 2 - extent / 2
    step = tile + gap

    centres = [
        (left + tile / 2, top + tile / 2),                 # top-left
        (left + step + tile / 2, top + tile / 2),          # top-right
        (left + tile / 2, top + step + tile / 2),          # bottom-left
        (left + step + tile / 2, top + step + tile / 2),   # bottom-right -> badge
    ]
    return tile, centres


def draw_glyph(draw, size, detail):
    """White rounded app tiles. Above TINY the fourth cell is left for the badge."""
    tile, centres = grid_geometry(size)
    radius = tile * 0.26

    cells = centres if detail == DETAIL_TINY else centres[:3]
    for cx, cy in cells:
        draw.rounded_rectangle(
            [cx - tile / 2, cy - tile / 2, cx + tile / 2, cy + tile / 2],
            radius=radius, fill=GLYPH,
        )


def draw_secondary_badge(base, size, detail):
    """The download mark sitting in the fourth grid cell."""
    if detail == DETAIL_TINY:
        return

    d = ImageDraw.Draw(base)
    tile, centres = grid_geometry(size)
    cx, cy = centres[3]

    if detail == DETAIL_SMALL:
        # No circle: a bare white arrow, sized to the cell. Anything more
        # turns into a grey smudge at 16px.
        shaft_w = tile * 0.34
        d.rounded_rectangle(
            [cx - shaft_w / 2, cy - tile * 0.50, cx + shaft_w / 2, cy + tile * 0.05],
            radius=shaft_w / 2, fill=GLYPH,
        )
        head = tile * 0.52
        d.polygon(
            [(cx - head, cy - tile * 0.08), (cx + head, cy - tile * 0.08),
             (cx, cy + tile * 0.52)],
            fill=GLYPH,
        )
        return

    # Circle badge, slightly larger than a tile so it reads as a distinct mark.
    r = tile * 0.62

    if detail == DETAIL_FULL:
        # A ring in the badge's own blue detaches the circle from the tiles.
        ring = r * 0.10
        d.ellipse([cx - r - ring, cy - r - ring, cx + r + ring, cy + r + ring],
                  fill=GRADIENT_BOTTOM)

    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=GLYPH)

    # Download arrow, in the badge blue.
    shaft_w = r * 0.30
    top = cy - r * (0.58 if detail == DETAIL_FULL else 0.62)
    bottom = cy + r * (0.06 if detail == DETAIL_FULL else 0.02)
    d.rounded_rectangle([cx - shaft_w / 2, top, cx + shaft_w / 2, bottom],
                        radius=shaft_w / 2, fill=GRADIENT_BOTTOM)

    head = r * 0.50
    tip = cy + r * (0.48 if detail == DETAIL_FULL else 0.52)
    d.polygon([(cx - head, cy - r * 0.04), (cx + head, cy - r * 0.04), (cx, tip)],
              fill=GRADIENT_BOTTOM)

    if detail == DETAIL_FULL:
        # The tray line under the arrow, dropped below 48px where it smears.
        bar_w = r * 0.88
        bar_h = r * 0.15
        by = cy + r * 0.66
        d.rounded_rectangle(
            [cx - bar_w / 2, by - bar_h / 2, cx + bar_w / 2, by + bar_h / 2],
            radius=bar_h / 2, fill=GRADIENT_BOTTOM,
        )


# ------------------------------------------------------------------- rendering

def render(target_size):
    """Renders one icon at target_size, supersampled for clean edges."""
    # Fine detail becomes noise at small sizes. Simplify rather than blur.
    detail = detail_for(target_size)

    if detail == DETAIL_TINY:
        return render_tiny(target_size)

    work = min(MASTER * 2, target_size * SUPERSAMPLE)
    work = max(work, 256)

    icon = rounded_badge(work)
    draw = ImageDraw.Draw(icon)

    draw_glyph(draw, work, detail)
    draw_secondary_badge(icon, work, detail)

    if work != target_size:
        icon = icon.resize((target_size, target_size), Image.LANCZOS)
    return icon


def render_tiny(size):
    """
    Draws the tiny sizes directly at their target resolution with whole-pixel
    geometry. Downsampling from a supersampled master puts the tile edges on
    fractional pixels, which at 16px reads as blur rather than as tiles.
    """
    icon = rounded_badge(size)
    d = ImageDraw.Draw(icon)

    tile = max(3, round(size * 0.28))
    gap = max(1, round(size * 0.11))
    span = tile * 2 + gap
    left = round((size - span) / 2)
    # A 1px radius on a 4px tile rounds it into a dot, so square them off.
    radius = 0 if tile <= 4 else (1 if tile <= 6 else 2)

    for row in range(2):
        for col in range(2):
            x = left + col * (tile + gap)
            y = left + row * (tile + gap)
            d.rounded_rectangle([x, y, x + tile - 1, y + tile - 1],
                                radius=radius, fill=GLYPH)
    return icon


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "icons")
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)

    for size in EXPORT_SIZES:
        img = render(size)
        img.save(os.path.join(out_dir, f"{APP_NAME}-{size}.png"))
        print(f"  wrote {APP_NAME}-{size}.png")

    # Canonical 256px copy, used by the README and the About dialog.
    render(256).save(os.path.join(out_dir, f"{APP_NAME}.png"))
    print(f"  wrote {APP_NAME}.png (256)")

    # Multi-resolution .ico embedded in the executable.
    ico_path = os.path.join(out_dir, f"{APP_NAME}.ico")
    frames = [render(s) for s in ICO_SIZES]
    frames[0].save(ico_path, format="ICO",
                   sizes=[(s, s) for s in ICO_SIZES],
                   append_images=frames[1:])
    print(f"  wrote {APP_NAME}.ico ({'/'.join(str(s) for s in ICO_SIZES)})")

    print(f"\nDone. Output: {out_dir}")


if __name__ == "__main__":
    main()
