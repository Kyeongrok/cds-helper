using System.IO;
using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 후원자 — 설득 · 보고 · 계약중단, 그리고 스폰서 일람.
/// </summary>
/// <remarks>
/// 왕궁만의 일이 아니다. 후원자는 총독부·상관·학자 저택 어디든 앉고, <b>앉은 자리</b>에
/// 이 줄들이 붙는다(<see cref="TownWorks"/>). 게임도 같은 자리를 계약 상태로 갈아 끼운다
/// (<c>0x0044E630</c>).
///
/// 값과 판정은 <see cref="Palace"/> 가 알고(사례 · 눈감아 주기 · 보고할 것 고르기),
/// 여기서는 묻고 알리는 차례만 맡는다.
/// </remarks>
/// <param name="view">이 건물을 낸 도시 창. 대사 창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위, 후원자 표가 여기서 온다.</param>
/// <param name="cityName">이 마을 이름. 후원자는 마을과 해로 자리가 정해진다.</param>
/// <param name="menu">그 건물의 명령 창 — 설득·보고·계약중단이 이 줄에서 뻗는다.</param>
/// <param name="cityMenu">도시 커맨드 창 — 스폰서 일람이 이 줄에서 뻗는다.</param>
/// <param name="cityTrack">이 마을 곡. 알현이 끝나면 이 곡으로 되돌린다.</param>
internal sealed class PatronMenu(Window view, Engine.Game game, string cityName,
                                 GameMenuHost menu, GameMenuHost cityMenu, int cityTrack,
                                 int culture, int cityId)
{
    private readonly int _cityTrack = cityTrack;
    private readonly int _culture = culture;
    private readonly int _cityId = cityId;
    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly string _cityName = cityName;
    private readonly GameMenuHost _menu = menu;
    private readonly GameMenuHost _cityMenu = cityMenu;

    private Player _player => _game.Player;
    private Random _random => _game.Random;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window is { Visibility: Visibility.Visible } window
        ? window : _view;

    /// <summary>
    /// 후원자와 이야기하는 동안 <b>그 건물의 명령 창을 접는다</b>.
    /// </summary>
    /// <remarks>
    /// 게임은 알현이 시작되면 명령 창을 지우고 화면 가득 대사만 낸다. 우리 명령 창은
    /// 제 창(HWND)이라 도시 그림 위에 그대로 남아, 대사 창 옆에 얹힌 채로 보였다 —
    /// 애니메이션(하트·설득)까지 그 창에 가렸다.
    ///
    /// 대사 창들은 이미 도시 그림(<c>_view</c>)을 주인으로 삼으므로 접어도 탈이 없다.
    /// 어떻게 끝나든 도로 펴 준다.
    /// </remarks>
    private void Alone(Action run)
    {
        var window = _menu.Window;
        bool shown = window is { Visibility: Visibility.Visible };
        if (shown) window!.Visibility = Visibility.Hidden;
        try { run(); }
        finally { if (shown && window!.IsLoaded) window.Visibility = Visibility.Visible; }
    }

    /// <summary>후원자 자료. 한 번만 읽어 둔다.</summary>
    private static List<Patron>? _patrons;

    /// <summary>이 건물에 앉아 있는 후원자. 없으면 null.</summary>
    public Patron? At(string kind, HashSet<string> kindsHere) =>
        new PatronService().SeatedAt(LoadPatrons(), _cityName, _player.Date.Year, kind, kindsHere);

    /// <summary>
    /// 왕궁의 "설득" — 후원자에게 힌트를 내밀어 자금을 받아 낸다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004AEF50</c> 을 따라간다. 차례가 이렇다.
    /// <list type="number">
    /// <item>후원자를 찾는다. 없으면 "이 마을에는 아는 스폰서가 없습니다".</item>
    /// <item>힌트를 고르게 한다(<c>0x004AE8E0</c>). 안 고르면 "용건이 없는가".</item>
    /// <item>안목이 힌트 등급에 못 미치면 물린다 — "이야기가 막연하네".
    ///       게임 판정은 <c>안목/20 + (등급==5 ? 1 : 2) &gt;= 등급</c> 이다(<c>0x004AF0F0</c>).</item>
    /// <item>재력이 낼 돈에 못 미치면 물린다 — "돈이…".</item>
    /// <item>통과하면 선금·기한·사례금을 걸고 승낙을 묻는다.</item>
    /// </list>
    /// 대사는 게임 EXE 에 있는 말을 그대로 옮겼다(<c>0x00546778</c>~<c>0x00546D80</c>).
    /// 게임은 신분(왕궁·총독부·성)에 따라 세 벌을 갈아 쓰는데, 여기서는 왕궁 것 한 벌만 쓴다.
    ///
    /// <b>아직 안 되는 것</b> — 계약을 맺어 두지 않는다. 승낙해도 돈이 들어오거나 기한이
    /// 걸리지 않는다. 계약 상태를 어디에 적어 둘지(세이브 자리)를 아직 못 풀었다.
    /// </remarks>
    public void Persuade(Patron patron) => Alone(() => PersuadeNow(patron));

    private void PersuadeNow(Patron patron)
    {
        // 내밀 것이 없으면 그 자리에서 물린다(0x004769D4). 문간 관문보다 먼저다.
        if (LiveHints.Count == 0)
        {
            NoticeDialog.Show(_view, "설득 가능한 힌트가 없습니다");
            return;
        }

        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        string shown = sponsor?.Name ?? patron.Name;             // 게임 이름은 가운뎃점이 들어간다
        string sir = sponsor?.Honorific ?? "각하";
        string me = _player.Name;

        var face = FaceOf(patron);
        void Say(string words) => TalkDialog.Say(_view, face, "", words);
        void Steward(string words) => TalkDialog.Say(_view, StewardFace(), "", words);

        // 첫 관문은 명성이다. 모자라면 집사가 문간에서 돌려보낸다(게임 0x004AE1F0).
        //
        // 게임은 이 관문을 시설 종류 하나에만 건다(vtbl+0x48 이 3 일 때). 그 3 이 어느
        // 건물인지는 아직 못 갈라서 여기서는 다 건다.
        //
        // "명성치가 모자랍니다" 는 내지 않는다 — 게임에서도 그 줄은 디버그 깃발
        // (0x00580C6C 의 2비트)이 서 있을 때만 나오는 기록용이지 사람에게 보이는 말이 아니다.
        //
        // 아직 안 하는 것 — 게임에는 집사에게 뇌물을 주어 이 관문을 뚫는 길이 있다
        // ("매수한다" / "포기하고 돌아간다" → "집사에게 뇌물을 주겠습니다. 좋습니까?").
        if (_player.Fame < patron.Fame)
        {
            // 문 앞에서 돌려보낼 때 소리가 한 번 난다(닻 소리와 같은 파트다).
            _game.Sfx?.Play(SoundBank.TurnedAwayPart);
            Steward($"{shown}님은 바쁘셔서 만나실 수 없습니다.");
            return;
        }

        // 관문을 넘으면 집사가 맞고, 무기를 맡기고, 안에 들여보낸 뒤 주인에게 알린다.
        // 게임의 알현 순서 그대로다(문구도 EXE 0x005459B8~ 에서 옮겼다).
        //
        // 곡도 여기서 바뀐다 — 돌려보낼 때는 그대로고, 인사를 받고 들어갈 때부터 알현 곡이다.
        // 나갈 때 도시 곡으로 되돌리는 것은 아래 try/finally 가 맡는다.
        _game.Bgm.Play(BgmPlayer.SponsorTrack);
        try
        {
            Audience(patron, shown, sir, me, face, Say, Steward);
        }
        finally
        {
            _game.Bgm.Play(_cityTrack);   // 그 자리를 나오면 도시 곡으로 돌아간다
        }
    }

    /// <summary>알현 — 집사가 들여보낸 뒤부터 계약을 묻기까지.</summary>
    private void Audience(Patron patron, string shown, string sir, string me,
                          uint[]? face, Action<string> Say, Action<string> Steward)
    {
        Steward($"오래 기다리셨습니다. 제가 {shown} {sir}의 집사입니다.");
        Steward("무기는 여기서 보관하겠습니다. 그러면 안으로 들어가십시오.");
        Steward($"{sir}. {me}{Particle(me)} 데리고 왔습니다.");

        // 주인이 용건을 묻는다. 게임은 신분마다 말이 다르고 첫 방문이냐에 따라 또 갈리는데,
        // 여기서는 화면에서 본 한 벌만 쓴다.
        //
        // 이 자리에서 낯을 튼다. 게임도 이 물음 바로 뒤에 후원자의 "아직 못 만남" 표를
        // 지운다(0x004AE595 — 객체 +0x28 의 비트 15). 그래야 스폰서 일람에 뜬다.
        _player.Meet(patron.Name);
        Say($"흐음. {me}, 이번 모험 목적은 무엇인가?");

        // 얻었고 아직 보고 안 한 힌트만 내밀 수 있다 — 원본 힌트 상태로 13 인 것이다
        // (0x0044E7B0 이 상태가 맞는 것만 목록에 올린다). 보고를 마치면 0x004AACA0 이
        // bit1 을 켜서 여기서도 빠진다.
        var mine = LiveHints;
        var names = mine.Select(_game.HintName).ToList();
        int row = HintListDialog.Pick(_view, names, "제안 선택");
        if (row < 0)
        {
            Say("뭔가, 용건이 없는가? 이쪽은 바쁘네, 빨리 나가주게.");
            return;
        }

        var hint = _game.Hints?.Find(mine[row]);
        if (hint == null)
        {
            Say("흠, 원조해 주고 싶은 마음은 많지만.");
            return;
        }

        var it = hint.Value;

        // 받아 줄지는 게임 셈 그대로 가린다(Persuasion 참고) — 이야기 크기, 좋아하는
        // 갈래, 안목·웅변·매력 굴림 차례다.
        var verdict = Decide(it, patron, _game.Sponsors?.FindByName(patron.Name),
                             face, Say, mine.Count > 1);
        if (verdict is Persuasion.Verdict.Refused or Persuasion.Verdict.TooBig
                    or Persuasion.Verdict.AskAnother) return;

        int funds = Persuasion.Funds(
            it.Funds,
            _game.Sponsors?.FindByName(patron.Name)?.Closeness ?? DefaultCloseness,
            verdict);

        // 재력 판정 — 낼 돈이 없으면 물린다.
        if (patron.Wealth < funds)
        {
            Say("원조는 해 주고 싶지만, 흐~음, 돈이... , 또 다음번이다.");
            return;
        }

        // 계약금은 반으로 나뉜다 — 절반은 선금으로 그 자리에서 받고, 절반은 성공한 뒤에
        // 받는다. 제안 대사도 그 절반을 두 번 부른다(0x004AF1B6 이 계약금/2 를 두 번 넘기고,
        // 서식은 0x00546B80 "먼저 금화 %ld닢을 주겠다 … %ld닢의 사례" 다).
        int years = it.Deadline;
        int half = funds / 2;

        TalkDialog.Say(_view, face, "",
            $"모험하는데 돈은 필요하겠지. 먼저 금화 {half}닢을 주겠다. " +
            $"{years}년 내에 성공하면 {half}닢의 사례를 약속하겠네. 이것으로 어떤가.");

        // 말과 고르기는 <b>따로 뜨는 창 둘</b>이다. 말은 얼굴을 단 알림창(0x004694C0)이고,
        // 고르기는 제목 띠에 기간·금화를 이고 승낙/교섭 두 줄만 놓인 창(0x00469A70,
        // 0x004AF22A 가 줄 수 2 를 넘긴다)이다. 두 줄이 같은 무늬라 Pick 을 쓴다.
        // <b>제목 띠에는 계약금 전부가 뜬다</b> — 절반이 아니다(서식은 0x00546C18
        // " 기간%d년 금화 %ld닢 "). 절반은 바로 위 대사가 이미 두 번 불렀다.
        int pick = ChoiceDialog.Pick(_view, $" 기간{years}년 금화 {funds}닢 ",
                                     ["승낙한다", "교섭한다"]);
        if (pick < 0) return;

        // 「교섭한다」를 골랐을 때만 한 번 더 묻는다 — <b>되풀이는 없다</b>.
        if (pick != 0 && !Bargain(patron, face, Say, ref funds, ref years)) return;

        // 계약을 적어 두고 선금을 받는다. 게임도 이 자리에서 소지금에 계약금의 절반을
        // 더한다(0x004ADF3E).
        // 감찰관은 계약마다 반드시 하나 붙는다 — 얼굴은 늘 같고 이름만 갈린다.
        string inspector = Inspector.Pick(_culture, _random);

        _player.Sign(new Contract(it.Id, patron.Name, _cityName, funds,
                                  _player.Date, years, inspector));

        // 맺고 나면 배 → 감찰관 → 배웅 차례다(게임 0x004AF2A3 · 0x004AF2B7 · 0x004AF3A4).
        LendShips(funds, Say);
        SendInspector(inspector, me, Say);

        // 배웅도 신분마다 세 벌이다(0x00546D28 "그러면, %s, 기대하고 있겠네." 따위).
        // 화면에서 본 셋째 벌(0x00546DA8)을 쓴다.
        Say("기대하고 있겠네. 훌륭히 성공을 거두고 돌아오게.");
    }

    /// <summary>
    /// 후원자가 감찰관을 딸려 보낸다 — 배를 대 준 바로 다음이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004AF2B7</c>(<c>0x004AF450</c> 이 사람을 짓는다) 다음에 오는 말 두 마디다.
    /// <code>
    ///   546CE0  "감찰관으로서 %s%s 따라가 주게. %s, 부탁하네."   후원자 얼굴
    ///   546D10  "하앗, 알겠습니다."                              감찰관 얼굴(232)
    /// </code>
    /// 앞의 것은 신분마다 세 벌인데(<c>0x00546C80</c>·<c>0x00546CB0</c>·<c>0x00546CE0</c>)
    /// 화면에서 본 셋째 벌만 쓴다 — 이 글의 다른 대사와 같은 다룸이다.
    /// </remarks>
    private void SendInspector(string inspector, string me, Action<string> Say)
    {
        Say($"감찰관으로서 {me}{Particle(me)} 따라가 주게. {inspector}, 부탁하네.");

        var face = _game.Faces?.TryGetBgra(Inspector.Face, female: false);
        TalkDialog.Say(_view, face, "", "하앗, 알겠습니다.");
    }

    /// <summary>계약금이 이만큼 오를 때마다 배가 한 척씩 는다(<c>0x004105F4</c> 의 <c>0xEA60</c>).</summary>
    private const int GoldPerShip = 60000;

    /// <summary>
    /// 계약 직후 스폰서가 항구에 배를 대 준다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00410620</c> 이다. 설득이 끝나면 <c>0x004AF2A3</c> 이 <b>첫 인자에 1 을
    /// 넣어</b> 부르는데, 그 갈래는 묻지 않고 그냥 준다(그냥 찾아가 빌릴 때는 0 이고,
    /// 그때만 "배를 빌리겠습니까?" <c>0x0055C4D8</c> 를 묻는다).
    /// <code>
    ///   410677  척수 = 0x004105C0(빌려줄 수 있는 배, 계약금)
    ///     4105F4    계약금 / 60000 + 1
    ///     410605    min(그 값, 항구에 세워 둔 스폰서 배 수)
    ///     41060B    min(그 값, 8 - 내 함대 척수)
    ///   410718  0x00410800 — 배가 한 척이라도 있으면 1
    ///   410724  그러면 "배를 빌리겠습니까?"(0x55C4D8) 를 묻는다. 예(2) 라야 준다
    ///   41073A  아니라면 "그런가. 그렇다면, 좋을 대로 하게."(0x55C6C0 세 벌) 로 물린다
    ///   4106ED  "…배 %d척을 항구에 준비시켜 놓겠네" (문화권 3벌: 0x55C3F8·0x55C438·0x55C480)
    ///   410798  0x0040FA00 이 배 레코드(0x005A4E18) 의 <b>+0x64 에 1</b> 을 박는다 = 대출 표시
    ///   410763  줄 배가 없으면 "…배가 전부 나가고 없네" (0x55C718 세 벌)
    /// </code>
    /// 화면에서 본 것은 셋째 벌이라 그것을 쓴다 — 계약금 13,500닢에 1척이 나왔고
    /// <c>13500 / 60000 + 1 = 1</c> 로 셈이 맞는다.
    ///
    /// <b>못 옮긴 것 셋.</b> 스폰서마다 항구에 세워 둔 배 무리가 우리 쪽에 없어 가운데
    /// 상한을 뺐고, 선체도 고를 데가 없어 제일 싼 것으로 세운다. 그리고 <b>돌려주는 자리가
    /// 아직 없다</b> — 게임은 계약이 끝나면 거둬 가며 「스폰서에게 배를 반환했습니다」
    /// (<c>0x0055C2B0</c>) 를 내는데, 우리 쪽에서는 그냥 내 배로 남는다.
    /// </remarks>
    private void LendShips(int funds, Action<string> Say)
    {
        int ships = Math.Min(funds / GoldPerShip + 1, Player.MaxShips - _player.Ships.Count);
        if (ships <= 0)
        {
            Say("흐음, 빌려주고 싶은 마음은 굴뚝같지만 배가 전부 나가고 없네. 다시 오게.");
            return;
        }

        // <b>후원자가 먼저 내주겠다고 말한 뒤에</b> 빌릴지 묻는다 — 물음창이 먼저 뜨면
        // 무엇을 빌리는지 모른 채 고르게 된다. 게임 차례가 그렇다.
        Say($"모험의 도움을 위해서 배 {ships}척을 항구에 준비시켜 놓겠네. "
          + "마음대로 사용해도 상관없네.");

        // 배가 한 척이라도 있으면 빌릴지 묻는다. 한 척도 없으면 묻지 않고 그냥 준다
        // (0x00410718 이 0x00410800 으로 가려, 0 이면 물음창을 건너뛴다).
        if ((_player.Ships.Count > 0 || _player.DockedAt(_cityId).Count > 0)
            && !ConfirmDialog.Ask(_view, "배를 빌리겠습니까?"))
        {
            Say("그런가. 그렇다면, 좋을 대로 하게.");
            return;
        }

        if (Hull.All.MinBy(h => h.Price) is not { } hull) return;

        // 함대에 곧장 들어가지 않는다 — 항구에 「대출 · 계류」로 대 놓인다.
        int given = 0;
        for (int i = 0; i < ships; i++) if (_player.Give(hull, _cityId)) given++;
        if (given == 0) return;

        if (_player.Contract is { } deal) deal.ShipsLent = true;
    }

    /// <summary>
    /// 「교섭한다」 — 자금을 올리거나 기간을 늘린다(<c>0x004AEDA0</c>).
    /// </summary>
    /// <remarks>
    /// 세 줄짜리 고르기 창이 뜬다(<c>0x004AEDF7</c> 이 줄 수 3 을 넘긴다).
    /// <code>
    ///   자금 증가   자금 x 1.3, 기간 절반   — <b>기간이 2년 이상이라야 고를 수 있다</b>
    ///   기간 연장   기간 x 1.5, 자금 x 0.7  — 1년이면 2년으로
    ///   변경 없음   그대로
    /// </code>
    /// 두 셈 다 <b>10닢 단위로 내린다</b> — 게임이 <c>x13/10/10*5*2</c> 꼴로 셈해서다
    /// (<c>0x004AEE32</c> · <c>0x004AEEE4</c>).
    ///
    /// 자금을 올려 달랬는데 후원자의 재력이 못 미치면 쫓겨난다(<c>0x004AEE7D</c>) —
    /// "탐욕스러운 놈! 너 같은 녀석에게 볼일 없다. 썩 꺼져라!"(<c>0x00546430</c>).
    /// </remarks>
    /// <returns>계약으로 넘어가면 true, 물러났거나 쫓겨났으면 false.</returns>
    private bool Bargain(Patron patron, uint[]? face, Action<string> Say,
                         ref int funds, ref int years)
    {
        int at = ChoiceDialog.Pick(_view, " 교섭 ",
            [("자금 증가", years > 1), ("기간 연장", true), ("변경 없음", true)]);

        // <b>「변경 없음」은 바로 계약으로 간다</b> — 제안을 다시 묻지 않는다.
        // 게임도 그 갈래가 1 을 내고(0x004AEF40), 부르는 쪽은 1 이면 계약을 맺는다
        // (0x004AF249 가 0 이 아니면 0x004AF28A 로 간다).
        if (at < 0 || at == 2) return true;

        if (at == 0)
        {
            int raised = To10(funds * 13 / 10);
            if (patron.Wealth < raised)
            {
                Say("탐욕스러운 놈! 너 같은 녀석에게 볼일 없다. 썩 꺼져라!");
                return false;
            }

            funds = raised;
            years /= 2;
            Say($"흐음, 좋다. 돈은 전부 {funds}닢 주겠다. " +
                $"그대신 기간은 {years}년으로 줄어드네. 이의없겠지.");
        }
        else
        {
            funds = To10(funds * 7 / 10);
            years = years > 1 ? years * 15 / 10 : years + 1;
            Say($"흐음, 좋다. 기간은 {years}년으로 늘려도 상관없네. " +
                $"그대신 돈은 전부 {funds}닢 이상 줄 수 없네. 이의없겠지.");
        }

        // 새 값으로 한 번만 되묻는다. 마다하면 이야기가 끝난다.
        return ConfirmDialog.Ask(_view, $"기간{years}년 금화 {funds}닢으로 하겠습니까?",
                                 face: face);
    }

    /// <summary>10닢 단위로 내린다 — 게임의 <c>/10*10</c> 꼴이다.</summary>
    private static int To10(int coins) => coins / 10 * 10;

    /// <summary>친밀도를 모를 때 쓰는 밑값. 표를 못 읽었을 때다.</summary>
    private const int DefaultCloseness = 60;

    /// <summary>
    /// 이야기를 받아 줄지 가린다 — 게임 <c>0x004AE5F0</c> 의 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 말은 세 벌(문화권별)이 있는데 우리는 한 벌만 쓴다. 아주 물리면 게임은 후원자의
    /// 기분을 상하게 해 한동안 안 만나 주는데, 그 자리는 아직 안 들고 있다.
    /// </remarks>
    private Persuasion.Verdict Decide(HintTable.Hint hint, Patron patron,
                                      SponsorTable.Sponsor? sponsor,
                                      uint[]? face, Action<string> Say, bool more)
    {
        var dice = new GameRandom(Environment.TickCount);
        var stage = _view as CityPicView;

        // 1. 이야기가 감당할 만한가. 게임은 여기서 <b>설득 애니메이션(5번)</b>을 돌린다
        //    (0x004AE68D) — 감당할 만하면 청을 들어주고 아니면 엎어진다.
        int weight = Persuasion.Weight(hint.Grade, _player.Fame);
        stage?.PlayFameCheck(weight == 0);

        if (weight == 2)
        {
            Say("그런 이야기는 들어본 적도 없다. 자네에게는 짐이 너무 무거울 걸세.");
            return Persuasion.Verdict.TooBig;
        }
        if (weight == 1)
        {
            // 무겁지만 우겨 볼 만하다 — 한 번 더 묻고 그래도 하겠다면 받아 준다.
            if (!ConfirmDialog.Ask(_view, "자네에게는 짐이 너무 무거우리라 생각되는데... "
                                        + "꼭 하고 싶은가?", face: face))
                return Persuasion.Verdict.TooBig;
            return Persuasion.Verdict.Reluctant;
        }

        // 2. 좋아하는 갈래면 두말이 없다.
        if (Persuasion.Likes(sponsor?.Tastes ?? 0, hint.Category))
        {
            Say("흐음, 흥미있군.");
            return Persuasion.Verdict.Interested;
        }

        int eye = sponsor?.Eye ?? patron.Discernment;
        int rhetoric = _player.LevelOf(Skill.Names[Skill.Rhetoric]);
        int charm = _player.AbilityOf(Ability.Charm);

        // 3. 말솜씨로 넘긴다. 굴림 결과가 곧 <b>하트(3번)</b>다 — 이기면 커지고 지면 깨진다.
        bool talked = Persuasion.Talks(eye, rhetoric, charm, dice);
        stage?.PlayHeart(talked);

        if (talked)
        {
            Say("흐음, 그다지 흥미가 없지만 자네의 부탁이라면 안 들어 줄 것도 없지.");
            return Persuasion.Verdict.Reluctant;
        }

        // 4. 못 넘겼다 — 다른 이야기라도 물어볼지, 아주 물릴지. 여기서도 하트가 돈다.
        bool softened = more && Persuasion.Softens(eye, rhetoric, charm, dice);
        if (more) stage?.PlayHeart(softened);

        if (softened)
        {
            Say("흐음, 썩 내키지 않는군. 좀더 흥미있는 이야기는 없는가?");
            return Persuasion.Verdict.AskAnother;
        }

        Say("그런 쓸데없는 이야기에 버릴 돈은 없네.");
        return Persuasion.Verdict.Refused;
    }

    /// <summary>

    /// 그 후원자에게 <b>보고</b>할 수 있는지 — 계약을 맺은 그 자리이고 맡은 것을 찾아 왔는가.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044EA00</c> 이다.
    /// <code>
    ///   0x0044E9E0  계약이 있고 그 계약을 맺은 자리인가(0x0044E550 → 0x00493DB0)
    ///   0x0044E880  보고할 것이 하나 이상인가 — 계약의 유적 번호(0x00493E60)로 모은다
    /// </code>
    /// 게임은 도시와 <b>시설 종류</b>까지 견주는데 우리 계약은 후원자 이름과 마을을 들고
    /// 있으므로 그 둘로 가른다 — 결과는 같다(한 사람은 한 자리에만 앉는다).
    /// </remarks>
    private bool CanReport(Patron patron) => ReportTargets(patron).Count > 0;

    /// <summary>
    /// 후원자가 앉은 건물의 첫 줄 — 계약 상태로 갈린다.
    /// </summary>
    /// <remarks>
    /// 게임도 셋을 같은 자리에 갈아 끼운다(<c>0x0044E630</c> 이 <c>+0xB0</c> 을 정한다).
    /// <code>
    ///   0x0044E9A0  설득     = 이 자리에 후원자가 있고 내밀 힌트가 있고 <b>계약 중이 아니다</b>
    ///   0x0044E9E0  계약중단 = 이 후원자와 <b>계약 중</b>이다
    ///   0x0044EA00  보고     = 계약중단 조건 + <b>보고할 발견물이 있다</b>
    /// </code>
    /// 그래서 계약을 맺어 두고 아무것도 못 찾은 채 찾아가면 "계약중단" 만 뜬다 —
    /// 그 자리에서 다시 설득할 수는 없다.
    /// </remarks>
    /// <returns>붙일 줄이 없으면 빈 문자열 — 그러면 후원자 줄이 아예 안 뜬다.</returns>
    public string PatronRow(Patron patron) =>
        CanReport(patron) ? Facility.Report
      : Contracted(patron) ? Facility.Break
      : CanPersuade ? Facility.Persuade
      : "";

    /// <summary>
    /// 후원자가 앉아 있으면 "설득" 줄은 <b>늘 뜬다</b>.
    /// </summary>
    /// <remarks>
    /// 한때 내밀 힌트가 없으면 줄부터 감췄는데 <b>게임은 그렇지 않다</b> — 눌러 보고 나서
    /// 「설득 가능한 힌트가 없습니다」로 물린다(<c>0x004769D4</c> 가 <c>0x0055E548</c> 을 낸다).
    /// <code>
    ///   004769c6  call 0x0044E7B0(&amp;buf)   ; 내밀 수 있는 힌트를 모은다
    ///   004769d0  test esi, esi             ; 하나도 없으면
    ///   004769d4  push 0x0055E548           ;   "설득 가능한 힌트가 없습니다"
    ///   004769e5  eax = -1                  ;   그러고 물러난다
    /// </code>
    /// </remarks>
    private static bool CanPersuade => true;

    /// <summary>
    /// 아직 살아 있는 힌트 — 얻었고 아직 보고 안 한 것이다(원본 힌트 상태 13).
    /// </summary>
    /// <remarks>
    /// 보고까지 마친 힌트는 여기서 빠진다. 왜 «발견» 이 아니라 «보고» 인지는
    /// <see cref="DiscoveryLog.IsHintDone"/> 에 적어 두었다.
    /// </remarks>
    private List<int> LiveHints =>
        _game.Discoveries?.LiveHints(_player) ?? [.. _player.Hints.Order()];

    /// <summary>이 후원자와 이 자리에서 계약 중인지(<c>0x0044E550</c>).</summary>
    private bool Contracted(Patron patron) =>
        _player.Contract is { } c && c.Sponsor == patron.Name && c.City == _cityName;

    /// <summary>
    /// 그 후원자에게 보고할 발견물. 계약의 유적 번호를 가진 것 중 발견했고 아직 안 알린 것이다.
    /// </summary>
    private List<DiscoveryTable.Record> ReportTargets(Patron patron) =>
        Palace.ReportTargets(_player, patron.Name, _cityName,
                             _game.Discoveries?.Table, _game.Hints);

    /// <summary>
    /// 맡은 것을 찾아 왔다고 후원자에게 알린다 — 사례를 받고 계약이 끝난다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044ED9A</c> → <c>0x00412020</c> 이다. 사례를 셈하는 자리는
    /// <c>0x00411D10</c> 이고, 밑값이 <b>미불(계약금/2)</b> 이다.
    /// <code>
    ///   411d1f  push 0x1E ; call 0x4B7C0F      ; rand(30)
    ///   411d29  ecx = eax + 0x78               ; 기한 안이면 120 + rand(30) %
    ///   411d47  esi = 0x5A - rand(0x14)        ; 늦었으면  90 - rand(20) %
    ///   411d3b  eax = 계약금 / 2               ; 미불
    ///   411d3e  imul ; div 100                 ; 미불 x 비율 / 100
    /// </code>
    /// 100닢 단위로 내린다(<c>0x004117D0</c> — 100 이하면 그대로 둔다).
    /// 받은 돈은 <c>0x0041200E</c> 가 소지금에 더한다.
    ///
    /// 게임은 여기서 발견물의 사람 칸 2 를 채우고 깃발 <c>0x80</c> 을 세운다
    /// (<c>0x004AACA0</c>, 볼트 23) — 우리 쪽의 "알림" 과 같은 자리라 그렇게 적는다.
    /// 그래서 <b>계약으로 맡은 것은 항구에서 못 알리고 여기서만 매듭이 지어진다.</b>
    ///
    /// 발견물 하나마다의 셈은 <c>0x004111D0</c> 이고 <see cref="Palace"/> 에 옮겼다 —
    /// 명성 <see cref="Palace.FameFor"/>, 친밀도 <see cref="Palace.ClosenessFor"/>,
    /// 후원자에게 쌓이는 값 <see cref="Palace.CreditFor"/> 다.
    ///
    /// 아직 안 옮긴 것 — 모조품 갈래("이것은 모조품이네", <c>0x00530BC8</c>), 선대의 계약.
    /// </remarks>
    public void Report(Patron patron) => Alone(() => ReportNow(patron));

    /// <summary>
    /// 발견물을 하나씩 보고한다 — 게임의 <c>0x00412020</c> 안쪽 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 발견물마다 <b>"…의 발견을 보고했다!!"</b>(<c>0x00530BA8</c>)를 내고, 동영상이나
    /// 그림이 있으면 그것을 튼 뒤 친밀도 · 아이템 · 명성 차례로 낸다(<c>0x004111D0</c>).
    /// 사례는 다 끝나고 한 번이다.
    /// </remarks>
    /// <returns>받은 사례(닢).</returns>
    private int ReportEach(Patron patron, Contract contract,
                           IReadOnlyList<DiscoveryTable.Record> rows, bool inTime)
    {
        string me = _player.Name;
        int fame = 0, closer = 0;

        foreach (var row in rows)
        {
            GameDialog.Show(_view,
                $"{me}{GameUi.Josa(me, "은", "는")} {row.Name}의 발견을 보고했다!!");

            // 동영상이 있으면 틀고, 없고 그림만 있으면 그림을 낸다 — 발견할 때와 같다.
            if (row.Movie >= 0)
                MoviePlayer.Play(_view, DiscoveryDialog.MovieOf(_game.Directory, row.Movie));
            else if (row.Picture >= 0)
                DiscoveryDialog.Show(_view, _game.Stills, row.Picture, row.Name);

            _player.Announce(row.Id);

            // 좋아하는 갈래를 물어다 주면 덤이 붙는다(0x004ADAE0 이 후원자 표 +0x38 을 본다).
            int by = Palace.ClosenessFor(row, inTime, KnownByOthers,
                                         patron.Likes(row.Category), _random);
            if (by != 0)
            {
                _player.Endear(patron.Name, by);
                GameDialog.Show(_view, by > 0 ? "친밀도가 올라갔다!" : "친밀도가 내려갔다!");
            }
            closer += by;

            // 그 발견물이 준 물건은 후원자가 <b>돌려준다</b> — "이것은 자네가 가지고 가게"
            // 하고 소지품에 넣는다(0x004113F5 → 0x004B1710). 빼앗기는 것이 아니다.
            // 서적·유물(아이템 분류 7)만 그렇고, 이미 들고 있으면 그대로 둔다.
            if (row.GivesItem && _game.Items?.Find(row.ItemId) is { } gift
                && gift.Category == Palace.KeepsakeCategory
                && !_player.Items.Contains(row.ItemId) && _player.Take(row.ItemId))
                GameDialog.Show(_view, $"[{gift.Name}]{GameUi.Josa(gift.Name, "을", "를")} 손에 넣었다!");

            // 알린 것마다 명성이 오른다. 항구 발표(보수/70)와 셈이 다르다 — 보고는
            // 보수/50 이고 늦으면 그 반이다(0x004111D0).
            int up = Palace.FameFor(row, inTime, KnownByOthers);
            if (up > 0)
            {
                _player.Fame += up;
                GameDialog.Show(_view, $"명성이 {up} 올라갔다!");
                // 명성이 오른 때만 함께 딸려 온다 — 항구 발표와 같은 두 줄이다(0x0041156A).
                Harbor.Celebrate(_player);
            }
            fame += up;
        }

        if (fame == 0 && closer == 0)
            TalkDialog.Say(_view, FaceOf(patron), "", "굉장하다! 잘 해냈네!! 사례는 듬뿍하겠네.");

        int paid = RewardFor(contract, inTime);
        _player.Earn(paid);
        _player.EndContract();
        return paid;
    }

    /// <summary>
    /// 남이 먼저 보고해 버린 발견물인가. <b>우리 쪽에서는 늘 거짓이다.</b>
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x004AADB0</c> 으로 가리고, 참이면 <b>명성이 한 톨도 안 오르고</b> 사례도
    /// 계약금/4(늦었으면 0)로 깎인다.
    ///
    /// <b>원본에서도 이것이 켜지는 일은 없다.</b> 남의 이름을 사람 칸 2 에 올리는 길은
    /// 이벤트 명령 둘뿐인데(<c>0x3F</c> · <c>0x68 0B</c>, 볼트 23), 딸려 오는 대본
    /// (<c>DISEV.CDS</c> · <c>STORY0/1.CDS</c> · <c>HIST_EV.CDS</c>) 어디에도 그 명령이
    /// 없다. 새 판은 세 칸을 모두 비우고 시작한다(<c>0x004AA9B3</c>). 그러니 이 자리는
    /// 거짓이 맞고, 셈만 <see cref="Palace.FameFor"/> 에 갖춰 둔다.
    /// </remarks>
    private const bool KnownByOthers = false;

    private void ReportNow(Patron patron)
    {
        var contract = _player.Contract;
        var rows = ReportTargets(patron);
        if (contract == null || rows.Count == 0) return;

        var face = FaceOf(patron);
        void Say(string text) => TalkDialog.Say(_view, face, "", text);

        bool inTime = contract.DaysLeft(_player.Date) > 0;
        Say(inTime ? "오오, 무사히 돌아왔는가! 자 빨리 성과를 들려 주게."
                   : "꽤 늦었군. 그래, 결과는 어떤가?");

        // 인사 다음에 <b>계약 정보 창</b>이 뜬다 — 발견물과 증거품이 거기 적힌다.
        var sheet = GameInfo.ContractSheetOf(_game);
        ContractDialog.Show(_view, sheet.Contract, _player.Date,
                            sheet.HintName, sheet.Found, sheet.Evidence,
                            _game.Sponsors?.FindByName(sheet.Contract?.Sponsor ?? "")?.Name);

        var stage = _view as CityPicView;
        int paid;
        try
        {
            // 보고하는 동안 도시 그림이 파래진다 — 바다에서 발견할 때와 같다.
            stage?.Shade(true);
            paid = ReportEach(patron, contract, rows, inTime);
        }
        finally
        {
            stage?.Shade(false);
        }

        // 사례는 파란 막이 걷힌 뒤에 받는다.
        GameDialog.Show(_view, $"금화 {paid}닢을 받았다!");

        // 보고가 끝나면 그 줄이 사라져야 한다 — 계약이 없어졌으니 「보고」 줄도 없다.
        // 줄 목록을 다시 지어 그리게 한다(TownWorks.LinesOf 가 후원자 줄을 다시 고른다).
        _menu.Refresh();
    }

    /// <summary>
    /// 계약중단 — 계약을 깨고 위약금을 문다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044F7A0</c> 이다. <b>기한이 지났다고 저절로 무슨 일이 나지는 않는다</b> —
    /// 후원자를 다시 찾아갔을 때에야 따진다.
    /// <code>
    /// 44f7aa  ebx = (남은기한 &gt; 0) ? 1 : 0                ; 0x004ADB40
    /// 44f7c1  0x0044F2E0(건물, ebx)                        ; 내 쪽 대사
    /// 44f7c9  0x0044F4C0(건물, ebx)                        ; 집사 대사 — 기한을 넘겼으면 딴 말
    /// 44f7d6  edi = 0x0044F8B0(후원자, ebx)                 ; 용서받나
    /// 44f826  위약금 = 계약금 / 2                            ; 받은 선금과 같다
    /// 44f831  못 내면 "위약금을 지불할 수 없습니다!" 하고 미움을 산다
    /// 44f895  0x0044EEA0(건물)                              ; 계약을 끝낸다
    /// </code>
    /// 용서 판정은 이렇다.
    /// <code>
    /// 44f8b0  주사위 = rand(기한을 넘겼으면 150, 아니면 100)
    /// 44f8ca  문턱  = min(97, [후원자+0x20] + [0x5B60D0] + 1)
    /// 44f8de  용서받는다 = 주사위 &lt; 문턱
    /// </code>
    /// <c>[후원자+0x20]</c> 은 후원자 표(<c>0x005228B8</c>)의 <b>명성 / 100</b> 이다 —
    /// 국왕이 90 넘고 장사치가 한 자리라 <b>높은 사람일수록 너그럽다</b>.
    /// <c>[0x5B60D0]</c> 은 설득(<c>0x0044EF62</c>)도 쓰는 주인공 값인데 무엇인지 못 짚었다 —
    /// 여기서는 <b>내 명성 / 100</b> 을 넣었다.
    /// </remarks>
    public void BreakContract(Patron patron) => Alone(() => BreakContractNow(patron));

    private void BreakContractNow(Patron patron)
    {
        if (_player.Contract is not { } contract) return;

        var owner = Owner;
        var face = FaceOf(patron);
        void Say(string text) => TalkDialog.Say(_view, face, "", text);

        bool overdue = contract.IsOverdue(_player.Date);
        if (!ConfirmDialog.Ask(owner, overdue
                ? "기한을 넘겼다. 계약을 그만두겠나?"
                : "계약을 그만두겠나?")) return;

        _cityMenu.Close();

        // 집사가 먼저 알린다. 기한을 넘겼으면 말이 달라진다.
        string me = _player.Name;
        Say(overdue
            ? $"{patron.Name}님. {me}{GameUi.Josa(me, "이", "가")} 돌아왔습니다. " +
              "기한을 넘은 데다, 아무런 성과도 없는 듯 합니다만."
            : $"{patron.Name}님. {me}{GameUi.Josa(me, "이", "가")} 왔습니다. " +
              "뭔가, 계약을 파기하고 싶다고 합니다만.");

        bool forgiven = Forgiven(patron, overdue);
        if (!forgiven)
        {
            Say("후~... 계약을 파기하리라고는.");
            _player.EndContract();
            GameDialog.Show(_view, "제독, 곤란하게 되었습니다... 위험하니 일단 스폰서와는 " +
                                  "가까이 하지 않는 것이 좋을 것 같군요.");
            return;
        }

        Say(overdue
            ? "기대가 빗나갔군! 이번 실패는 잊어주지. 생각이 바뀌기 전에 나가주게."
            : "안됐군요, 무리하게 보내서는 성과도 없을테니, 이 계약은 잊어버립시다.");

        int penalty = contract.Penalty;
        if (!_player.Pay(penalty))
        {
            GameDialog.Show(_view, "위약금을 지불할 수 없습니다!");
            Say("바보같은, 위약금을 지불할 수 없다고! 어디까지 어리석은...");
            _player.EndContract();
            GameDialog.Show(_view, "제독, 곤란하게 되었습니다... 위험하니 일단 스폰서와는 " +
                                  "가까이 하지 않는 것이 좋을 것 같군요.");
            return;
        }

        _player.EndContract();
        GameDialog.Show(_view, $"위약금으로 금화 {penalty}닢을 물었다.");
    }

    /// <summary>계약을 깨는 것을 후원자가 눈감아 주는지(<see cref="Palace.Forgiven"/>).</summary>
    private bool Forgiven(Patron patron, bool overdue) =>
        Palace.Forgiven(patron.Fame, _player.Fame, overdue, _random);

    /// <summary>
    /// 보고 사례. 남이 먼저 발표해 버렸으면 <b>깎인 사례</b>다(<c>0x00411FC0</c> 이 가른다).
    /// </summary>
    private int RewardFor(Contract contract, bool inTime) =>
        KnownByOthers ? Palace.ScoopedRewardFor(contract.Amount, inTime)
                      : Palace.RewardFor(contract.Unpaid, inTime, _random);


    /// <summary>그 후원자의 얼굴. 표나 그림을 못 읽으면 null 이고, 그러면 대사만 나온다.</summary>
    private uint[]? FaceOf(Patron patron)
    {
        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        if (sponsor == null) return null;
        return _game.Faces?.TryGetBgra(sponsor.Value.Face, sponsor.Value.IsFemale);
    }

    /// <summary>취차(집사)의 얼굴. 어느 후원자에게 가든 같은 사람이다.</summary>
    private uint[]? StewardFace() =>
        _game.Faces?.TryGetBgra(SponsorTable.StewardFace, female: false);

    /// <summary>이름 뒤에 붙는 목적격 조사. 받침이 있으면 "을", 없으면 "를".</summary>
    /// <remarks>
    /// 게임도 조사를 따로 끼워 넣는다 — "%s%s 데리고 왔습니다" 의 두 번째 자리가 이것이다.
    /// </remarks>
    private static string Particle(string name)
    {
        if (name.Length == 0) return "를";
        char last = name[^1];
        if (last is < '가' or > '힣') return "를";       // 한글이 아니면 그냥 둔다
        return (last - '가') % 28 == 0 ? "를" : "을";
    }


    /// <summary>후원자 자료. 못 읽으면 빈 목록이다 — 그렇다고 도시 화면까지 막을 일은 아니다.</summary>
    private static List<Patron> LoadPatrons()
    {
        if (_patrons != null) return _patrons;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "patrons.json");
            _patrons = new PatronService().LoadPatrons(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[City] 후원자 자료 없음: {ex.Message}");
            _patrons = [];
        }
        return _patrons;
    }


    /// <summary>
    /// 「스폰서 일람」 — 한 번이라도 만난 후원자를 늘어놓는다.
    /// </summary>
    /// <remarks>
    /// 게임의 목록 짓는 곳은 <c>0x00476660</c> 이다. 후원자 81명을 죽 훑으며 두 가지를 본다.
    /// <code>
    ///   vtbl[0x38]  0x004ADD70  지금 그 자리에 앉아 있는 사람인가(같은 자리를 여럿이 나눠 쓴다)
    ///   vtbl[0x34]  0x004AD800  후원자 객체 +0x28 의 비트 15 — 서 있으면 뺀다
    /// </code>
    /// 비트 15 는 <b>아직 못 만났다</b> 는 표다. 알현이 이루어져 주인이 "…모험 목적을 말해
    /// 보게" 하고 물을 때 지운다(<c>0x004AE595</c>). 그래서 <b>한 번 만나야 목록에 뜬다.</b>
    ///
    /// 우리 쪽은 비트를 따로 들지 않고 <see cref="Player.Met"/>(낯을 튼 사람)로 가른다.
    /// 자리 판정은 <see cref="Patron.IsActive"/> 로 물러선다 — 게임처럼 같은 자리를 두고
    /// 다투는 것까지는 못 가리지만, 대가 갈려 물러난 사람은 걸러진다.
    ///
    /// 이름은 게임 표에서 가져온다 — <c>patrons.json</c> 은 "페르난 마르틴스" 인데 게임 화면은
    /// 가운뎃점을 쓴다("페르난·마르틴스").
    /// </remarks>
    public void ShowPatrons()
    {
        int year = _player.Date.Year;
        var table = _game.Sponsors;

        var mine = LoadPatrons()
            .Where(p => p.IsActive(year) && _player.HasMet(p.Name))
            .Select(p => (Patron: p, Row: table?.FindByName(p.Name)))
            .ToList();
        var names = mine.Select(m => m.Row?.Name ?? m.Patron.Name).ToList();

        // 고르면 상세를 띄우고 닫으면 목록으로 돌아온다 — 게임도 그렇다(0x0049348E 가
        // 목록 짓는 데로 되돌아간다).
        var owner = _cityMenu.Window ?? _view;
        while (true)
        {
            int row = HintListDialog.Pick(owner, names, "스폰서 일람",
                                          "이 마을에는 아는 스폰서가 없습니다");
            if (row < 0 || row >= mine.Count) return;

            var (patron, sponsor) = mine[row];
            PatronInfoDialog.Show(owner, patron, sponsor?.Name, sponsor?.Job,
                                  _player.ClosenessOf(patron.Name));
        }
    }
}
