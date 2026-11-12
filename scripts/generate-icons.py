"""Generate ApiNet's folded N as SVG, transparent PNG and ICO (Python 3 + Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "ApiNet.UI" / "Assets"
SCALE = 4

# Three joined ribbon faces form Net's initial. Use shared geometry for all
# exports; the surrounding space and the openings remain genuinely transparent.
PANELS = [
    ("#0077B8", [(48, 104), (144, 48), (144, 408), (48, 464)]),
    ("#009DE6", [(48, 104), (144, 48), (464, 408), (368, 464)]),
    ("#23B8F2", [(368, 104), (464, 48), (464, 408), (368, 464)]),
]


def generate():
    canvas = Image.new("RGBA", (512 * SCALE, 512 * SCALE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    svg = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">',
        '  <title>ApiNet Folded N</title>',
    ]
    for color, points in PANELS:
        draw.polygon([(x * SCALE, y * SCALE) for x, y in points], fill=color)
        coordinates = " ".join(f"{x},{y}" for x, y in points)
        svg.append(f'  <polygon points="{coordinates}" fill="{color}"/>')
    svg.append("</svg>")
    (ASSETS / "app.svg").write_text("\n".join(svg) + "\n", encoding="utf-8")
    icon = canvas.resize((1024, 1024), Image.Resampling.LANCZOS)
    icon.save(ASSETS / "app.png")
    icon.save(ASSETS / "app.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    assert icon.getpixel((0, 0))[3] == 0
    print("Generated folded N SVG, 1024px transparent PNG and nine-size ICO.")


if __name__ == "__main__":
    generate()
