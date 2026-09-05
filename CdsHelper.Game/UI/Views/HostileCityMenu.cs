using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Land;
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
    /// <summary>
    /// 마을을 칠 때 펴는 싸움터 — LANDDATA 파트 1(<b>도시</b>)이다.
    /// </summary>
    /// <remarks>
    /// 싸움터 넷 가운데 어느 것을 펼지는 판을 세우는 자리(<c>0x0044A646</c> 어름)가
    /// 전투 갈래로 가르는데, 마을 공략은 도시 그림이다.
    /// </remarks>
    private const int CityField = 0;

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
            return AtTheGate(scene as Window ?? owner, scene, game, city, cityName, byLand);
        }
        finally
        {
            scene?.Close();
        }
    }

    /// <summary>성문 앞에서 문지기를 만나고 차림표를 돌린다.</summary>
    private static Outcome AtTheGate(Window owner, GateScene? scene, Engine.Game game, int city,
                                     string cityName, bool byLand)
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
        TalkDialog.Say(owner, gate, "", Standoff.Heard(Standoff.GateWord, heard));
        // 덧붙이는 것은 대원이다 — 문지기 얼굴을 그대로 두면 그가 말한 꼴이 된다.
        if (!heard)
            TalkDialog.Say(owner, null, "",
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
                (Standoff.Choices[Standoff.Leave], true),
            };

            int pick = ChoiceDialog.Pick(owner, Standoff.GateTitle(cityName), rows);
            switch (pick)
            {
                case Standoff.Attack:
                    // 물음이 둘이다(0x00551BF0 → 0x00551C00). 어느 하나라도 무르면 돌아간다.
                    if (!ConfirmDialog.Ask(owner, Standoff.SureWord, cityName)) break;
                    if (!ConfirmDialog.Ask(owner, Standoff.AttackWord, cityName)) break;

                    // 게임도 물음 뒤에 부대배치 화면부터 편다(0x0044A870 의 0x00446E60).
                    // 배치가 끝나면 그 길로 싸움터로 넘어간다.
                    if (LandDeployDialog.Show(owner, game, cityName) is not { } line) break;

                    var aide = player.Mates.Count > 0 && player.Mates[0].Length > 0
                        ? player.MateInfoOf(player.Mates[0]) : null;
                    var field = new LandBattle(line, player, aide,
                                               game.CityRows?.ScaleOf(city) ?? 0,
                                               nation, culture, CityField, dice);
                    if (!LandBattleScene.Run(owner, game, field, dice)) break;

                    // 이겼으면 그 도시는 그 뒤로 그냥 열린다 — 교섭·잠입으로 뚫었을 때와
                    // 같다("제독, 이것으로 마을에 들어갈 수 있습니다").
                    player.OpenGate(city);
                    NoticeDialog.Show(owner, string.Format(Standoff.TalkWonWord, where), "");
                    return new Outcome(true, false);

                case Standoff.Sneak:
                    // 잠입은 되든 안 되든 차림표가 다시 안 뜬다(0x004A57E7 이 반환값을
                    // 안 보고 고리를 빠져나간다). 달아났어도 그대로 물러선다.
                    return Sneak(owner, scene, game, dice, city, gate, heard);

                case Standoff.Talk:
                    // 게임도 돈부터 본다(0x00468BF0 → 「소지금이 모자랍니다!」). 한 번
                    // 걸리면 그 자리에서는 교섭 칸이 죽는다(0x004A5800).
                    if (player.Gold <= 0)
                    {
                        NoticeDialog.Show(owner, Standoff.TooPoorWord, "");
                        canTalk = false;
                        break;
                    }
                    if (Talk(owner, scene, game, dice, city, where))
                        return new Outcome(Entered: true, GameOver: false);
                    canTalk = false;          // 이번 방문에서는 다시 못 조른다
                    break;

                default:
                    // 떠난다, 또는 창을 닫았다.
                    NoticeDialog.Show(owner, Standoff.GiveUpWord, "");
                    return new Outcome(false, false);
            }
        }
    }

    /// <summary>
    /// 교섭한다 — 되면 돈을 건네고 문이 열린다(<c>0x004A55C0</c>).
    /// </summary>
    /// <remarks>
    /// 어그러지면 그 자리(마을 쪽·항구 쪽)에 <b>실패 표시</b>가 서서 <b>「교섭한다」가 죽는다</b> —
    /// 게임도 도시 레코드 <c>+0xB0</c>(마을) · <c>+0xB4</c>(항구)에 1 을 적고, 차림표를 깔
    /// 때 그 값을 도로 읽어 칸을 끈다.
    /// <code>
    ///   4a5779  cmp [도시+0xb0], 1      ; 마을 쪽 ([도시+0x9c] 이 0 이 아닐 때)
    ///   4a5781  cmp [도시+0xb4], 1      ; 항구 쪽
    ///   4a5788  sbb eax,eax; neg eax    ; 적힌 적 없으면 1(켜짐)
    ///   4a578f  → 교섭 줄의 켜짐 칸
    ///   4a5800  같은 칸을 0 으로 — 이번 판에 어그러졌거나 소지금이 0 일 때
    /// </code>
    /// <b>그 표시는 도시가 아니라 성문 화면 객체에 산다.</b> <c>+0xB0</c>·<c>+0xB4</c> 는
    /// 92바이트짜리 도시 레코드(<c>0x005863A8</c> + 번호 x 0x5C) 밖이고, 화면 객체는 성문에
    /// 다가설 때마다 새로 서므로 <b>물러섰다 다시 오면 교섭 칸이 되살아난다</b>. 그래서
    /// 우리도 세이브에 적지 않고 이 고리 안의 <c>canTalk</c> 하나로 든다.
    /// </remarks>
    private static bool Talk(Window owner, GateScene? scene, Engine.Game game, GameRandom dice,
                             int city, string where)
    {
        var player = game.Player;

        // 게임도 굴리고 나서 하트를 돌린다(0x004A55EE) — 깨지면 이미 진 것이다.
        bool won = Standoff.Talks(player, dice);
        scene?.PlayHeart(won);

        // 결과 문구는 0x00469680 이 부관 여부로 골라 <b>하나만</b> 낸다.
        // 부관이 있으면 부관이 말하고(「잘됐습니다…」·「교섭할 수 없군요…」),
        // 없으면 그냥 서술한다(「교섭에 성공/실패했습니다…」).
        bool aide = Standoff.HasAide(player);

        if (!won)
        {
            NoticeDialog.Show(owner,
                aide ? Standoff.TalkLostWord : string.Format(Standoff.TalkLostNews, where), "");
            return false;
        }

        int paid = player.Spend(Standoff.Price(player, dice));
        NoticeDialog.Show(owner, string.Format(Standoff.PaidWord, paid), "");
        NoticeDialog.Show(owner,
            string.Format(aide ? Standoff.TalkWonWord : Standoff.TalkWonNews, where), "");
        player.OpenGate(city);
        return true;
    }

    /// <summary>
    /// 잠입한다 — 되면 그대로 들어가고, 들키면 달아나거나 재판이다(<c>0x004A52F0</c>).
    /// </summary>
    /// <remarks>
    /// <b>어느 쪽이든 성문을 떠난다.</b> 차림표는 잠입이 돌려준 값을 아예 안 본다 —
    /// <c>0x004A57E2</c> 가 잠입을 부르고 <c>0x004A57E7</c> 이 곧장 고리 밖으로 뛴다.
    /// 달아났어도 다시 조를 기회를 안 준다는 뜻이다.
    /// </remarks>
    private static Outcome Sneak(Window owner, GateScene? scene, Engine.Game game,
                                 GameRandom dice, int city, uint[]? gate, bool heard)
    {
        var player = game.Player;

        // 그 도시가 쓰는 말을 얼마나 아는지. 셋에 못 미치면 부관이 말린다.
        // 부관이 없으면 아예 말이 없다 — 0x004A52F6 이 0x00468EF0 으로 먼저 막는다.
        int tongue = TongueAt(game, city);
        bool aide = Standoff.HasAide(player);
        if (aide)
            NoticeDialog.Show(owner,
                tongue >= Standoff.SafeTongue ? Standoff.TakeCare : Standoff.TongueTooThin, "");

        bool turban = HasTurban(game, player);
        if (turban)
            NoticeDialog.Show(owner, $"{Standoff.TurbanName}을 사용했다", "");

        // 게임도 굴리고 나서 동전을 돌린다(0x004A53D0) — 멎은 쪽이 곧 결과다.
        bool got = Standoff.Sneaks(player, tongue, turban, dice);
        scene?.PlayCoin(got);

        if (got)
        {
            // 게임은 여기서 아무 말도 안 한다 — 0x004A53DC 가 곧장 돌아서서 도시로 든다.
            player.OpenGate(city);
            return new Outcome(Entered: true, GameOver: false);
        }

        TalkDialog.Say(owner, gate, "", Standoff.Heard(Standoff.Spotted, heard));

        // 달아나기도 굴리고 나서 벌을 돌린다(0x004A5419 → 파트 0).
        bool away = Standoff.Escapes(player, dice);
        scene?.PlayEscape(away);

        if (away)
        {
            if (aide) NoticeDialog.Show(owner, Standoff.GotAwaySafe, "");
            return new Outcome(false, false);  // 차림표로 안 돌아간다 — 그대로 물러선다
        }

        return Trial(owner, scene, game, dice, gate, heard, aide);
    }

    /// <summary>
    /// 잡힌 뒤의 재판(<c>0x004A5439</c> 부터).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   4a546e  가벼움 = rand(2000) + 1000 &gt; max(0, 악명 - 운 - 1)
    ///   4a549f  그 굴림을 하트로 낸다
    ///   4a54af  가벼우면 rand(100) &lt; 운 + 1
    ///   4a54cc  그 굴림을 동전으로 낸다
    ///           되면 추방만, 아니면 벌금 + 소지금 몰수(0x004A5508)
    ///   4a5561  무거우면 "죽음으로서 속죄하라!" — 그대로 놀이가 끝난다(0x0044AF70)
    /// </code>
    /// <b>감옥에 갇히는 갈래는 없다.</b> 재판의 끝은 이 셋뿐이고, 갇혀 날짜가 흐르는
    /// 자리도 없다 — 그 사이의 <c>0x004A5AE0(-1, 1)</c> 은 <c>0x00428000(40, 1)</c> 을
    /// 부르는 <b>40밀리초 기다리기</b>지 날짜가 아니다.
    /// </remarks>
    private static Outcome Trial(Window owner, GateScene? scene, Engine.Game game,
                                 GameRandom dice, uint[]? gate, bool heard, bool aide)
    {
        var player = game.Player;
        TalkDialog.Say(owner, gate, "", Standoff.Heard(Standoff.Caught, heard));

        // ① 죄가 가벼운가 — 굴리고 나서 하트를 돌린다(0x004A549F).
        int weight = Math.Max(0, player.Infamy - player.AbilityOf(Ability.Luck) - 1);
        bool light = dice.Next(2000) + 1000 > weight;
        scene?.PlayHeart(light);

        if (!light)
        {
            TalkDialog.Say(owner, gate, "",
                Standoff.Heard($"거기는 악명 높은 {player.Name}군. 죽음으로서 속죄하라!", heard));
            return new Outcome(Entered: false, GameOver: true);
        }

        // ② 운을 한 번 더 — 굴리고 나서 동전을 돌린다(0x004A54CC).
        bool lucky = dice.Next(100) < player.AbilityOf(Ability.Luck) + 1;
        scene?.PlayCoin(lucky);

        if (lucky)
        {
            TalkDialog.Say(owner, gate, "", Standoff.Heard(Standoff.Banished, heard));
        }
        else
        {
            TalkDialog.Say(owner, gate, "", Standoff.Heard(Standoff.Fined, heard));
            player.Spend(player.Gold);
            NoticeDialog.Show(owner, Standoff.Robbed, "");
        }

        if (aide) NoticeDialog.Show(owner, Standoff.GiveUpHere, "");
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
