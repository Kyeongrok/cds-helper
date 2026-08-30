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
                                 GameMenuHost menu, GameMenuHost cityMenu, int cityTrack)
{
    private readonly int _cityTrack = cityTrack;
    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly string _cityName = cityName;
    private readonly GameMenuHost _menu = menu;
    private readonly GameMenuHost _cityMenu = cityMenu;

    private Player _player => _game.Player;
    private Random _random => _game.Random;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window ?? _view;

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
    public void Persuade(Patron patron)
    {
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

        // 얻은 힌트만 내밀 수 있다. 게임도 상태가 맞는 것만 목록에 올린다(0x0044E7B0).
        var mine = _player.Hints.Order().ToList();
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
        int half = funds / 2;

        int pick = TalkDialog.Ask(_view, face, "",
            $"모험하는데 돈은 필요하겠지. 먼저 금화 {half}닢을 주겠다. " +
            $"{it.Deadline}년 내에 성공하면 {half}닢의 사례를 약속하겠네. 이것으로 어떤가.\n\n" +
            $" 기간{it.Deadline}년 금화 {half}닢 ",
            "승낙한다", "교섭한다");
        if (pick != 0) return;      // 교섭은 아직 흉내내지 않는다

        // 계약을 적어 두고 선금을 받는다. 게임도 이 자리에서 소지금에 계약금의 절반을
        // 더한다(0x004ADF3E).
        _player.Sign(new Contract(it.Id, patron.Name, _cityName, funds,
                                  _player.Date, it.Deadline));

        Say("그러면, 기대하고 있겠네. 훌륭히 성공을 거두고 돌아오게.");
    }

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

        // 1. 이야기가 감당할 만한가.
        int weight = Persuasion.Weight(hint.Grade, _player.Fame);
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

        // 3. 말솜씨로 넘긴다.
        if (Persuasion.Talks(eye, rhetoric, charm, dice))
        {
            Say("흐음, 그다지 흥미가 없지만 자네의 부탁이라면 안 들어 줄 것도 없지.");
            return Persuasion.Verdict.Reluctant;
        }

        // 4. 못 넘겼다 — 다른 이야기라도 물어볼지, 아주 물릴지.
        if (more && Persuasion.Softens(eye, rhetoric, charm, dice))
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
    public string PatronRow(Patron patron) =>
        CanReport(patron) ? Facility.Report
      : Contracted(patron) ? Facility.Break
      : Facility.Persuade;

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
    /// 아직 안 옮긴 것 — 모조품 갈래, 남이 먼저 발표해 버렸을 때 깎이는 갈래, 선대의 계약.
    /// </remarks>
    public void Report(Patron patron)
    {
        var contract = _player.Contract;
        var rows = ReportTargets(patron);
        if (contract == null || rows.Count == 0) return;

        var face = FaceOf(patron);
        void Say(string text) => TalkDialog.Say(_view, face, "", text);

        bool inTime = contract.DaysLeft(_player.Date) > 0;
        Say(inTime ? "오오, 무사히 돌아왔는가! 자 빨리 성과를 들려 주게."
                   : "꽤 늦었군. 그래, 결과는 어떤가?");

        foreach (var row in rows)
        {
            Say($"[{row.Name}]{GameUi.Josa(row.Name, "을", "를")} 발견했습니다.");
            _player.Announce(row.Id);
        }

        Say("굉장하다! 잘 해냈네!! 사례는 듬뿍하겠네.");

        int paid = RewardFor(contract, inTime);
        _player.Earn(paid);
        _player.EndContract();

        GameDialog.Show(_view, $"금화 {paid}닢을 받았다!");
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
    public void BreakContract(Patron patron)
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

    /// <summary>보고 사례(<see cref="Palace.RewardFor"/>).</summary>
    private int RewardFor(Contract contract, bool inTime) =>
        Palace.RewardFor(contract.Unpaid, inTime, _random);


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
            PatronInfoDialog.Show(owner, patron, sponsor?.Name, sponsor?.Job);
        }
    }
}
