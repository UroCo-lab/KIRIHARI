"""Generate the two bundled image fixtures (requires Pillow)."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


OUT = Path(__file__).resolve().parent / "lightweight" / "images"
OUT.mkdir(parents=True, exist_ok=True)

badge = Image.new("RGBA", (384, 216))
draw = ImageDraw.Draw(badge)
draw.rounded_rectangle((20, 48, 364, 168), radius=32, fill=(115, 47, 154, 200))
draw.rounded_rectangle((28, 56, 356, 160), radius=24, outline=(255, 209, 241, 245), width=3)
draw.ellipse((48, 83, 98, 133), fill=(255, 209, 241, 255))
draw.polygon([(66, 95), (66, 122), (86, 108)], fill=(115, 47, 154, 255))
draw.text((117, 77), "KIRIHARI", font=ImageFont.load_default(size=32), fill="white")
draw.text((118, 119), "PNG  /  ALPHA SAMPLE", font=ImageFont.load_default(size=17), fill="white")
badge.save(OUT / "transparent-badge.png", optimize=True)

card = Image.new("RGB", (480, 270), "#193943")
draw = ImageDraw.Draw(card)
draw.rectangle((0, 0, 479, 13), fill="#53c8b8")
draw.rounded_rectangle((22, 34, 458, 236), radius=17, fill="#eff7f3")
draw.text((45, 54), "KIRIHARI", font=ImageFont.load_default(size=38), fill="#193943")
draw.text((47, 103), "JPEG  /  COLOR SAMPLE", font=ImageFont.load_default(size=21), fill="#365b63")
for index, color in enumerate(("#0d5663", "#36b1a0", "#f4c267", "#ec795f", "#694485")):
    x = 47 + 78 * index
    draw.rounded_rectangle((x, 152, x + 69, 212), radius=8, fill=color)
card.save(OUT / "color-card.jpg", quality=88, optimize=True)
