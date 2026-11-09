"""Generate the transparent SVG, PNG and multi-size Windows ICO (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent
assets = root / "src" / "ApiNet.UI" / "Assets"
orange = "#F1844C"
paths = [((176, 192), (112, 256), (176, 320)), ((286, 176), (226, 336)), ((336, 192), (400, 256), (336, 320))]
svg_paths = "\n".join('  <path d="M ' + " L ".join(f"{x} {y}" for x, y in points) + '" />' for points in paths)
svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
  <title>ApiNet</title>
  <rect x="32" y="32" width="448" height="448" rx="112" fill="{orange}" />
  <g fill="none" stroke="#FFFFFF" stroke-width="34" stroke-linecap="round" stroke-linejoin="round">
{svg_paths}
  </g>
</svg>
'''
(assets / "app.svg").write_text(svg, encoding="utf-8")
scale = 4
canvas = Image.new("RGBA", (512 * scale, 512 * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(canvas)
draw.rounded_rectangle((32 * scale, 32 * scale, 480 * scale, 480 * scale), radius=112 * scale, fill=orange)
for path in paths:
    points = [(x * scale, y * scale) for x, y in path]
    draw.line(points, fill="white", width=34 * scale, joint="curve")
    for x, y in points:
        radius = 17 * scale
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill="white")
icon = canvas.resize((1024, 1024), Image.Resampling.LANCZOS)
icon.save(assets / "app.png")
icon.save(assets / "app.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
assert icon.getpixel((0, 0))[3] == 0
print("Generated transparent app.svg, 1024px PNG and nine-size ICO.")
