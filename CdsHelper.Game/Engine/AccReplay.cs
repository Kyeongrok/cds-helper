using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 누적 캐릭터의 <b>행적을 되돌려 튼다</b>(<c>0x00432740</c> → <c>0x0040D1D0</c>).
/// </summary>
/// <remarks>
/// 은퇴한 제독은 다음 판에서 인물 276~280 에 앉고(<see cref="AccData.Place"/>), 날마다 제
/// 행적 대본을 한 줄씩 읽어 그때 그 자리로 옮겨 다닌다. 게임은 인물 레코드에 세 칸을 둔다.
/// <code>
///   +0x110  지난 날수      +0x114  대본 파일 위치      +0x118  달 오프셋(늦어짐)
/// </code>
/// 우리는 대본 대신 <see cref="Player.Trace"/> 목록을 그대로 걸어 두고, <b>판이 열린 날부터
/// 흐른 날수</b>에 늦어짐을 뺀 값으로 줄을 짚는다. 줄이 다하면 세상에서 사라진다
/// (원본도 <c>0x0040D1D0</c> 이 −1 을 주면 그렇게 한다).
///
/// 늦어짐은 <c>0x004A4A3D</c> 다 — 해적질을 당하면 <c>(rand(3) x 3 + 3) x 4</c> 날만큼
/// 밀리고 「%s의 행동이 늦어졌습니다」가 뜬다(그 자리는 <see cref="Delay"/>).
/// </remarks>
public sealed class AccReplay
{
    /// <summary>누적 캐릭터 하나가 어디까지 왔는지.</summary>
    private sealed class Runner
    {
        public required int Person { get; init; }
        public required Player.Trace[] Track { get; init; }

        /// <summary>대본이 시작하는 날 — 그 사람의 첫 행적 날짜다.</summary>
        public required DateTime From { get; init; }

        /// <summary>늦어진 날수(<c>인물 +0x118</c>).</summary>
        public int Late { get; set; }

        /// <summary>다 틀었는지(<c>0x0040D1D0</c> 이 −1 을 준 뒤).</summary>
        public bool Done { get; set; }
    }

    private readonly List<Runner> _runners = [];
    private readonly DateTime _opened;

    /// <summary>판이 열린 날부터 센다.</summary>
    public AccReplay(DateTime opened) => _opened = opened;

    /// <summary>
    /// 올라 있는 사람들의 대본을 건다 — <see cref="AccData.Place"/> 바로 뒤에 부른다.
    /// </summary>
    public void Load()
    {
        _runners.Clear();
        var all = AccData.Load();
        for (int i = 0; i < all.Count && i < AccData.Slots; i++)
        {
            var track = all[i].Track;
            if (track is not { Length: > 0 }) continue;
            _runners.Add(new Runner
            {
                Person = AccData.FirstPerson + i,
                Track = track,
                From = track[0].On,
            });
        }
    }

    /// <summary>걸린 대본이 하나라도 있는지.</summary>
    public bool Any => _runners.Count > 0;

    /// <summary>
    /// 하루를 넘긴다 — 사람마다 그날 있어야 할 자리로 옮긴다.
    /// </summary>
    /// <param name="today">놀이 날짜.</param>
    /// <param name="people">인물 표.</param>
    public void PassDay(DateTime today, IReadOnlyList<PersonTable.Row> people)
    {
        foreach (var run in _runners)
        {
            if (run.Done || run.Person >= people.Count) continue;

            // 판이 열린 날부터 흐른 날수에서 늦어진 만큼을 뺀다.
            int gone = (int)(today - _opened).TotalDays - run.Late;
            if (gone < 0) continue;

            var at = run.From.AddDays(gone);
            int step = Array.FindLastIndex(run.Track, t => t.On <= at);
            if (step < 0) continue;

            // 마지막 줄까지 갔고 그날도 지났으면 세상에서 사라진다.
            if (step == run.Track.Length - 1 && at > run.Track[^1].On)
            {
                people[run.Person].Appear = 0;
                run.Done = true;
                continue;
            }

            var row = people[run.Person];
            if (run.Track[step].Kind != Player.TraceArrival) continue;
            row.City = run.Track[step].A;
            row.Building = PersonTable.Tavern;
            row.Appear = 1;
        }
    }

    /// <summary>
    /// 그 사람의 행적을 늦춘다(<c>0x004A4A3D</c>) — <c>(rand(3) x 3 + 3) x 4</c> 날이다.
    /// </summary>
    /// <returns>늦춘 날수. 그 사람이 대본을 안 들고 있으면 0.</returns>
    public int Delay(int person, Random dice)
    {
        var run = _runners.Find(r => r.Person == person && !r.Done);
        if (run == null) return 0;

        int days = (dice.Next(3) * 3 + 3) * 4;
        run.Late += days;
        return days;
    }

    /// <summary>「%s의 행동이 늦어졌습니다」(<c>0x005516B8</c>).</summary>
    public static string Delayed(string name) => $"{name}의 행동이 늦어졌습니다";

    /// <summary>늦추고 나서 붙는 악명(<c>0x004A4A66</c> 의 <c>0x4697C0(1, 100)</c>).</summary>
    public const int DelayInfamy = 100;
}
