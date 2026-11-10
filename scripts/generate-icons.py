"""Generate the transparent SVG, PNG and multi-size Windows ICO (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent
assets = root / "src" / "ApiNet.UI" / "Assets"
orange = "#F1844C"
# One central hub and three connected peers: a network rather than code brackets.
center = (256, 256)
peers = [(256, 128), (367, 320), (145, 320)]
svg_paths = "\n".join(f'  <path d="M {center[0]} {center[1]} L {x} {y}" />' for x, y in peers)
svg_nodes = "\n".join(f'  <circle cx="{x}" cy="{y}" r="29" />' for x, y in peers)
svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
  <title>ApiNet Network</title>
  <rect x="32" y="32" width="448" height="448" rx="112" fill="{orange}" />
  <g fill="none" stroke="#FFFFFF" stroke-width="26" stroke-linecap="round">
{svg_paths}
  </g>
  <circle cx="256" cy="256" r="49" fill="{orange}" stroke="#FFFFFF" stroke-width="26" />
  <g fill="#FFFFFF">
{svg_nodes}
  </g>
</svg>
'''
(assets / "app.svg").write_text(svg, encoding="utf-8")
scale = 4
canvas = Image.new("RGBA", (512 * scale, 512 * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(canvas)
draw.rounded_rectangle((32 * scale, 32 * scale, 480 * scale, 480 * scale), radius=112 * scale, fill=orange)
for x, y in peers:
    draw.line((center[0] * scale, center[1] * scale, x * scale, y * scale), fill="white", width=26 * scale)
draw.ellipse((194 * scale, 194 * scale, 318 * scale, 318 * scale), fill="white")
draw.ellipse((220 * scale, 220 * scale, 292 * scale, 292 * scale), fill=orange)
for x, y in peers:
    draw.ellipse(((x - 29) * scale, (y - 29) * scale, (x + 29) * scale, (y + 29) * scale), fill="white")
icon = canvas.resize((1024, 1024), Image.Resampling.LANCZOS)
icon.save(assets / "app.png")
icon.save(assets / "app.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
assert icon.getpixel((0, 0))[3] == 0
print("Generated transparent app.svg, 1024px PNG and nine-size ICO.")
