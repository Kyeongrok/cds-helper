namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「낚시 게임」 — 바늘을 떨어뜨려 바닥의 대어를 낚는 사다리 타기.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0047BDD0</c> 이고 판을 까는 곳은 <c>0x0047B7A5</c> 다.
/// <code>
///   0x0056EDB0  바다에서 바늘을 떨어뜨려서 바닥에 있는 대어를 낚는 게임입니다.
///               낚시바늘은 줄을 따라 내려갑니다.
///               내려가는 도중에 화살표를 클릭하든지 ←→버튼을 누르면 교차하는
///               데에서 낚시바늘을 옆으로 이동할 수 있습니다만, 다음에 교차하는
///               데에서는 반드시 밑으로 내려갑니다.
/// </code>
/// 판은 <b>일곱 칸 x 여섯 줄</b>이다 — <c>0x0047B7C3</c> 이 <c>[0x118]</c> 부터
/// 마흔두 칸을 0 으로 지우고, 내려가는 곳(<c>0x0047AB41</c> 벌)이 자리를 <b>+7</b>(곧장
/// 아래) · <b>+8</b>(오른쪽 아래) · <b>+6</b>(왼쪽 아래) 로 옮긴다.
/// </remarks>
public sealed class FishingGame
{
    /// <summary>칸과 줄(<c>0x0047B7D2</c> 의 <c>0x2A</c> = 7 x 6).</summary>
    public const int Columns = 7, Rows = 6, Cells = Columns * Rows;

    /// <summary>길을 되짚는 걸음 수(<c>0x0047B85F</c> 의 <c>cmp $7</c>).</summary>
    private const int WalkSteps = 7;

    /// <summary>뿌리는 것 수(<c>0x0047B8D5</c> 의 <c>cmp $0xF</c>).</summary>
    private const int Scattered = 15;

    /// <summary>칸에 든 것.</summary>
    public const int Empty = 0, Path = 1, Squid = 2, Octopus = 4;

    /// <summary>
    /// 결과 — <c>[0x1F4]</c> 그 자체다. <c>0x0047AD41</c> 의 뜀표가 이 번호로 갈린다.
    /// </summary>
    public enum Catch
    {
        /// <summary>아직 내려가는 중.</summary>
        None = 0,

        /// <summary>오징어를 낚았다 — "왓! 오징어가 얼굴에 먹물을 토했다!"</summary>
        SquidCaught = 1,

        /// <summary>낙지를 낚았다 — "너무 징그러워서 갑판에 내동댕이쳤다."</summary>
        OctopusCaught = 2,

        /// <summary>잡어를 낚았다 — 두 갈래가 다 같은 글이다.</summary>
        SmallFry = 3,

        /// <summary>잡어를 낚았다(다른 갈래).</summary>
        SmallFryToo = 4,

        /// <summary><b>대어을 낚았다</b> — 이때만 이긴 것으로 친다.</summary>
        BigOne = 5,

        /// <summary>바닥에 걸렸다 — "[지구를 낚았다]고 해야하나."</summary>
        Seabed = 6,
    }

    private readonly int[] _cell = new int[Cells];

    /// <summary>대어가 있는 칸(<c>[0x1DC]</c>).</summary>
    public int BigOneColumn { get; }

    /// <summary>바늘을 떨어뜨리는 칸(<c>[0x1D8]</c>). 고르는 것이 아니라 굴린다.</summary>
    public int DropColumn { get; }

    /// <summary>
    /// 바늘이 지금 있는 자리(<c>[0x1E0]</c>). 처음엔 판 위(음수)고, 바닥에 닿으면
    /// <see cref="Cells"/> 위다.
    /// </summary>
    public int At { get; private set; }

    /// <summary>다음 교차점에서 옆으로 갈지 — <c>1</c> 오른쪽, <c>-1</c> 왼쪽, <c>0</c> 곧장.</summary>
    public int Lean { get; private set; }

    /// <summary>낚은 것. <see cref="Catch.None"/> 이면 아직이다.</summary>
    public Catch Got { get; private set; }

    /// <summary>대어를 낚았나(<c>0x0047AD6C</c> 의 <c>[0x9C] = 1</c>).</summary>
    public bool Won => Got == Catch.BigOne;

    /// <summary>그 칸에 든 것.</summary>
    public int CellAt(int cell) => _cell[cell];

    /// <summary>지금 칸 번호. 판 위에 있을 때도 칸은 있다.</summary>
    public int Column => ((At % Columns) + Columns) % Columns;

    /// <summary>지금 줄 번호. 아직 안 내려왔으면 -1 이다.</summary>
    public int Row => At < 0 ? -1 : At / Columns;

    /// <summary>
    /// 판을 깐다(<c>0x0047B7A5</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    /// 47b7b3  칸 = rand(7)
    /// 47b7ff  일곱 걸음을 되짚는다 — 왼쪽 끝이면 rand(2), 오른쪽 끝이면 rand(2)*2,
    ///         아니면 rand(3). 2 는 왼쪽(-1)으로 바꾼다
    /// 47b882  걸어 온 끝 칸이 <b>대어 칸</b>이 된다
    /// 47b88e  그 길 여섯 칸을 1 로 막는다 — 여기엔 아무것도 안 뿌린다
    /// 47b89f  빈 칸에 열다섯을 뿌린다
    /// 47b8b8  rand(11) 이 7 밑이면 <b>오징어(2)</b>, 아니면 <b>낙지(4)</b>
    /// </code>
    /// 길을 먼저 막고 뿌리므로 <b>대어까지 가는 깨끗한 길이 늘 하나 있다</b>.
    /// </remarks>
    public FishingGame(Random rng)
    {
        int column = rng.Next(Columns);
        DropColumn = column;
        var path = new int[WalkSteps];

        for (int i = 0; i < WalkSteps; i++)
        {
            int step = column == 0 ? rng.Next(2)
                     : column == Columns - 1 ? rng.Next(2) * 2
                     : rng.Next(3);
            if (step == 2) step = -1;

            int at = i == 0 ? column : path[i - 1] + Columns;
            path[i] = at + step;
            column += step;
        }

        BigOneColumn = column;

        // 길 여섯 칸을 막는다. 일곱째는 판 밖이라 안 쓴다.
        for (int i = 0; i < Rows; i++)
            if (path[i] >= 0 && path[i] < Cells) _cell[path[i]] = Path;

        for (int n = 0; n < Scattered; n++)
        {
            int at = rng.Next(Cells);
            if (_cell[at] > Empty) { n--; continue; }
            _cell[at] = rng.Next(11) < 7 ? Squid : Octopus;
        }

        // 바늘은 떨어뜨리는 칸의 <b>맨 윗줄 위</b>에서 시작한다(0x0047BA33 의 -7).
        At = DropColumn - Columns;
    }

    /// <summary>「←→」 — 다음 교차점에서 옆으로 갈지 말지 잡는다.</summary>
    /// <remarks>
    /// 게임은 <c>[0x1EC]</c> 에 적어 두었다가 <c>[0x1F0]</c> 과 같아질 때 옮긴다
    /// (<c>0x0047AB2C</c>). 곧 <b>한 교차점에 한 번</b>이고, 옮기고 나면 지워져
    /// 다음 교차점에서는 반드시 밑으로 내려간다.
    /// </remarks>
    public void Steer(int way)
    {
        if (Got != Catch.None) return;
        if (way > 0 && Column < Columns - 1) Lean = 1;
        else if (way < 0 && Column > 0) Lean = -1;
        else Lean = 0;
    }

    /// <summary>
    /// 한 줄 내려간다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 47ab41  옆으로 갈 참이면 +8(오른쪽) 또는 +6(왼쪽), 아니면 +7
    /// 47ab8e  내려선 칸이 2 면 오징어, 4 면 낙지 — 그 자리에서 끝난다
    /// 47ac6c  바닥이면 자리 % 7 을 대어 칸과 견준다
    /// </code>
    /// </remarks>
    /// <returns>아직 내려갈 데가 있으면 true.</returns>
    public bool Fall()
    {
        if (Got != Catch.None) return false;

        At += Lean > 0 ? Columns + 1 : Lean < 0 ? Columns - 1 : Columns;
        Lean = 0;

        if (At >= Cells)
        {
            // 바닥. 칸이 맞으면 대어, 아니면 그냥 바닥에 걸린다.
            Got = At % Columns == BigOneColumn ? Catch.BigOne : Catch.Seabed;
            return false;
        }

        Got = _cell[At] switch
        {
            Squid => Catch.SquidCaught,
            Octopus => Catch.OctopusCaught,
            _ => Catch.None,
        };
        return Got == Catch.None;
    }
}
