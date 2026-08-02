import os
import resvg_py
from PIL import Image

def get_svg_content(transparent=False):
    width = 1024
    height = 1024
    bg_color = "#0A2540"
    primary_color = "#F2A900"
    accent_color = "#FFFFFF"
    
    bg_rect = "" if transparent else f'<rect x="64" y="64" width="896" height="896" rx="192" ry="192" fill="{bg_color}" />'
    
    # Masterpiece Vector Mark: Wheat Stalk + Integrated Bread Loaf
    # Canvas 1024x1024, Symbol bounds X: 210..814, Y: 145..840
    
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}">
  {bg_rect}
  <g id="bakery-erp-icon">
    
    <!-- 1. LOWER HALF: BREAD LOAF (Primary Golden #F2A900) -->
    <!-- Formed seamlessly by the lower body of the wheat stalk -->
    <!-- Base Y=840, Apex Y=490, X=210 to X=814 -->
    <path fill="{primary_color}" d="
      M 210 790
      C 210 460, 814 460, 814 790
      C 814 825, 784 840, 720 840
      L 304 840
      C 240 840, 210 825, 210 790
      Z
    " />
    
    <!-- 2. CENTRAL WHEAT STEM / RACHIS (Accent White #FFFFFF) -->
    <!-- Extends from top kernel tip (Y=160) down into the Bread Crown (Y=510) -->
    <rect x="500" y="160" width="24" height="350" rx="12" fill="{accent_color}" />
    
    <!-- 3. UPPER HALF: WHEAT GRAIN KERNELS (Primary Golden #F2A900) -->
    <!-- Top Central Terminal Grain Kernel -->
    <path fill="{primary_color}" d="
      M 512 145
      C 538 185, 542 235, 512 275
      C 482 235, 486 185, 512 145
      Z
    " />
    
    <!-- Grain Pair 1 (Top Left & Right) -->
    <path fill="{primary_color}" d="
      M 488 265
      C 430 225, 345 185, 305 165
      C 325 215, 385 265, 460 287
      Z
    " />
    <path fill="{primary_color}" d="
      M 536 265
      C 594 225, 679 185, 719 165
      C 699 215, 639 265, 564 287
      Z
    " />
    
    <!-- Grain Pair 2 (Middle Left & Right) -->
    <path fill="{primary_color}" d="
      M 488 355
      C 415 315, 305 270, 255 245
      C 277 300, 355 355, 444 380
      Z
    " />
    <path fill="{primary_color}" d="
      M 536 355
      C 609 315, 719 270, 769 245
      C 747 300, 669 355, 580 380
      Z
    " />
    
    <!-- Grain Pair 3 (Lower Left & Right - Resting gracefully on Bread Loaf Dome) -->
    <path fill="{primary_color}" d="
      M 488 445
      C 400 405, 275 355, 215 330
      C 240 390, 330 450, 430 475
      Z
    " />
    <path fill="{primary_color}" d="
      M 536 445
      C 624 405, 749 355, 809 330
      C 784 390, 694 450, 594 475
      Z
    " />

    <!-- 4. ARTISAN BREAD SCORE SLASHES (Accent White #FFFFFF) -->
    <!-- 3 Slashes angled at -40 deg, echoing the exact pitch of wheat grains -->
    <rect x="325" y="605" width="24" height="150" rx="12" fill="{accent_color}" transform="rotate(-40 325 605)" />
    <rect x="450" y="545" width="24" height="205" rx="12" fill="{accent_color}" transform="rotate(-40 450 545)" />
    <rect x="615" y="575" width="24" height="165" rx="12" fill="{accent_color}" transform="rotate(-40 615 575)" />

  </g>
</svg>'''
    return svg

def main():
    root_dir = r"c:\Users\Ahmed\OneDrive\Desktop\bakery"
    wpf_dir = r"c:\Users\Ahmed\OneDrive\Desktop\bakery\Bakery.WPF"
    
    svg_opaque = get_svg_content(transparent=False)
    svg_trans = get_svg_content(transparent=True)
    
    # 1. Save SVGs
    svg_path = os.path.join(root_dir, "BakeryERP.svg")
    svg_trans_path = os.path.join(root_dir, "BakeryERP_transparent.svg")
    
    with open(svg_path, "w", encoding="utf-8") as f:
        f.write(svg_opaque)
    with open(svg_trans_path, "w", encoding="utf-8") as f:
        f.write(svg_trans)
    print("Saved SVG files.")
    
    # 2. Render 1024x1024 PNGs using resvg
    png_bytes = resvg_py.svg_to_bytes(svg_opaque, width=1024, height=1024)
    png_1024_path = os.path.join(root_dir, "BakeryERP_1024.png")
    with open(png_1024_path, "wb") as f:
        f.write(png_bytes)
        
    png_trans_bytes = resvg_py.svg_to_bytes(svg_trans, width=1024, height=1024)
    png_trans_path = os.path.join(root_dir, "BakeryERP_transparent.png")
    with open(png_trans_path, "wb") as f:
        f.write(png_trans_bytes)
    print("Saved 1024 PNG files.")
    
    # 3. Create multi-size ICO file (16, 24, 32, 48, 64, 128, 256)
    img = Image.open(png_1024_path)
    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    
    ico_root_path = os.path.join(root_dir, "BakeryERP.ico")
    ico_wpf_path = os.path.join(wpf_dir, "BakeryERP.ico")
    
    img.save(ico_root_path, format="ICO", sizes=sizes)
    img.save(ico_wpf_path, format="ICO", sizes=sizes)
    print("Saved BakeryERP.ico in root and Bakery.WPF.")

if __name__ == "__main__":
    main()
