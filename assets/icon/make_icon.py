"""
Icone de Cyberpunk Mod Translator : le badge jaune « CMT » de l'en-tete de l'appli, en carre.

    python make_icon.py [police.otf]

Il faut Pillow (pip install pillow). Police : Inter ExtraBold (celle utilisee pour l'icone livree), sinon Segoe UI Bold.
Ecrit assets/icon/app.ico (exe et fenetre), assets/icon/icon-1024.png (page Nexus, GitHub)
et src/App/wwwroot/icon-256.png, icon-32.png (icone de la fenetre).
"""
import io, os, struct, sys
from PIL import Image, ImageDraw, ImageFont

YELLOW = (0xF5, 0xE1, 0x4B, 255)   # --accent de app.css
INK = (0x0D, 0x0F, 0x14, 255)      # --bg de app.css
HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = [
    "/usr/share/fonts/opentype/inter/Inter-ExtraBold.otf",
    os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "Windows", "Fonts", "Inter-ExtraBold.otf"),
    r"C:\Windows\Fonts\Inter-ExtraBold.otf",
    r"C:\Windows\Fonts\segoeuib.ttf",
]
FONT = sys.argv[1] if len(sys.argv) > 1 else next((f for f in FONTS if os.path.exists(f)), FONTS[-1])

def render(size):
    # dessin en 8x puis reduction : bords nets sans crenelage
    k = 8 if size <= 64 else 4
    S = size * k
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    pad = 0 if size <= 24 else round(S * 0.03)
    radius = round(S * (0.12 if size > 24 else 0.14))
    d.rounded_rectangle([pad, pad, S - 1 - pad, S - 1 - pad], radius=radius, fill=YELLOW)
    # Texte le plus grand possible : ~84 % de la largeur aux petites tailles, ~74 % aux grandes
    target = (S - 2 * pad) * (0.86 if size <= 24 else 0.76)
    spacing_ratio = 0.04
    fs = S
    while True:
        font = ImageFont.truetype(FONT, fs)
        widths = [font.getlength(c) for c in "CMT"]
        spacing = fs * spacing_ratio
        total = sum(widths) + spacing * 2
        if total <= target: break
        fs -= max(1, fs // 50)
    # centrage vertical sur la hauteur des capitales
    bbox = font.getbbox("CMT")
    cap_top, cap_bottom = bbox[1], bbox[3]
    y = (S - (cap_bottom - cap_top)) / 2 - cap_top
    x = (S - total) / 2
    for c, w in zip("CMT", widths):
        d.text((x, y), c, font=font, fill=INK)
        x += w + spacing
    return im.resize((size, size), Image.LANCZOS)

def bmp_entry(im):
    """Image 32 bits BGRA + masque AND, format DIB des .ico (pour les petites tailles)."""
    w, h = im.size
    px = im.load()
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            rows += bytes((b, g, r, a))
    mask_row = ((w + 31) // 32) * 4
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(mask_row)
        for x in range(w):
            if px[x, y][3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits
    return header + bytes(rows) + bytes(mask)

def png_entry(im):
    buf = io.BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return buf.getvalue()

def write_ico(path, images):
    entries = []
    for im in images:
        data = png_entry(im) if im.size[0] >= 128 else bmp_entry(im)
        entries.append((im.size[0], data))
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    blobs = b""
    for size, data in entries:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset + len(blobs))
        blobs += data
    open(path, "wb").write(bytes(out) + blobs)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = {s: render(s) for s in sizes}
write_ico(os.path.join(HERE, "app.ico"), [images[s] for s in sizes])
render(1024).save(os.path.join(HERE, "icon-1024.png"), optimize=True)
www = os.path.join(HERE, "..", "..", "src", "App", "wwwroot")
images[256].save(os.path.join(www, "icon-256.png"), optimize=True)
images[32].save(os.path.join(www, "icon-32.png"), optimize=True)
print("Icones ecrites avec la police", FONT)
