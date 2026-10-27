import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

def create_claude_xo_icon():
    SIZE = 1024
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    cx, cy = SIZE // 2, SIZE // 2

    # Layer 1: Ambient warm terracotta & emerald radial aura (pure transparent background)
    aura_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_aura = ImageDraw.Draw(aura_layer)
    draw_aura.ellipse([cx - 420, cy - 420, cx + 420, cy + 420], fill=(217, 119, 87, 45))
    draw_aura.ellipse([cx - 280, cy - 280, cx + 280, cy + 280], fill=(16, 185, 129, 35))
    aura_layer = aura_layer.filter(ImageFilter.GaussianBlur(50))
    img = Image.alpha_composite(img, aura_layer)

    # Layer 2: Authentic Anthropic 14-lobed Sunburst Asterisk (Canonical Claude Emblem)
    # The Anthropic asterisk has 14 rounded radiating lobes with organic thickness
    sunburst_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_sb = ImageDraw.Draw(sunburst_layer)

    num_lobes = 14
    outer_radius = 420
    inner_radius = 170

    for i in range(num_lobes):
        angle = (i * 2 * math.pi / num_lobes) - (math.pi / 2)
        # Each lobe is drawn as a rounded organic pill radiating outward
        lobe_len = outer_radius - inner_radius
        start_x = cx + math.cos(angle) * inner_radius
        start_y = cy + math.sin(angle) * inner_radius
        end_x = cx + math.cos(angle) * outer_radius
        end_y = cy + math.sin(angle) * outer_radius
        
        # Pill width is thicker in the middle
        pill_w = 44
        draw_sb.line([(start_x, start_y), (end_x, end_y)], fill=(217, 119, 87, 255), width=pill_w)
        # Rounded caps
        draw_sb.ellipse([end_x - pill_w // 2, end_y - pill_w // 2, end_x + pill_w // 2, end_y + pill_w // 2], fill=(217, 119, 87, 255))

    # Inner core connecting circle of the Claude sunburst
    draw_sb.ellipse([cx - inner_radius - 10, cy - inner_radius - 10, cx + inner_radius + 10, cy + inner_radius + 10], fill=(217, 119, 87, 255))
    
    # Soft glow around the Claude sunburst
    sb_glow = sunburst_layer.filter(ImageFilter.GaussianBlur(16))
    img = Image.alpha_composite(img, sb_glow)
    img = Image.alpha_composite(img, sunburst_layer)

    # Layer 3: Central Deep Obsidian Medallion for "XO // BZ"
    core_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_core = ImageDraw.Draw(core_layer)
    core_r = 175
    draw_core.ellipse([cx - core_r, cy - core_r, cx + core_r, cy + core_r], fill=(15, 17, 23, 245))
    # Double ring edge (Terracotta & Emerald)
    draw_core.ellipse([cx - core_r, cy - core_r, cx + core_r, cy + core_r], outline=(217, 119, 87, 220), width=6)
    draw_core.ellipse([cx - core_r + 8, cy - core_r + 8, cx + core_r - 8, cy + core_r - 8], outline=(16, 185, 129, 180), width=3)
    img = Image.alpha_composite(img, core_layer)

    # Layer 4: Interlocking Metallic "XO // BZ" Monogram
    xo_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_xo = ImageDraw.Draw(xo_layer)

    # The "O" Shield Ring
    o_rad = 100
    draw_xo.ellipse([cx - o_rad, cy - o_rad, cx + o_rad, cy + o_rad], outline=(245, 245, 247, 240), width=16)
    draw_xo.ellipse([cx - o_rad + 4, cy - o_rad + 4, cx + o_rad - 4, cy + o_rad - 4], outline=(217, 119, 87, 200), width=4)

    # The "X" Cross Blades
    arm = 90
    xw = 18
    draw_xo.line([(cx - arm, cy - arm), (cx + arm, cy + arm)], fill=(245, 245, 247, 250), width=xw)
    draw_xo.line([(cx - arm, cy + arm), (cx + arm, cy - arm)], fill=(245, 245, 247, 250), width=xw)

    # Emerald / Terracotta highlights on tips
    tip = 30
    draw_xo.line([(cx - arm, cy - arm), (cx - arm + tip, cy - arm + tip)], fill=(217, 119, 87, 255), width=xw)
    draw_xo.line([(cx + arm - tip, cy + arm - tip), (cx + arm, cy + arm)], fill=(217, 119, 87, 255), width=xw)
    draw_xo.line([(cx - arm, cy + arm), (cx - arm + tip, cy + arm - tip)], fill=(16, 185, 129, 255), width=xw)
    draw_xo.line([(cx + arm - tip, cy - arm + tip), (cx + arm, cy - arm)], fill=(16, 185, 129, 255), width=xw)

    # Central mini glowing emerald jewel
    draw_xo.ellipse([cx - 14, cy - 14, cx + 14, cy + 14], fill=(16, 185, 129, 255))
    draw_xo.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=(255, 255, 255, 240))

    img = Image.alpha_composite(img, xo_layer)

    # Layer 5: "BZ" (Bogdan Zhukov) signature at bottom of core medallion
    try:
        font = ImageFont.truetype("arialbd.ttf", 34)
    except:
        font = ImageFont.load_default()

    draw_sig = ImageDraw.Draw(img)
    sig_text = "BZ"
    bbox = draw_sig.textbbox((0, 0), sig_text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw_sig.text((cx - tw // 2, cy + core_r - 42), sig_text, font=font, fill=(217, 119, 87, 230))

    # Save Master PNG (1024x1024, 100% transparent background)
    base_dir = r"C:\Users\bbara\.gemini\antigravity\scratch\project_xo"
    master_png = os.path.join(base_dir, "app_icon.png")
    img.save(master_png, "PNG")
    print(f"[+] Master transparent PNG saved to: {master_png}")

    # Generate Multi-Resolution ICO with TRUE RGBA TRANSPARENCY
    # Windows standard icon sizes: 16, 24, 32, 48, 64, 128, 256
    sizes = [16, 24, 32, 48, 64, 128, 256]
    ico_images = []
    for s in sizes:
        resized = img.resize((s, s), Image.Resampling.LANCZOS)
        ico_images.append(resized)

    ico_path = os.path.join(base_dir, "app_icon.ico")
    # Save first frame (256x256) with remaining frames appended
    ico_images[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in sizes])
    print(f"[+] Multi-resolution transparent ICO saved to: {ico_path}")

    # Generate Android launcher icons for all density buckets
    android_res = os.path.join(base_dir, "android_app", "res")
    densities = {
        "drawable": 96,
        "mipmap-mdpi": 48,
        "mipmap-hdpi": 72,
        "mipmap-xhdpi": 96,
        "mipmap-xxhdpi": 144,
        "mipmap-xxxhdpi": 192,
    }
    for folder, dim in densities.items():
        f_path = os.path.join(android_res, folder)
        os.makedirs(f_path, exist_ok=True)
        icon_file = os.path.join(f_path, "ic_launcher.png")
        resized_icon = img.resize((dim, dim), Image.Resampling.LANCZOS)
        resized_icon.save(icon_file, "PNG")
        print(f"[+] Android {folder}/ic_launcher.png ({dim}x{dim}) saved")

    # Also save to android/ directory for zip
    android_dir = os.path.join(base_dir, "android")
    os.makedirs(android_dir, exist_ok=True)
    img.resize((192, 192), Image.Resampling.LANCZOS).save(os.path.join(android_dir, "app_icon.png"), "PNG")

if __name__ == "__main__":
    create_claude_xo_icon()
