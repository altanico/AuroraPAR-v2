"""Draws the AuroraPAR icon: a small PAR profile view (ground, coverage sector,
glide path with tolerances, range marks) with an aircraft track symbol."""
from PIL import Image, ImageDraw

S = 1024  # design canvas
BG = (8, 20, 14, 255)
BORDER = (40, 120, 70, 255)
GROUND = (40, 200, 90, 255)
SECTOR = (95, 160, 160, 255)
GLIDE = (255, 215, 0, 255)
TOL = (220, 50, 50, 255)
MARK = (30, 120, 60, 255)
ACFT = (60, 255, 110, 255)

O = (150, 800)       # touchdown / origin
END_X = 880


def y_on(slope_end_y, x):
    return O[1] + (slope_end_y - O[1]) * (x - O[0]) / (END_X - O[0])


def draw(detail: bool) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([24, 24, S - 24, S - 24], radius=190, fill=BG,
                        outline=BORDER, width=28 if detail else 40)
    w = 1 if detail else 1.7  # thicker lines for tiny sizes
    sector_end, glide_end = 200, 520
    if detail:
        for x in (340, 530, 720):
            d.line([(x, O[1]), (x, y_on(sector_end, x))], fill=MARK, width=int(10 * w))
        for e in (glide_end - 70, glide_end + 70):
            d.line([O, (END_X, e)], fill=TOL, width=int(10 * w))
    d.line([O, (END_X, sector_end)], fill=SECTOR, width=int(26 * w))
    d.line([(O[0], O[1]), (END_X, O[1])], fill=GROUND, width=int(26 * w))
    d.line([O, (END_X, glide_end)], fill=GLIDE, width=int(24 * w))
    # Aircraft track symbol on the glide path: circle with a cross.
    ax = 620
    ay = y_on(glide_end, ax)
    r = 70 if detail else 105
    t = int(20 * (1 if detail else 1.5))
    d.ellipse([ax - r, ay - r, ax + r, ay + r], outline=ACFT, width=t)
    d.line([(ax - r, ay), (ax + r, ay)], fill=ACFT, width=t)
    d.line([(ax, ay - r), (ax, ay + r)], fill=ACFT, width=t)
    return img


big, small = draw(True), draw(False)
sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [(small if s <= 32 else big).resize((s, s), Image.LANCZOS) for s in sizes]
frames[-1].save("AuroraPAR.ico", format="ICO", sizes=[(s, s) for s in sizes],
                append_images=frames[:-1])
# Preview sheet for the user.
sheet = Image.new("RGBA", (256 + 128 + 64 + 48 + 32 + 16 + 7 * 24, 280), (240, 240, 240, 255))
x = 12
for f in reversed(frames):
    if f.size[0] == 24:
        continue
    sheet.paste(f, (x, (280 - f.size[1]) // 2), f)
    x += f.size[0] + 24
sheet.save("icon_preview.png")
print("ok")
