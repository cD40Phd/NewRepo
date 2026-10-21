"""Рисует иконку TypeStrike: клавиша с буквой «Т», в которую снизу стреляет корабль.

Запуск: python tools/make_icon.py  (нужен Pillow). Пишет Assets/TypeStrike.ico и .png.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 1024
OUT = Path(__file__).resolve().parent.parent / "TypeStrike" / "Assets"
CYAN, ORANGE, NAVY = (92, 225, 255), (255, 159, 28), (8, 12, 34)


def font(size):
    for name in ("DejaVuSans-Bold.ttf", "arialbd.ttf", "consolab.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def draw():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # Фон: скруглённый квадрат с вертикальным градиентом
    bg = Image.new("RGBA", (S, S))
    px = bg.load()
    for y in range(S):
        t = y / S
        c = tuple(int(a + (b - a) * t) for a, b in zip((14, 20, 58), (3, 4, 14)))
        for x in range(S):
            px[x, y] = c + (255,)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((24, 24, S - 24, S - 24), radius=200, fill=255)
    img.paste(bg, (0, 0), mask)

    # Свечение
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.rounded_rectangle((250, 150, 774, 610), radius=80, outline=CYAN + (255,), width=60)
    g.line((512, 640, 512, 780), fill=ORANGE + (255,), width=60)
    glow = glow.filter(ImageFilter.GaussianBlur(40))
    img.alpha_composite(glow)

    d = ImageDraw.Draw(img)
    # Клавиша с буквой
    d.rounded_rectangle((250, 150, 774, 610), radius=80, fill=(20, 34, 70, 255), outline=CYAN + (255,), width=34)
    f = font(400)
    box = d.textbbox((0, 0), "T", font=f)
    tw, th = box[2] - box[0], box[3] - box[1]
    d.text((512 - tw / 2 - box[0], 380 - th / 2 - box[1]), "T", font=f, fill=ORANGE + (255,))

    # Лазер и корабль
    d.line((512, 640, 512, 760), fill=(255, 220, 150, 255), width=34)
    d.polygon([(512, 740), (650, 920), (512, 870), (374, 920)], fill=(26, 42, 68, 255), outline=CYAN + (255,))
    d.line([(512, 740), (650, 920), (512, 870), (374, 920), (512, 740)], fill=CYAN + (255,), width=26, joint="curve")
    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    img = draw()
    img.resize((256, 256), Image.LANCZOS).save(OUT / "TypeStrike.png")
    img.save(OUT / "TypeStrike.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


if __name__ == "__main__":
    main()
