# -*- coding: utf-8 -*-
"""
SCOMBAT.CDS — 해전 화면 그림을 떠서 asset/scombat/*.png 로 저장한다.

크기를 짐작하지 않는다. EXE 가 조각을 어디에 얹고 어떤 크기로 찍는지 그대로 옮겼다.
게임은 파트를 하나로 이어 붙인 버퍼에 풀어 놓고, 그 안의 자리를 상수로 들고 있다.

    0x00549A00  480000   파트 1 시작
    0x00549A04  521472   파트 1 + 41472
    0x00549A08  524544   파트 1 + 44544
    0x00549A0C  534784   파트 1 + 54784
    0x00549A10  534976   파트 2 시작
    0x00549A14  539072   파트 3 시작

찍는 자리(그리기는 0x004B5CB9(x, y, 폭, 높이, 조각))에서 크기가 나온다.

    0x004378BC  0x549A00 → 48 x 48   틀 2304, +0x6C00 부터
    0x0044006C  0x549A04 → 48 x 32   틀 1536
    0x00440326  0x549A08 → 32 x 32   틀 1024
    0x0043878B  0x549A0C →  8 x  8   틀 64
    0x00440B32  0x549A10 → 64 x 32
    0x00440BBE  0x549A14 → 640 x 32  (화면이 넓으면 800, 0x00440BA2 가 가른다)

그래서 조각이 이렇다.

    파트 0        800x600            바다 바탕 — 넓은 화면일 때 통째로 쓴다
    파트 1  +0        48x48 x 18     폭발·불길·잔해
            +41472    48x32 x  2     칸(마름모) — 빈 칸과 짚은 칸
            +44544    32x32 x 10     방향 화살표·작은 배·문장
            +54784     8x 8 x  3     작은 표시
    파트 2        64x32 x 2
    파트 3·4      640x32 · 800x32 띠  상단 정보 띠(화면 폭에 따라 갈린다)
    파트 5~12     48x48 x 12         배 여덟 벌 — 열두 방향
    파트 13~16    276,480 씩 넷      아직 안 짚었다
    파트 17       261바이트  87색 제 팔레트
    파트 18       768바이트  256색 (앞쪽만 값이 있다)
    파트 19·20    87,808 · 5,760

★ 색인 규칙은 BOOKSHEL.CDS 와 같다 — 74 밑은 공용 색표, 그 위는 파트 17 의 제 팔레트
  (한 색이 파랑·빨강·초록 순), **색인 160 이 비침**이다.

  다만 파트 0·3·4·19 는 색인이 160 을 넘는다 — 파트 17 로도 18 로도 안 풀린다.
  그 넷은 딴 그림(바다 쪽)의 팔레트를 얹어 쓰는 것으로 보이고 아직 못 짚었다.
"""
import argparse
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ls12 import Ls12                                       # noqa: E402

try:
    from PIL import Image
except ImportError:
    sys.exit("pillow 가 필요하다:  pip install pillow")

OWN_PALETTE_PART = 17
OWN_PALETTE_BASE = 74
TRANSPARENT = 160

# (파트, 시작, 폭, 높이, 이름) — 시작은 그 파트 안에서의 자리다.
PIECES = [
    (1, 0, 48, 48, "blast"),      # 폭발·불길·잔해 18장
    (1, 41472, 48, 32, "cell"),   # 칸(마름모) 2장
    (1, 44544, 32, 32, "mark"),   # 방향·작은 배·문장 10장
    (1, 54784, 8, 8, "dot"),      # 작은 표시 3장
    (2, 0, 64, 32, "pair"),
]

# 배 여덟 벌 — 한 벌이 열두 방향이다.
SHIP_PARTS = range(5, 13)
SHIP_W = SHIP_H = 48


def game_palette(repo):
    """앱의 GamePalette.cs 에서 공용 색표를 읽는다 — 값을 두 군데 두지 않으려고."""
    path = os.path.join(repo, "CdsHelper.Game", "Local", "Helpers", "GamePalette.cs")
    src = open(path, encoding="utf-8-sig").read()
    body = src.split("private static readonly byte[] Low =")[1].split("];")[0]
    nums = [int(x) for x in re.findall(r"\d+", body)]
    pal = [(nums[i * 3], nums[i * 3 + 1], nums[i * 3 + 2]) for i in range(len(nums) // 3)]
    return pal + [(255, 0, 255)] * (256 - len(pal))


def color(value, own, shared):
    """색인 하나를 색으로. 비침은 None."""
    if value == TRANSPARENT:
        return None
    k = (value - OWN_PALETTE_BASE) * 3
    if value >= OWN_PALETTE_BASE and k + 2 < len(own):
        return (own[k + 1], own[k + 2], own[k])       # 제 팔레트는 (파랑, 빨강, 초록)
    return shared[value]


def frames(data, w, h, own, shared):
    """한 덩이를 틀 크기로 잘라 RGBA 로 낸다."""
    out = []
    for f in range(len(data) // (w * h)):
        im = Image.new("RGBA", (w, h))
        px = []
        for v in data[f * w * h:(f + 1) * w * h]:
            c = color(v, own, shared)
            px.append((0, 0, 0, 0) if c is None else (c[0], c[1], c[2], 255))
        im.putdata(px)
        out.append(im)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="게임 폴더 (SCOMBAT.CDS 가 있는 곳)")
    ap.add_argument("--out", default=None, help="저장할 폴더 (기본 asset/scombat)")
    args = ap.parse_args()

    path = os.path.join(args.game, "SCOMBAT.CDS")
    if not os.path.exists(path):
        sys.exit("SCOMBAT.CDS 가 없다: " + path)

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = args.out or os.path.join(repo, "asset", "scombat")
    os.makedirs(out_dir, exist_ok=True)

    archive = Ls12.open(path)
    own = archive.decode(OWN_PALETTE_PART)
    shared = game_palette(repo)

    made = 0
    for part, start, w, h, name in PIECES:
        data = archive.decode(part)
        span = len(data) - start
        # 다음 조각이 있으면 거기까지만 자른다.
        for p2, s2, _, _, _ in PIECES:
            if p2 == part and s2 > start:
                span = min(span, s2 - start)
        for i, im in enumerate(frames(data[start:start + span], w, h, own, shared)):
            im.save(os.path.join(out_dir, "%s-%02d.png" % (name, i)))
            made += 1

    for k, part in enumerate(SHIP_PARTS):
        data = archive.decode(part)
        for i, im in enumerate(frames(data, SHIP_W, SHIP_H, own, shared)):
            im.save(os.path.join(out_dir, "ship%d-%02d.png" % (k, i)))
            made += 1

    with open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8") as f:
        f.write(__doc__.strip() + "\n")
    print("%d장을 %s 에 저장했다" % (made, out_dir))


if __name__ == "__main__":
    main()
