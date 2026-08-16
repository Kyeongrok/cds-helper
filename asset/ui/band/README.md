# 메뉴 띠 조각

`MISC.CDS` 파트 4 에서 뽑은 것이다. `tools/extract_menu_band.py` 가 만든다.

띠는 **왼끝 · 가운데(되풀이) · 오른끝** 셋을 이어 붙여 짓는다.
한 장을 늘리는 것이 아니다. 높이는 늘 24픽셀, 폭은 `16 + 8*n + 16`.

| 파일 | 크기 | 쓰임 |
|---|---|---|
| `title-left.png` | 16x24 | 진홍 장식 왼끝 |
| `title-mid.png` | 8x24 | 진홍 장식 가운데(되풀이) |
| `title-right.png` | 16x24 | 진홍 장식 오른끝 |
| `button-left.png` | 16x24 | 베이지 왼끝 |
| `button-mid.png` | 8x24 | 베이지 가운데(되풀이) |
| `button-right.png` | 16x24 | 베이지 오른끝 |
| `alt-left.png` | 16x24 | 회녹색 왼끝 |
| `alt-mid.png` | 8x24 | 회녹색 가운데(되풀이) |
| `alt-right.png` | 16x24 | 회녹색 오른끝 |

`*-band.png` 는 가운데를 6번 되풀이해 이어 붙인 맛보기다.

앱은 이 PNG 를 쓰지 않고 `UiSprites` 가 게임 폴더의 MISC.CDS 에서 바로 읽는다 —
여기 있는 것은 눈으로 보고 손보기 위한 것이다.
