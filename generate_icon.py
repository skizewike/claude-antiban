from PIL import Image, ImageDraw, ImageFilter, ImageFont
import math

SIZE = 512
img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# 1. Dark squircle / rounded shield background with subtle radial gradient
pad = 24
sq_box = [pad, pad, SIZE - pad, SIZE - pad]
radius = 110

# Base background mask
bg_mask = Image.new("L", (SIZE, SIZE), 0)
draw_mask = ImageDraw.Draw(bg_mask)
draw_mask.rounded_rectangle(sq_box, radius=radius, fill=255)

# Gradient background (Obsidian to deep slate)
bg_grad = Image.new("RGBA", (SIZE, SIZE), (11, 14, 20, 255))
draw_grad = ImageDraw.Draw(bg_grad)
for y in range(SIZE):
    ratio = y / SIZE
    r = int(14 + ratio * 12)
    g = int(18 + ratio * 15)
    b = int(26 + ratio * 22)
    draw_grad.line([(0, y), (SIZE, y)], fill=(r, g, b, 255))

img.paste(bg_grad, (0, 0), bg_mask)

# 2. Subtle glowing perimeter rim
rim_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw_rim = ImageDraw.Draw(rim_layer)
draw_rim.rounded_rectangle(sq_box, radius=radius, outline=(217, 119, 87, 180), width=4)
# Soft glow
glow_layer = rim_layer.filter(ImageFilter.GaussianBlur(8))
img = Image.alpha_composite(img, glow_layer)
img = Image.alpha_composite(img, rim_layer)

# 3. Outer Cyber Ring with ticks
cx, cy = SIZE // 2, SIZE // 2
ring_r = 175
draw = ImageDraw.Draw(img)
draw.ellipse([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r], outline=(40, 52, 75, 200), width=3)

# 4. Central Shield Outline
shield_w, shield_h = 240, 280
shield_pts = [
    (cx, cy - shield_h // 2),
    (cx + shield_w // 2, cy - shield_h // 3),
    (cx + shield_w // 2, cy + shield_h // 5),
    (cx, cy + shield_h // 2),
    (cx - shield_w // 2, cy + shield_h // 5),
    (cx - shield_w // 2, cy - shield_h // 3)
]

shield_mask = Image.new("L", (SIZE, SIZE), 0)
draw_smask = ImageDraw.Draw(shield_mask)
draw_smask.polygon(shield_pts, fill=255)

shield_surface = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw_ssurf = ImageDraw.Draw(shield_surface)
for y in range(cy - shield_h // 2, cy + shield_h // 2 + 1):
    ratio = (y - (cy - shield_h // 2)) / shield_h
    r = int(24 + (1 - ratio) * 25)
    g = int(32 + ratio * 20)
    b = int(48 + ratio * 35)
    draw_ssurf.line([(0, y), (SIZE, y)], fill=(r, g, b, 220))

img.paste(shield_surface, (0, 0), shield_mask)

# Shield border glow (Terracotta / Emerald)
draw.polygon(shield_pts, outline=(217, 119, 87, 255))

# 5. Interlocking "XO // BZ" Metallic Monogram
# The "O" ring
o_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw_o = ImageDraw.Draw(o_layer)
o_r = 85
draw_o.ellipse([cx - o_r, cy - o_r, cx + o_r, cy + o_r], outline=(217, 119, 87, 240), width=18)
# Subtle inner emerald ring
draw_o.ellipse([cx - o_r + 14, cy - o_r + 14, cx + o_r - 14, cy + o_r - 14], outline=(16, 185, 129, 140), width=3)
img = Image.alpha_composite(img, o_layer)

# The "X" crossing blades
x_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw_x = ImageDraw.Draw(x_layer)
arm = 75
width_x = 16

# Draw diagonal 1
draw_x.line([(cx - arm, cy - arm), (cx + arm, cy + arm)], fill=(245, 245, 247, 250), width=width_x)
# Draw diagonal 2
draw_x.line([(cx - arm, cy + arm), (cx + arm, cy - arm)], fill=(245, 245, 247, 250), width=width_x)

# Terracotta / Titanium caps
draw_x.line([(cx - arm, cy - arm), (cx - arm + 25, cy - arm + 25)], fill=(217, 119, 87, 255), width=width_x)
draw_x.line([(cx + arm - 25, cy + arm - 25), (cx + arm, cy + arm)], fill=(217, 119, 87, 255), width=width_x)
draw_x.line([(cx - arm, cy + arm), (cx - arm + 25, cy + arm - 25)], fill=(16, 185, 129, 255), width=width_x)
draw_x.line([(cx + arm - 25, cy - arm + 25), (cx + arm, cy - arm)], fill=(16, 185, 129, 255), width=width_x)

img = Image.alpha_composite(img, x_layer)

# Central Anthropic ✶ Star Core
star_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw_star = ImageDraw.Draw(star_layer)
rays = 14
inner_r = 12
outer_r = 30
star_pts = []
for i in range(rays * 2):
    angle = (i * math.pi / rays) - (math.pi / 2)
    r = outer_r if (i % 2 == 0) else inner_r
    star_pts.append((cx + math.cos(angle) * r, cy + math.sin(angle) * r))
draw_star.polygon(star_pts, fill=(217, 119, 87, 255))
draw_star.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=(16, 185, 129, 255))

star_glow = star_layer.filter(ImageFilter.GaussianBlur(6))
img = Image.alpha_composite(img, star_glow)
img = Image.alpha_composite(img, star_layer)

# Bottom Monogram Text "BZ" (Bogdan Zhukov)
try:
    font = ImageFont.truetype("arialbd.ttf", 26)
except:
    font = ImageFont.load_default()

draw_text = ImageDraw.Draw(img)
text = "BZ"
bbox = draw_text.textbbox((0, 0), text, font=font)
tw = bbox[2] - bbox[0]
th = bbox[3] - bbox[1]
tx = cx - tw // 2
ty = cy + shield_h // 2 - 38
draw_text.text((tx, ty), text, font=font, fill=(217, 119, 87, 240))

# Save multi-size icons
output_png = r"C:\Users\bbara\.gemini\antigravity\scratch\project_xo\app_icon.png"
output_ico = r"C:\Users\bbara\.gemini\antigravity\scratch\project_xo\app_icon.ico"

img.save(output_png, "PNG")

# Create ICO with standard Windows sizes
img.save(output_ico, format="ICO", sizes=[
    (16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)
])

print("Icon created successfully at:")
print(output_png)
print(output_ico)
