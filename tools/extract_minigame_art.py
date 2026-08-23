# -*- coding: utf-8 -*-
"""
MGGRAPH.CDS 에서 미니 게임 그림을 뽑는다.

MGGRAPH.CDS 는 LS12 아카이브고 파트가 쉰여덟이다. 큰 것 둘이 화면 배경이고,
나머지가 조각과 팔레트다.

    파트 0     48바이트   — 16색 팔레트
    파트 1    471바이트   — 157색 팔레트 (파트 14 것)
    파트 2    471바이트   — 157색 팔레트 (파트 51 것)
    파트 14  180224바이트 — 352x512, 색이 열넷뿐인 무늬
    파트 51  158976바이트 — 368x432, <b>성배 퍼즐 배경</b>
    파트 29~48  1728바이트씩 — 48x36 성배 조각 스물
    파트 3~13·17~28 3072바이트씩 — 64x48 바가지 조각들

색인은 <b>74 를 빼서</b> 제 팔레트 자리로 삼는다. 팔레트 한 색은 파일에
(파랑, 빨강, 초록) 차례로 적혀 있다 — 도시 그림·아이템 그림과 같은 규칙이다.

    색인 최소 75, 최대 229 이고 157색이 74~230 을 덮는다.
    base 를 73 으로 잡으면 마지막 자리(마젠타)가 걸려 돌 이음매가 분홍으로 튄다.
    74 라야 그 자리가 검정이 되어 이음매 선이 제대로 나온다.

쓰는 법:
    python tools/extract_minigame_art.py --game "C:\\...\\대항해시대3"
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ls12  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "asset", "minigame")

#: 그림마다 (파트, 팔레트 파트, 너비, 높이, 파일 이름).
PICTURES = [
    (51, 2, 368, 432, "grail-bg.png"),
]

#: 제 팔레트가 덮기 시작하는 색인.
BASE = 74


def decode(archive, part, pal_part, width, height):
    px = archive.decode(part)
    if len(px) != width * height:
        raise SystemExit(f"파트 {part} 크기가 {len(px)} 라 {width}x{height} 와 안 맞는다")
    own = ls12.palette(archive.decode(pal_part))
    out = []
    for v in px:
        k = v - BASE
        out.append(own[k] if 0 <= k < len(own) else (255, 0, 255))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="대항해시대3 폴더")
    args = ap.parse_args()

    path = os.path.join(args.game, "MGGRAPH.CDS")
    if not os.path.exists(path):
        raise SystemExit(f"{path} 가 없다")

    from PIL import Image

    archive = ls12.Ls12(open(path, "rb").read())
    os.makedirs(OUT_DIR, exist_ok=True)

    for part, pal_part, width, height, name in PICTURES:
        rgb = decode(archive, part, pal_part, width, height)
        image = Image.new("RGB", (width, height))
        image.putdata(rgb)
        image.save(os.path.join(OUT_DIR, name))
        print(f"{name}  {width}x{height}  (파트 {part}, 팔레트 {pal_part})")


if __name__ == "__main__":
    main()
