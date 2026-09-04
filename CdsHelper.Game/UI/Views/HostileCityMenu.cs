using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 적대 도시 앞에 서면 뜨는 차림표 — 공격한다 · 잠입한다 · 교섭한다 · 떠난다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004A56F0</c> 이다. 규칙은 <see cref="Standoff"/> 에 모아 두었고 여기서는
/// <b>차례</b>만 맡는다 — 게임처럼 <b>고를 때마다 다시 뜬다</b>. 문이 열리거나 물러설
/// 때까지 돌고, 꺼진 칸도 자리를 안 비운다(<c>0x004A5726</c> 이 넉 줄을 먼저 깔고
/// 그 뒤에 켜고 끈다).
///
/// 앞머리(<c>0x004A5210</c>)가 먼저다 — <b>도시 그림을 펴고</b> 문지기가
/// "외국인은 들어올 수 없다" 고 말한 뒤에야 차림표가 뜬다. 말을 못 알아들으면 글자가
/// ×로 뭉개지고, 대원 중에도 아는 이가 없으면 한 마디 덧붙는다.
///
/// <b>공격은 아직 못 옮겼다.</b> 육상전(<c>0x0044A870</c>, 볼트 <c>65.분석-육상전</c>)이
/// 통째로 남은 숙제라, 물음까지는 게임 그대로 묻고 나서 아직이라고 이른다.
/// </remarks>
internal static class HostileCityMenu
{
    /// <summary>한 판의 끝.</summary>
    /// <param name="Entered">문이 열렸는지 — 들어가도 되면 참.</param>
    /// <param name="GameOver">잡혀 죽었는지(<c>0x004A559F</c>).</param>
    public readonly record struct Outcome(bool Entered, bool GameOver);

    /// <summary>
    /// 적대 도시 앞에 선다.
    /// </summary>
    /// <param name="byLand">말로 왔는지 — 마을 쪽이면 참, 배로 온 항구 쪽이면 거짓.</param>
    /// <param name="mapArea">도시 그림을 펼 자리. 비어 있으면 임자 창 한가운데다.</param>
    public static Outcome Run(Window owner, Engine.Game game, int city, string cityName,
                              bool byLand, Rect mapArea = default)
    {
        // 게임도 그림부터 편다 — 도시는 그려지고 성문에서 막히는 것이다.
        var scene = GateScene.Open(owner, game, city, mapArea);

        // 그림이 펴졌으면 이미 도시에 닿은 것이라 곡도 그 도시 것으로 바뀐다.
        // 못 들어가고 물러서면 부르는 쪽이 뭍·바다 곡으로 되돌린다(ShipMapWindow.PassGate).
        if (scene != null)
            game.Bgm.Play(BgmPlayer.CityTrackForCulture(game.CityRows?.CultureOf(city) ?? 0));
        try
        {
            return AtTheGate(scene as Window ?? owner, game, city, cityName, byLand);
        }
        finally
        {
            scene?.Close();
        }
    }

    /// <summary>성문 앞에서 문지기를 만나고 차림표를 돌린다.</summary>
    private static Outcome AtTheGate(Window owner, Engine.Game game, int city, string cityName,
                                     bool byLand)
    {
        var player = game.Player;
        var dice = new GameRandom(Environment.TickCount);
        int nation = game.CityRows?.NationOf(city) ?? -1;
        int sect = nation >= 0 ? game.Nations?.Find(nation)?.Sect ?? 0 : 0;
        string where = Standoff.Where(byLand);
        bool canTalk = true;

        // 문지기가 먼저 말한다(0x004A521A). 아는 말이 아니면 ×로 뭉개져 나오고,
        // 그때는 대원이 한 마디 덧붙인다 — 마을과 항구의 문구가 다르다(0x004A526E).
        bool heard = TongueAt(game, city) > 0;
        int culture = game.CityRows?.CultureOf(city) ?? 0;
        var gate = game.SpeakerFace(Standoff.GateSpeaker(byLand), culture);
        TalkDialog.Say(owner, gate, cityName,
                       heard ? Standoff.GateWord
                             : Standoff.Garble(Standoff.GateWord));
        // 덧붙이는 것은 대원이다 — 문지기 얼굴을 그대로 두면 그가 말한 꼴이 된다.
        if (!heard)
            TalkDialog.Say(owner, null, cityName,
                           byLand ? Standoff.GateLostVillage
                                  : Standoff.GateLostPort);

        while (true)
        {
            // 넉 줄을 먼저 깔고 켜고 끈다 — 꺼진 칸도 자리를 지킨다.
            var rows = new (string, bool)[]
            {
                (Standoff.Choices[Standoff.Attack], true),
                (Standoff.Choices[Standoff.Sneak], Standoff.CanSneak(sect)),
                (Standoff.Choices[Standoff.Talk], canTalk),
                (Standoff.Choices[Standoff.Leave], !player.TalkLostAt(city, byLand)),
            };

            int pick = ChoiceDialog.Pick(owner, cityName, rows);
            switch (pick)
            {
                case Standoff.Attack:
                    // 물음이 둘이다(0x00551BF0 → 0x00551C00). 어느 하나라도 무르면 돌아간다.
                    if (!ConfirmDialog.Ask(owner, Standoff.SureWord, cityName)) break;
                    if (!ConfirmDialog.Ask(owner, Standoff.AttackWord, cityName)) break;

                    NoticeDialog.Show(owner,
                        "…(육상전은 아직 옮기지 못했다. 이번에는 물러선다.)", cityName);
                    break;

                case Standoff.Sneak:
                    if (Sneak(owner, game, dice, city, cityName) is { } sneaked) return sneaked;
                    break;

                case Standoff.Talk:
                    // 게임도 돈부터 본다(0x00468BF0 → 「소지금이 모자랍니다!」). 한 번
                    // 걸리면 그 자리에서는 교섭 칸이 죽는다(0x004A5800).
                    if (player.Gold <= 0)
                    {
                        NoticeDialog.Show(owner, Standoff.TooPoorWord, cityName);
                        canTalk = false;
                        break;
                    }
                    if (Talk(owner, game, dice, city, cityName, byLand, where))
                        return new Outcome(Entered: true, GameOver: false);
                    break;

                default:
                    // 떠난다, 또는 창을 닫았다.
                    NoticeDialog.Show(owner, Standoff.GiveUpWord, cityName);
                    return new Outcome(false, false);
            }
        }
    }

    /// <summary>
    /// 교섭한다 — 되면 돈을 건네고 문이 열린다(<c>0x004A55C0</c>).
    /// </summary>
    /// <remarks>
    /// 어그러지면 그 자리(마을 쪽·항구 쪽)에 <b>실패 표시</b>가 서서 「떠난다」가 죽는다 —
    /// 게임도 도시 레코드 <c>+0xB0</c>/<c>+0xB4</c> 에 1 을 적는다.
    /// </remarks>
    private static bool Talk(Window owner, Engine.Game game, GameRandom dice, int city,
                             string cityName, bool byLand, string where)
    {
        var player = game.Player;

        if (!Standoff.Talks(player, dice))
        {
            NoticeDialog.Show(owner, Standoff.TalkLostWord, cityName);
            NoticeDialog.Show(owner, string.Format(Standoff.TalkLostNews, where), cityName);
            player.MarkTalkLost(city, byLand);
            return false;
        }

        int paid = player.Spend(Standoff.Price(player, dice));
        NoticeDialog.Show(owner, string.Format(Standoff.PaidWord, paid), cityName);
        NoticeDialog.Show(owner, string.Format(Standoff.TalkWonWord, where), cityName);
        NoticeDialog.Show(owner, string.Format(Standoff.TalkWonNews, where), cityName);
        player.OpenGate(city);
        return true;
    }

    /// <summary>
    /// 잠입한다 — 되면 그대로 들어가고, 들키면 달아나거나 재판이다(<c>0x004A52F0</c>).
    /// </summary>
    /// <remarks>달아났으면 null 을 내어 차림표로 돌아간다.</remarks>
    private static Outcome? Sneak(Window owner, Engine.Game game, GameRandom dice, int city,
                                  string cityName)
    {
        var player = game.Player;

        // 그 도시가 쓰는 말을 얼마나 아는지. 셋에 못 미치면 대원이 말린다.
        int tongue = TongueAt(game, city);
        NoticeDialog.Show(owner,
            tongue >= Standoff.SafeTongue ? Standoff.TakeCare : Standoff.TongueTooThin,
            cityName);

        bool turban = HasTurban(game, player);
        if (turban)
            NoticeDialog.Show(owner, $"{Standoff.TurbanName}을 사용했다", cityName);

        if (Standoff.Sneaks(player, tongue, turban, dice))
        {
            NoticeDialog.Show(owner, Standoff.SneakedIn, cityName);
            player.OpenGate(city);
            return new Outcome(Entered: true, GameOver: false);
        }

        NoticeDialog.Show(owner, Standoff.Spotted, cityName);

        if (Standoff.Escapes(player, dice))
        {
            NoticeDialog.Show(owner, Standoff.GotAway, cityName);
            return null;                       // 차림표로 돌아간다
        }

        return Trial(owner, game, dice, cityName);
    }

    /// <summary>
    /// 잡힌 뒤의 재판(<c>0x004A5439</c> 부터).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   4a546e  가벼움 = rand(2000) + 1000 &gt; max(0, 악명 - 운 - 1)
    ///   4a54af  가벼우면 rand(100) &lt; 운 + 1 이라야 추방만, 아니면 벌금 + 소지금 몰수
    ///   4a5561  무거우면 "죽음으로서 속죄하라!" — 그대로 놀이가 끝난다(0x0044AF70)
    /// </code>
    /// </remarks>
    private static Outcome Trial(Window owner, Engine.Game game, GameRandom dice, string cityName)
    {
        var player = game.Player;
        NoticeDialog.Show(owner, Standoff.Caught, cityName);

        int weight = Math.Max(0, player.Infamy - player.AbilityOf(Ability.Luck) - 1);
        if (dice.Next(2000) + 1000 <= weight)
        {
            NoticeDialog.Show(owner,
                $"거기는 악명 높은 {player.Name}군. 죽음으로서 속죄하라!", cityName);
            return new Outcome(Entered: false, GameOver: true);
        }

        if (dice.Next(100) < player.AbilityOf(Ability.Luck) + 1)
        {
            NoticeDialog.Show(owner, Standoff.Banished, cityName);
        }
        else
        {
            NoticeDialog.Show(owner, Standoff.Fined, cityName);
            player.Spend(player.Gold);
            NoticeDialog.Show(owner, Standoff.Robbed, cityName);
        }

        NoticeDialog.Show(owner, Standoff.GotAway, cityName);
        return new Outcome(false, false);
    }

    /// <summary>그 도시가 쓰는 말을 얼마나 아는지. 표를 못 읽으면 0.</summary>
    private static int TongueAt(Engine.Game game, int city)
    {
        int nation = game.CityRows?.NationOf(city) ?? -1;
        if (nation < 0 || game.Nations?.Find(nation) is not { } row) return 0;
        if (row.Language < 0 || row.Language >= Skill.Languages.Length) return 0;
        return game.Player.TongueOf(Skill.Languages[row.Language]);
    }

    /// <summary>터번을 들었는지. 아이템 표를 못 읽으면 안 든 것으로 본다.</summary>
    private static bool HasTurban(Engine.Game game, Player player)
    {
        if (game.Items?.Find(Standoff.TurbanName) is not { } turban) return false;
        return player.Items.Contains(turban.Id);
    }
}
