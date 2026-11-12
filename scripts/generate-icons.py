"""Generate ApiNet's puppy SVG, transparent PNG and Windows ICO (Python 3 + Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "ApiNet.UI" / "Assets"
SCALE = 4
BLUE, CREAM, CARAMEL, INK = "#1684D3", "#FFF1D6", "#C98D57", "#29384B"


def generate():
    canvas = Image.new("RGBA", (512 * SCALE, 512 * SCALE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    svg = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">',
        '  <title>ApiNet Puppy</title>',
        f'  <rect x="32" y="32" width="448" height="448" rx="104" fill="{BLUE}"/>',
    ]
    draw.rounded_rectangle((32 * SCALE, 32 * SCALE, 480 * SCALE, 480 * SCALE), radius=104 * SCALE, fill=BLUE)

    def path(operations, fill=None, stroke=None, width=1):
        # Shared geometry keeps the SVG and raster exports in sync. Sample cubic
        # curves at high resolution before downscaling with alpha preserved.
        points, commands = [], []
        current = start = (0, 0)
        for operation in operations:
            command, values = operation[0], operation[1:]
            commands.append(command + " " + ",".join(map(str, values)))
            if command == "M":
                current = start = tuple(values)
                points.append(current)
            elif command == "L":
                current = tuple(values)
                points.append(current)
            elif command == "C":
                p0, p1, p2, p3 = current, values[:2], values[2:4], values[4:6]
                for step in range(1, 49):
                    t = step / 48
                    u = 1 - t
                    points.append(tuple(u**3 * p0[j] + 3*u*u*t*p1[j] + 3*u*t*t*p2[j] + t**3*p3[j] for j in (0, 1)))
                current = tuple(p3)
            elif command == "Z":
                points.append(start)
                current = start
        scaled = [(round(x * SCALE), round(y * SCALE)) for x, y in points]
        if fill:
            draw.polygon(scaled, fill=fill)
        if stroke:
            draw.line(scaled, fill=stroke, width=width * SCALE, joint="curve")
            radius = width * SCALE / 2
            for x, y in (scaled[0], scaled[-1]):
                draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=stroke)
        svg.append(f'  <path d="{" ".join(commands)}" fill="{fill or "none"}" stroke="{stroke or "none"}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>')

    def eye(x, y):
        radius = 15
        draw.ellipse(((x-radius)*SCALE, (y-radius)*SCALE, (x+radius)*SCALE, (y+radius)*SCALE), fill=INK)
        svg.append(f'  <circle cx="{x}" cy="{y}" r="{radius}" fill="{INK}"/>')

    path([("M", 181, 178), ("C", 162, 146, 127, 153, 111, 190), ("C", 94, 233, 94, 296, 118, 312), ("C", 136, 324, 154, 312, 164, 288), ("L", 191, 208), ("Z",)], fill=CARAMEL)
    path([("M", 331, 178), ("C", 350, 146, 385, 153, 401, 190), ("C", 418, 233, 418, 296, 394, 312), ("C", 376, 324, 358, 312, 348, 288), ("L", 321, 208), ("Z",)], fill=CARAMEL)
    path([("M", 166, 193), ("C", 174, 155, 214, 143, 256, 147), ("C", 298, 143, 338, 155, 346, 193), ("C", 384, 232, 379, 301, 350, 334), ("C", 326, 361, 287, 374, 256, 374), ("C", 225, 374, 186, 361, 162, 334), ("C", 133, 301, 128, 232, 166, 193), ("Z",)], fill=CREAM)
    eye(207, 259)
    eye(305, 259)
    path([("M", 240, 293), ("C", 238, 283, 274, 283, 272, 293), ("C", 270, 302, 260, 309, 256, 309), ("C", 252, 309, 242, 302, 240, 293), ("Z",)], fill=INK)
    path([("M", 256, 307), ("L", 256, 318)], stroke=INK, width=7)
    path([("M", 234, 317), ("C", 238, 330, 250, 330, 256, 318), ("C", 262, 330, 274, 330, 278, 317)], stroke=INK, width=7)
    svg.append("</svg>")
    (ASSETS / "app.svg").write_text("\n".join(svg) + "\n", encoding="utf-8")
    icon = canvas.resize((1024, 1024), Image.Resampling.LANCZOS)
    icon.save(ASSETS / "app.png")
    sizes = [(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
    icon.save(ASSETS / "app.ico", sizes=sizes)
    assert icon.getpixel((0, 0))[3] == 0
    print("Generated puppy SVG, 1024px transparent PNG and nine-size ICO.")


if __name__ == "__main__":
    generate()
