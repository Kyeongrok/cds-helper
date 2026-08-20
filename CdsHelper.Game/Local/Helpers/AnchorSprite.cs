namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 정박했을 때 배 옆에 내리는 닻 그림 한 장.
/// </summary>
/// <remarks>
/// 배·말과 달리 게임에서 뜬 것이 아니라 여기서 점을 찍어 만든다. 16x16 한 장이라
/// <c>asset</c> 에 파일로 둘 만큼도 아니고, 파일이 빠져 안 보이는 일도 없다.
/// 몸통은 양피지빛이고 둘레에 어두운 테를 두른다 — 파란 바다 위에서 형태가 뭉개지지 않게.
/// </remarks>
public static class AnchorSprite
{
    public const int Width = 16;
    public const int Size = Width * Width;

    private const uint Body = 0xFFE8DCC0;
    private const uint Edge = 0xFF2A2118;

    /// <summary>테를 두를 자리가 있도록 위아래·좌우에 빈 칸을 한 줄씩 남겨 둔다.</summary>
    private static readonly string[] Art =
    [
        "................",
        ".......##.......",
        "......#..#......",
        "......#..#......",
        ".......##.......",
        "....########....",
        ".......##.......",
        ".......##.......",
        ".......##.......",
        "..#....##....#..",
        "..#....##....#..",
        "...#...##...#...",
        "....#..##..#....",
        ".....######.....",
        "......####......",
        "................",
    ];

    private static uint[]? _pixels;

    /// <summary>16x16 BGRA 한 장. 비침은 알파 0 이다.</summary>
    public static ReadOnlySpan<uint> Pixels => _pixels ??= Build();

    private static uint[] Build()
    {
        var body = new uint[Size];
        for (int y = 0; y < Width; y++)
            for (int x = 0; x < Width; x++)
                if (Art[y][x] == '#') body[y * Width + x] = Body;

        // 몸통에 닿은 빈 점을 테로 채운다. 칠할 곳은 원본을 보고 정해야 테가 테를 부르지 않는다.
        var outlined = (uint[])body.Clone();
        for (int y = 0; y < Width; y++)
            for (int x = 0; x < Width; x++)
                if (body[y * Width + x] == 0 && TouchesBody(body, x, y))
                    outlined[y * Width + x] = Edge;
        return outlined;
    }

    private static bool TouchesBody(uint[] body, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Width) continue;
                if (body[ny * Width + nx] != 0) return true;
            }
        return false;
    }
}
