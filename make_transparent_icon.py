import os
import math
from PIL import Image, ImageDraw, ImageFilter

def create_claude_prohibition_icon():
    base_dir = r"C:\Users\bbara\.gemini\antigravity\scratch\project_xo"
    claude_raw = r"C:\Program Files\WindowsApps\Claude_3.6556.2.0_x64__pzs8sxrjxfjjc\assets\Square310x310Logo.scale-400.png"

    if os.path.exists(claude_raw):
        raw_im = Image.open(claude_raw).convert("RGBA")
        bbox = raw_im.getbbox()
        sunburst = raw_im.crop(bbox) if bbox else raw_im
    else:
        # Fallback to authentic procedural Claude sunburst if path is missing
        sunburst = Image.new("RGBA", (620, 620), (0, 0, 0, 0))
        draw_sb = ImageDraw.Draw(sunburst)
        cx, cy = 310, 310
        num_lobes = 14
        outer_radius = 280
        inner_radius = 110
        for i in range(num_lobes):
            angle = (i * 2 * math.pi / num_lobes) - (math.pi / 2)
            sx = cx + math.cos(angle) * inner_radius
            sy = cy + math.sin(angle) * inner_radius
            ex = cx + math.cos(angle) * outer_radius
            ey = cy + math.sin(angle) * outer_radius
            draw_sb.line([(sx, sy), (ex, ey)], fill=(217, 119, 87, 255), width=32)
            draw_sb.ellipse([ex - 16, ey - 16, ex + 16, ey + 16], fill=(217, 119, 87, 255))
        draw_sb.ellipse([cx - inner_radius, cy - inner_radius, cx + inner_radius, cy + inner_radius], fill=(217, 119, 87, 255))

    SIZE = 1024
    master = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # 1. Claude squircle background (#F3E9D7)
    pad = 48
    bg_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_bg = ImageDraw.Draw(bg_layer)
    draw_bg.rounded_rectangle([pad, pad, SIZE - pad, SIZE - pad], radius=210, fill=(243, 233, 215, 255))
    master = Image.alpha_composite(master, bg_layer)

    # 2. Authentic sunburst in center
    sb_size = 640
    sunburst_scaled = sunburst.resize((sb_size, sb_size), Image.Resampling.LANCZOS)
    sb_pos = ((SIZE - sb_size) // 2, (SIZE - sb_size) // 2)
    master.paste(sunburst_scaled, sb_pos, sunburst_scaled)

    # 3. Red prohibition symbol (crossed circle)
    overlay = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_p = ImageDraw.Draw(overlay)
    cx, cy = SIZE // 2, SIZE // 2
    pr_radius = 370
    stroke_w = 64

    # Red ring (ISO red: #DC2626 -> (220, 38, 38))
    draw_p.ellipse([cx - pr_radius, cy - pr_radius, cx + pr_radius, cy + pr_radius], outline=(220, 38, 38, 255), width=stroke_w)

    # Diagonal bar from top-left to bottom-right (45 degrees)
    diag_off = int(pr_radius * math.cos(math.pi / 4))
    draw_p.line([(cx - diag_off, cy - diag_off), (cx + diag_off, cy + diag_off)], fill=(220, 38, 38, 255), width=stroke_w)

    # Soft ambient drop shadow for realistic 3D appearance
    shadow = overlay.filter(ImageFilter.GaussianBlur(10))
    master = Image.alpha_composite(master, shadow)
    master = Image.alpha_composite(master, overlay)

    # Save Master PNG
    master_png = os.path.join(base_dir, "app_icon.png")
    master.save(master_png, "PNG")
    print(f"[+] Master Claude Prohibition PNG saved: {master_png}")

    # Generate Multi-Resolution Windows ICO
    sizes = [16, 24, 32, 48, 64, 128, 256]
    ico_images = []
    for s in sizes:
        resized = master.resize((s, s), Image.Resampling.LANCZOS)
        ico_images.append(resized)

    ico_path = os.path.join(base_dir, "app_icon.ico")
    ico_images[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in sizes])
    print(f"[+] Multi-resolution Windows ICO saved: {ico_path}")

    # Android launcher icons
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
        resized_icon = master.resize((dim, dim), Image.Resampling.LANCZOS)
        resized_icon.save(icon_file, "PNG")
        print(f"[+] Android {folder}/ic_launcher.png ({dim}x{dim}) saved")

    # Android zip folder
    android_dir = os.path.join(base_dir, "android")
    os.makedirs(android_dir, exist_ok=True)
    master.resize((192, 192), Image.Resampling.LANCZOS).save(os.path.join(android_dir, "app_icon.png"), "PNG")

if __name__ == "__main__":
    create_claude_prohibition_icon()
