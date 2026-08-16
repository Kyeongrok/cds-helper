# -*- coding: utf-8 -*-
"""
MPCG.CDS 에서 술집·여관 손님 그림을 떠서 asset/guest/*.png 로 저장한다.

MPCG.CDS 는 두 가지를 함께 담고 있다.

    파트 0        320x240   타원 테두리 선
    파트 1        320x240   타원 채움 마스크 (63 안 / 47 밖)
    파트 2k+2     320x240   타원 건물 사진 84장 (k = 0~83)
    파트 2k+3     768바이트  그 사진의 팔레트
    파트 170~315  손님 146명  ← 이 스크립트가 뜨는 것

팔레트를 색인대로 갈라 쓴다. **0~63 이 손님 몫, 64~149 가 건물 사진 몫**이다.
그리고 0~63 구간은 84개 팔레트 전부에서 한 바이트도 다르지 않다 — 손님 색은 어느
사진과 함께 읽든 같다. 그래서 손님 파트에는 제 팔레트가 안 붙어 있고, 아무 사진
팔레트(여기서는 파트 3)의 앞 64색을 쓰면 된다.

손님은 **문화권 차례**로 놓여 있다.

    170~208  유럽          209~244  중근동·이슬람   245~250  아프리카
    251~259  인도          260~276  동아시아        277~289  동남아
    290~304  중남미        305~315  아메리카 원주민

크기는 거의 폭 64 인데 넷만 다르다 — 174(48) · 181(56) · 206(56) · 277(66).
전부 64 의 배수라 `길이 / 64` 로만 재면 이 넷이 어긋난 줄무늬가 된다. 그래서 파트마다
행 간 상관(윗줄과 아랫줄이 얼마나 닮나)으로 폭을 따로 잰다.

투명은 **색인 55** 다(건물 사진 쪽 64 와 다르다).

쓰는 법:
    python tools/extract_tavern_guests.py --game "C:\\...\\대항해시대3"

PIL(pillow) 이 있어야 한다.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ls12 import Ls12, palette                              # noqa: E402

try:
    from PIL import Image
except ImportError:
    sys.exit("pillow 가 필요하다:  pip install pillow")

FIRST_GUEST = 170       # 손님이 시작되는 파트
PALETTE_PART = 3        # 손님 색(0~63)은 어느 사진 팔레트를 써도 같다
TRANSPARENT = 55        # 손님 그림의 비침 색인
WIDTHS = (40, 48, 56, 64, 66, 72, 80)

# 파트 번호 → 문화권. 시작 파트만 적는다.
SPHERES = [
    (170, "유럽"), (209, "중근동"), (245, "아프리카"), (251, "인도"),
    (260, "동아시아"), (277, "동남아"), (290, "중남미"), (305, "아메리카"),
]


def best_width(data):
    """
    행 간 상관으로 폭을 잰다. 진짜 폭에서는 윗줄과 아랫줄이 가장 닮으므로 값이 가장 낮다.
    크기가 64 의 배수라고 폭이 64 인 것은 아니다 — 넷이 그렇지 않다.
    """
    best, best_score = 64, float("inf")
    for w in WIDTHS:
        if len(data) % w:
            continue
        score = sum(abs(data[i] - data[i + w]) for i in range(len(data) - w))
        score /= len(data) - w
        if score < best_score:
            best, best_score = w, score
    return best


def sphere_of(part):
    name = SPHERES[0][1]
    for start, s in SPHERES:
        if part >= start:
            name = s
    return name


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="게임 폴더 (MPCG.CDS 가 있는 곳)")
    ap.add_argument("--out", default=None, help="저장할 폴더 (기본 asset/guest)")
    ap.add_argument("--sheet", action="store_true", help="한눈에 보는 모음 장도 함께 저장")
    args = ap.parse_args()

    path = os.path.join(args.game, "MPCG.CDS")
    if not os.path.exists(path):
        sys.exit("MPCG.CDS 가 없다: " + path)

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = args.out or os.path.join(repo, "asset", "guest")
    os.makedirs(out_dir, exist_ok=True)

    arc = Ls12.open(path)
    pal = palette(arc.decode(PALETTE_PART))

    saved, odd, images = 0, [], []
    for part in range(FIRST_GUEST, len(arc)):
        data = arc.decode(part)
        if data is None:
            print("  파트 %d 를 못 풀었다" % part)
            continue

        w = best_width(data)
        h = len(data) // w
        if w != 64:
            odd.append("%d(%dx%d)" % (part, w, h))

        img = Image.new("RGBA", (w, h))
        px = img.load()
        for y in range(h):
            row = y * w
            for x in range(w):
                i = data[row + x]
                px[x, y] = (0, 0, 0, 0) if i == TRANSPARENT else pal[i] + (255,)

        n = part - FIRST_GUEST
        img.save(os.path.join(out_dir, "guest-%03d.png" % n))
        images.append(img)
        saved += 1

    print("손님 %d명을 %s 에 저장했다" % (saved, out_dir))
    if odd:
        print("폭이 64 가 아닌 것: " + " ".join(odd))

    if args.sheet:
        cols, cw, ch = 15, 74, 112
        rows = (len(images) + cols - 1) // cols
        sheet = Image.new("RGBA", (cols * cw, rows * ch), (30, 30, 34, 255))
        for k, im in enumerate(images):
            x = (k % cols) * cw + (cw - im.width) // 2
            y = (k // cols) * ch + (ch - im.height) // 2
            sheet.alpha_composite(im, (x, y))
        sheet_path = os.path.join(out_dir, "_sheet.png")
        sheet.save(sheet_path)
        print("모음 장: " + sheet_path)

    # 어느 번호가 어느 문화권인지 곁에 남긴다 — 번호만으로는 알 수 없다.
    with open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8") as f:
        f.write("# 술집·여관 손님\n\n")
        f.write("`MPCG.CDS` 파트 %d 부터를 뽑은 것이다. `tools/extract_tavern_guests.py` 가 만든다.\n\n"
                % FIRST_GUEST)
        f.write("손님 번호 = 파트 번호 - %d. 문화권 차례로 놓여 있다.\n\n" % FIRST_GUEST)
        f.write("| 손님 번호 | 파트 | 문화권 |\n|---|---|---|\n")
        for i, (start, name) in enumerate(SPHERES):
            end = SPHERES[i + 1][0] - 1 if i + 1 < len(SPHERES) else len(arc) - 1
            f.write("| %03d ~ %03d | %d ~ %d | %s |\n"
                    % (start - FIRST_GUEST, end - FIRST_GUEST, start, end, name))
        f.write("\n비침은 색인 55, 팔레트는 사진 팔레트의 앞 64색(바이트 차례 파랑·빨강·초록)이다.\n")


if __name__ == "__main__":
    main()
