# -*- coding: utf-8 -*-
"""
FIGHTER.CDS 에서 일기토 그림을 뽑아 asset/duel 에 둔다.

게임의 일기토 화면(0x004A7050)은 위에 마당 384x136 을, 아래에 눈금판 384x112 를
깐다. FIGHTER.CDS 는 LS12 이고 파트가 33 이다.

    0~17    646272 + 팔레트 768   아홉 벌   ; 사람 몸짓 33장씩 (144x136)
    18~31    52224 + 팔레트 768   일곱 벌   ; 마당 384x136
    32       43024                          ; 눈금판 — 앞 16바이트를 건너뛰고 384x112

<b>팔레트가 8비트다.</b> 다른 미니 게임 그림들은 6비트(0~63)라 4를 곱해야 했는데
이 파일은 그대로 쓴다. 칸 차례는 여느 것과 같이 (파랑, 빨강, 초록)이다.

<b>바탕 값이 파트마다 다르다.</b> 마당은 74, 눈금판은 11 이다.
눈금판은 제 팔레트가 없어 마당 것을 같이 쓴다 — 게임도 그렇게 해서 눈금판이
마당 빛깔로 물든다.
"""
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ls12                                                    # noqa: E402

GAME = r"C:\Users\ocean\Desktop\대항해시대3"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "asset", "duel")

ARENA_W, ARENA_H = 384, 136
PANEL_W, PANEL_H = 384, 112
PANEL_SKIP = 16                    # 눈금판 앞머리 16바이트

ARENAS = [
    (18, "deck", "배 갑판 — 해전 일기토가 쓰는 마당"),
    (20, "field", "초원"),
    (22, "wood", "숲"),
    (24, "sand", "사막"),
    (26, "tavern", "술집"),
    (28, "mosque", "이슬람 광장"),
    (30, "temple", "일본 절"),
]


def png(path, width, height, rows):
    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    raw = b"".join(b"\x00" + row for row in rows)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n"
                + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw))
                + chunk(b"IEND", b""))


def paint(pixels, palette, width, height, base, skip=0):
    rows = []
    for y in range(height):
        row = bytearray()
        for x in range(width):
            slot = (pixels[skip + y * width + x] - base) & 0xFF
            blue, red, green = palette[slot * 3:slot * 3 + 3]
            row += bytes((red, green, blue))
        rows.append(bytes(row))
    return rows



#: 싸움꾼 몸짓 — 파트 0·2·4…16 이 벌 아홉이고 팔레트가 그 다음 홀수 파트다.
#: 한 벌이 144x136 짜리 33장(646272 = 144*136*33)이고, 색인은 <b>160</b> 을 뺀다.
#: 서른세 장을 다 뽑는다 — 치고 막고 지고 이기는 몸짓이 골고루 흩어져 있다.
FIGHTER_W, FIGHTER_H, FIGHTER_FRAMES = 144, 136, 33
FIGHTER_BASE = 160
FIGHTER_KEEP = range(FIGHTER_FRAMES)


def clear(path, key=(255, 0, 255)):
    """마젠타 바탕을 비운다 — 싸움꾼은 마당 위에 얹히므로 테가 있으면 안 된다."""
    from PIL import Image
    art = Image.open(path).convert("RGBA")
    art.putdata([(0, 0, 0, 0) if p[:3] == key else p for p in art.getdata()])
    art.save(path)


def fighters(cds):
    for kit in range(9):
        art = cds.decode(kit * 2)
        palette = cds.decode(kit * 2 + 1)
        for frame in FIGHTER_KEEP:
            skip = frame * FIGHTER_W * FIGHTER_H
            rows = paint(art, palette, FIGHTER_W, FIGHTER_H, FIGHTER_BASE, skip)
            where = os.path.join(OUT, "duel-fighter-%d-%02d.png" % (kit, frame))
            png(where, FIGHTER_W, FIGHTER_H, rows)
            clear(where)
        print("duel-fighter-%d-*.png  %dx%d x%d"
              % (kit, FIGHTER_W, FIGHTER_H, len(FIGHTER_KEEP)))


def main():
    os.makedirs(OUT, exist_ok=True)
    cds = ls12.Ls12.open(os.path.join(GAME, "FIGHTER.CDS"))
    panel = cds.decode(32)

    fighters(cds)

    for part, name, what in ARENAS:
        art = cds.decode(part)
        palette = cds.decode(part + 1)

        png(os.path.join(OUT, "duel-%s.png" % name),
            ARENA_W, ARENA_H, paint(art, palette, ARENA_W, ARENA_H, 74))
        print("duel-%s.png  %s" % (name, what))

        # <b>눈금판은 제 팔레트가 없어 마당 것을 같이 쓴다.</b> 그래서 마당마다 빛깔이
        # 다르다 — 갑판 것 하나만 뽑아 두면 초원 판에 갑판 빛깔이 얹혀 보랏빛이 된다.
        # 마당마다 한 장씩 뽑는다.
        png(os.path.join(OUT, "duel-panel-%s.png" % name),
            PANEL_W, PANEL_H,
            paint(panel, palette, PANEL_W, PANEL_H, 11, PANEL_SKIP))
        print("duel-panel-%s.png  눈금판(%s 빛깔)" % (name, what))


if __name__ == "__main__":
    main()
