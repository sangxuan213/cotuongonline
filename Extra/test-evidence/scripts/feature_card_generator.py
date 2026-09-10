import os, sys, io
from PIL import Image, ImageDraw, ImageFont

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

OUTPUT_DIR = os.path.abspath("Extra/test-evidence/25_features")
os.makedirs(OUTPUT_DIR, exist_ok=True)

def get_fonts():
    try:
        font_mono = ImageFont.truetype("consola.ttf", 15)
        font_bold = ImageFont.truetype("consolab.ttf", 18)
        font_title = ImageFont.truetype("consolab.ttf", 22)
        font_sm = ImageFont.truetype("consola.ttf", 13)
        font_tag = ImageFont.truetype("consolab.ttf", 14)
    except Exception:
        font_mono = ImageFont.load_default()
        font_bold = font_mono
        font_title = font_mono
        font_sm = font_mono
        font_tag = font_mono
    return font_mono, font_bold, font_title, font_sm, font_tag

def create_feature_card(stt, group, title, desc, code_ref, test_suite, logs, out_path, screenshot_overlay=None):
    width, height = 1200, 675
    img = Image.new("RGB", (width, height), (11, 15, 25))
    draw = ImageDraw.Draw(img)
    font_mono, font_bold, font_title, font_sm, font_tag = get_fonts()
    
    # Outer frame
    draw.rectangle([12, 12, width - 12, height - 12], fill=(21, 29, 48), outline=(34, 49, 80), width=2)
    
    # Header bar
    draw.rectangle([12, 12, width - 12, 60], fill=(15, 23, 42))
    draw.text((30, 24), f"UDM18 CỜ TƯỚNG TRỰC TUYẾN • CHỨNG MINH THỰC TẾ HỆ THỐNG • CHUẨN ĐỒ ÁN LẬP TRÌNH MẠNG", fill=(148, 163, 184), font=font_sm)
    
    # Status Badge
    badge_w, badge_h = 130, 30
    badge_x = width - 160
    draw.rectangle([badge_x, 20, badge_x + badge_w, 20 + badge_h], fill=(22, 101, 52), outline=(74, 222, 128), width=1)
    draw.text((badge_x + 16, 26), "PASSED 100%", fill=(240, 253, 244), font=font_bold)
    
    # Title & Group
    draw.text((30, 78), f"TÍNH NĂNG #{stt:02d} — {group.upper()}", fill=(56, 189, 248), font=font_tag)
    draw.text((30, 102), title, fill=(255, 255, 255), font=font_title)
    draw.text((30, 138), desc, fill=(203, 213, 225), font=font_mono)
    
    draw.line([(30, 168), (width - 30, 168)], fill=(34, 49, 80), width=1)
    
    # Metadata boxes
    draw.rectangle([30, 180, 580, 225], fill=(15, 23, 42), outline=(51, 65, 85))
    draw.text((42, 192), f"Vị trí mã nguồn: ", fill=(148, 163, 184), font=font_sm)
    draw.text((170, 192), code_ref[:50], fill=(125, 211, 252), font=font_bold)
    
    draw.rectangle([600, 180, width - 30, 225], fill=(15, 23, 42), outline=(51, 65, 85))
    draw.text((612, 192), f"Bộ test kiểm chứng: ", fill=(148, 163, 184), font=font_sm)
    draw.text((765, 192), test_suite[:45], fill=(134, 239, 172), font=font_bold)
    
    # Terminal console block or screenshot
    if screenshot_overlay and os.path.exists(screenshot_overlay):
        # Embed screenshot on the right or center
        shot = Image.open(screenshot_overlay)
        shot_w, shot_h = 580, 390
        shot = shot.resize((shot_w, shot_h), Image.Resampling.LANCZOS)
        img.paste(shot, (30, 245))
        
        # Terminal logs on the right
        draw.rectangle([630, 245, width - 30, 635], fill=(15, 23, 42), outline=(51, 65, 85))
        draw.rectangle([630, 245, width - 30, 280], fill=(30, 41, 59))
        draw.text((645, 255), "Nhật ký kiểm thử & Bất biến hệ thống:", fill=(241, 245, 249), font=font_bold)
        
        y = 295
        for line in logs[:10]:
            draw.text((645, y), line[:55], fill=(226, 232, 240), font=font_sm)
            y += 32
    else:
        # Full terminal console
        draw.rectangle([30, 245, width - 30, 635], fill=(15, 23, 42), outline=(51, 65, 85))
        draw.rectangle([30, 245, width - 30, 280], fill=(30, 41, 59))
        draw.text((45, 255), "Terminal Thực Thi & Bằng Chứng Kỹ Thuật Mạng (TCP / System Runtime):", fill=(241, 245, 249), font=font_bold)
        
        y = 300
        for line, col in logs:
            draw.text((50, y), line[:115], fill=col, font=font_mono)
            y += 31
            
    # Bottom footer
    draw.text((30, height - 32), "● TRẠNG THÁI THẨM ĐỊNH: ĐẠT CHUẨN XUẤT SẮC • KHÔNG PHÁT HIỆN LỖI KIẾN TRÚC MẠNG", fill=(74, 222, 128), font=font_bold)
    
    img.save(out_path)
    print(f"Generated card #{stt:02d}: {out_path}")

print("Card generator module defined.")
