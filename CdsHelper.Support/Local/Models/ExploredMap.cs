namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 밝힌 바다 — 항해지도에 드러나는 자리다. 칸 <b>4x4</b> 를 한 점으로 묶어 비트 하나로 든다.
/// </summary>
/// <remarks>
/// 게임도 같은 모양이다(<c>0x005AA898</c>).
/// <code>
///   0x00468EA0  seen(x, y)    비트 = 표[x/8 + y*79] &amp; (1 &lt;&lt; (x &amp; 7))
///   0x00468D90  mark(칸x, 칸y) 배 둘레를 원으로 칠한다 — 반지름 = 시야 * 8 + 56 칸
///   0x00416A00  항해지도       625 x 313 점, 안 밝힌 곳은 양피지색으로 둔다
/// </code>
/// 한 줄이 <b>79바이트</b>(632비트)라 625점이 들어가고, 313줄이니 24,727바이트다.
/// 칸 2500x1250 을 넷으로 나눈 수다.
///
/// 지도 그림이 아니라 <b>밝힘</b>만 든다 — 뭍인지 바다인지는 그릴 때 WORLD.CDS 에서 본다.
/// </remarks>
public sealed class ExploredMap
{
    /// <summary>한 점이 덮는 칸 수(가로·세로 같다).</summary>
    public const int CellsPerBlock = 4;

    /// <summary>점의 가로·세로 수. 칸 2500x1250 을 넷으로 나눈 것이다.</summary>
    public const int Width = 625, Height = 313;

    /// <summary>한 줄의 바이트 수. 게임이 <c>x/8 + y*79</c> 로 짚는다.</summary>
    public const int Stride = 79;

    /// <summary>
    /// 배가 지나며 밝히는 반지름(칸). 게임은 <c>시야 * 8 + 56</c> 이고, 시야를 올리는 것은
    /// 망원경 같은 물건이다 — 우리는 아직 시야를 안 들고 있어 밑값만 쓴다.
    /// </summary>
    public const int SightCells = 56;

    private readonly byte[] _bits = new byte[Stride * Height];

    /// <summary>비트가 하나라도 서 있는지 — 한 번도 안 나갔으면 false.</summary>
    public bool Any { get; private set; }

    /// <summary>그 점이 밝혀졌는지. 밖이면 false.</summary>
    public bool Seen(int bx, int by)
    {
        if (bx < 0 || bx >= Width || by < 0 || by >= Height) return false;
        return (_bits[by * Stride + (bx >> 3)] & (1 << (bx & 7))) != 0;
    }

    /// <summary>
    /// 배가 선 칸 둘레를 밝힌다. 반지름은 <b>칸</b> 단위다.
    /// </summary>
    /// <returns>새로 밝힌 점이 하나라도 있으면 true.</returns>
    public bool Mark(int cellX, int cellY, int radiusCells = SightCells)
    {
        int r = Math.Max(1, radiusCells / CellsPerBlock);
        int cx = cellX / CellsPerBlock, cy = cellY / CellsPerBlock;
        bool fresh = false;

        for (int dy = -r; dy <= r; dy++)
        {
            int by = cy + dy;
            if (by < 0 || by >= Height) continue;
            int span = (int)Math.Sqrt((double)r * r - (double)dy * dy);
            for (int dx = -span; dx <= span; dx++)
            {
                int bx = cx + dx;
                if (bx < 0 || bx >= Width) continue;
                int at = by * Stride + (bx >> 3);
                byte bit = (byte)(1 << (bx & 7));
                if ((_bits[at] & bit) != 0) continue;
                _bits[at] |= bit;
                fresh = true;
            }
        }

        if (fresh) Any = true;
        return fresh;
    }

    /// <summary>적어 두는 꼴 — 비트를 그대로 base64 로.</summary>
    public string ToText() => Any ? Convert.ToBase64String(_bits) : "";

    /// <summary>적어 둔 것을 되읽는다. 길이가 안 맞거나 깨졌으면 빈 채로 둔다.</summary>
    public void Restore(string? text)
    {
        Array.Clear(_bits);
        Any = false;
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            var got = Convert.FromBase64String(text);
            int n = Math.Min(got.Length, _bits.Length);
            Array.Copy(got, _bits, n);
            for (int i = 0; i < n; i++)
                if (_bits[i] != 0) { Any = true; break; }
        }
        catch (FormatException)
        {
            Array.Clear(_bits);   // 깨졌으면 안 밝힌 셈 친다
        }
    }
}
