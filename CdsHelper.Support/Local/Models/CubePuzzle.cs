namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「화살표 입방체 퍼즐」 — 화살표가 그려진 입방체를 굴려 좌대를 옮긴다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0049B3C0</c> 이 껍데기, <c>0x0049B310</c> 이 한 판, <c>0x0049BE10</c> 이
/// 본체다. 설명 글은 <c>0x0056DB38</c> 벌에 그대로 있다.
/// <code>
///   성공조건 [자기가 타고 있는 좌대를 움직여서 출구로 이동한다]
///
///   입방체를 지면에 수직으로 90도 회전시키면 그 때 위의 면에 나온 화살표가
///   가리키는 방향으로 좌대가 하나만 움직인다.
///   지면에 수평방향으로 회전시켜도 대좌는 움직이지 않는다. 이 수평 회전은
///   한번에 90도이지만, 연달아 돌릴 수는 없다.
///   입방체가 서로 면하는 면은 대칭이 되어 있으며 반대편의 면이 그대로 비치는
///   것 처럼 되어 있다.
/// </code>
/// 지면 밑으로 떨어지면 진다. 그런데 <b>한 번은 봐 준다</b> —
/// <c>0x0049B3C0</c> 이 진 판을 받으면 "아니! 밑바닥으로 떨어진 줄 알았는데 실은 그
/// 아래층이 존재했다! 자, 모험자여! 이것이 마지막 찬스다!"(<c>0x0056DD58</c>) 하고
/// 한 판을 더 준다.
///
/// 이기면 <c>0x0049B366</c> 이 <c>0x0047CBC0(0x3E8)</c> 로 <b>금화 1000닢</b>을 준다 —
/// 글은 "금화로 따지면 %ld 닢에 상당되는 금괴를 손에 넣었다!"(<c>0x0056DDF8</c>)다.
/// </remarks>
public sealed class CubePuzzle
{
    /// <summary>판 한 변. <b>게임 것을 못 짚어 이만큼으로 잡았다.</b></summary>
    public const int Side = 5;

    /// <summary>이기면 받는 금화(<c>0x0049B366</c> 의 <c>0x3E8</c>).</summary>
    public const int Prize = 1000;

    /// <summary>네 방향 — 북·동·남·서. 화살표 번호가 이 차례다.</summary>
    public static readonly (int Dx, int Dy, string Name)[] Ways =
        [(0, -1, "북"), (1, 0, "동"), (0, 1, "남"), (-1, 0, "서")];

    /// <summary>
    /// 입방체의 여섯 면에 그려진 화살표. 값은 <see cref="Ways"/> 의 번호다.
    /// </summary>
    /// <remarks>
    /// <b>마주 보는 면은 대칭</b>이라 했으니, 위·아래가 같은 쪽을 가리키게 짝을 맞춘다.
    /// 곧 굴려서 어느 면이 올라오든 «보이는 화살표» 가 곧 갈 쪽이다.
    /// </remarks>
    private readonly int[] _face = new int[6];

    /// <summary>지금 위에 온 면과 그 밑에 온 면.</summary>
    private int _up = 0, _north = 1, _east = 2;

    /// <summary>수평으로 몇 번 돌렸는지. 위 면의 화살표가 그만큼 돌아간다.</summary>
    private int _turn;

    /// <summary>좌대가 선 자리.</summary>
    public int X { get; private set; }

    public int Y { get; private set; }

    /// <summary>출구 자리.</summary>
    public int ExitX { get; }

    public int ExitY { get; }

    /// <summary>몇 번 움직였는지.</summary>
    public int Moves { get; private set; }

    /// <summary>바로 앞에 수평으로 돌렸나. 그러면 또 못 돌린다.</summary>
    public bool JustSpun { get; private set; }

    /// <summary>끝났으면 이겼는지. 아직이면 null.</summary>
    public bool? Over { get; private set; }

    /// <summary>
    /// 지금 위에 온 면의 화살표가 가리키는 쪽. 수평으로 돌린 만큼 함께 돌아간다.
    /// </summary>
    public int Arrow => (_face[_up] + _turn) & 3;

    public CubePuzzle(Random rng)
    {
        // <b>마주 보는 면은 대칭</b>이라 했으니 짝끼리 같은 쪽을 가리키게 둔다.
        _face[0] = _face[5] = rng.Next(4);
        _face[1] = _face[3] = rng.Next(4);
        _face[2] = _face[4] = rng.Next(4);

        do
        {
            X = rng.Next(Side);
            Y = rng.Next(Side);
            ExitX = rng.Next(Side);
            ExitY = rng.Next(Side);
        }
        while (X == ExitX && Y == ExitY);
    }

    /// <summary>
    /// 수직으로 90도 굴린다 — 그 쪽으로 넘어뜨린다.
    /// </summary>
    /// <remarks>
    /// 넘어뜨리면 그 쪽 옆면이 위로 온다. 그러고 나서 <b>새로 위에 온 면의
    /// 화살표</b>가 가리키는 쪽으로 좌대가 한 칸 움직인다 — 넘어뜨린 쪽이 아니다.
    /// </remarks>
    /// <param name="way">넘어뜨릴 쪽(<see cref="Ways"/> 번호).</param>
    public void Roll(int way)
    {
        if (Over != null) return;

        int up = _up, north = _north, east = _east;
        (_up, _north, _east) = way switch
        {
            0 => (north, Flip(up), east),          // 북으로 넘어뜨린다
            1 => (east, north, Flip(up)),          // 동
            2 => (Flip(north), up, east),          // 남
            _ => (Flip(east), north, up),          // 서
        };

        JustSpun = false;
        Moves++;

        var (dx, dy, _) = Ways[Arrow];
        X += dx;
        Y += dy;

        if (X < 0 || X >= Side || Y < 0 || Y >= Side) { Over = false; return; }   // 떨어졌다
        if (X == ExitX && Y == ExitY) Over = true;
    }

    /// <summary>마주 보는 면.</summary>
    private static int Flip(int face) => 5 - face;

    /// <summary>수평으로 90도 돌린다. 좌대는 안 움직이고, 연달아는 못 한다.</summary>
    public bool Spin()
    {
        if (Over != null || JustSpun) return false;

        _turn = (_turn + 1) & 3;
        (_north, _east) = (_east, Flip(_north));
        JustSpun = true;
        Moves++;
        return true;
    }
}
