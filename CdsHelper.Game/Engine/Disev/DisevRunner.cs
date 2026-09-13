using System.IO;
using System.Windows;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// DISEV.CDS 의 발견 대본을 <b>돌린다</b>.
/// </summary>
/// <remarks>
/// 편집기(<see cref="DisevArchive"/> · <see cref="DisevPart"/> · <see cref="DisevScript"/>)는
/// 대본을 읽고 고칠 뿐 돌리지는 않았다. 그래서 발견 알림이 표에서 지어 낸 한 줄뿐이었다.
/// 이것이 그 대본을 차례대로 밟는다.
///
/// 카르낙 거석군(파트 19)이 이런 대본이다.
/// <code>
///    1 대사   부관      제독! 저것을 보십시오!
///    2 미디어 동영상    AVI 44
///    3 음원   재생      75
///    4 대사   화자없음  카르낙 거석군을 발견했다!
///    5 외부분기 STORY0.CDS 109
///    6 대사   부관      드디어 찾아냈군요…
///   …
///    9 아이템 획득      고대의 소뿔
///   13 발견             카르낙 거석군
/// </code>
///
/// <b>파트 번호가 곧 발견물 번호다</b> — 274개가 발견물 표와 1:1 이다.
///
/// <b>아직 안 하는 것.</b> 외부 분기(<c>STORY0.CDS</c> · <c>STORY1.CDS</c>)는 그 파일을
/// 안 뜯어서 <b>건너뛴다</b> — 뛰지 않고 다음 줄로 간다. 그 밖에 뜻을 모르는 명령도
/// 건너뛴다. 대본이 끊기는 것보다 한 줄 빠지는 편이 낫다.
/// </remarks>
public sealed class DisevRunner
{
    /// <summary>
    /// 대본 책은 한 번만 읽는다. <b>적어 둔 것이 새로 써지면</b> 다시 읽는다.
    /// </summary>
    /// <remarks>
    /// 읽는 것은 <c>DISEV.CDS</c> 가 아니라 <c>발견이벤트.json</c> 이다
    /// (<see cref="DisevBook"/>). 그 파일이 없으면 책이 원본을 떠서 <b>먼저 적어 두고</b>
    /// 그것을 읽는다 — 이 집이 EXE 표를 다루는 결과 같다.
    ///
    /// 다시 읽는 자리를 둔 까닭은 편집기 때문이다. 여기서 한 번 읽고 붙들고 있으면 앱을
    /// 껐다 켜기 전에는 고친 대본이 안 돈다.
    /// </remarks>
    private static DisevBook? _shared;
    private static string _sharedFrom = "";
    private static DateTime _sharedWhen;

    /// <summary>그 게임 폴더의 대본 책. 없으면 null 이고, 그러면 대본 없이 지나간다.</summary>
    public static DisevBook? Open(string gameDirectory)
    {
        var when = Stamp();
        if (_shared != null && _sharedFrom == gameDirectory && _sharedWhen == when) return _shared;

        _shared = DisevBook.Open(gameDirectory);
        _sharedFrom = gameDirectory;
        _sharedWhen = Stamp();
        return _shared;
    }

    /// <summary>적어 둔 책에 쓴 시각. 아직 없으면 밑값이다.</summary>
    private static DateTime Stamp() =>
        File.Exists(DisevBook.Path_) ? File.GetLastWriteTimeUtc(DisevBook.Path_) : DateTime.MinValue;

    private readonly Window _owner;
    private readonly Game _game;

    /// <summary>
    /// 아직 안 낸 DSTILL 그림 자리. 다음 대사와 <b>함께</b> 낸다.
    /// </summary>
    /// <remarks>
    /// 대본은 그림과 글을 두 줄로 나눠 적지만 화면에는 한 창에 함께 나온다 —
    /// 히랄다탑(파트 51)이 「DSTILL 69」 다음에 「히랄다탑을 발견했다!」 다.
    /// 동영상은 다르다. 그쪽은 화면을 가득 덮었다가 사라지고 글이 따로 뜬다.
    /// </remarks>
    private int _pendingStill = -1;

    /// <summary>물고 있는 그림이 사건 스틸(EVSTILL)인지. 아니면 발견물 스틸(DSTILL)이다.</summary>
    private bool _pendingIsEvent;

    /// <summary>
    /// 마지막 미니게임을 이겼는지 — 게임 해석기의 <c>[ebp-0x1C]</c> 다.
    /// </summary>
    /// <remarks>
    /// <c>0E 04 [n]</c> 이 채우고(<c>0x00408D3A</c> 벌), 조건 <c>47</c> 이 읽는다(<c>0x0040B1C8</c>).
    /// 기제의 3대 피라미드(파트 26)가 성배 퍼즐 뒤에 이 값으로 갈라진다 — 이기면 앵크를 얻고,
    /// 지면 「왕릉을 침해한 죄를 죽음으로 대신해라!」 뒤 <c>4A</c>(게임 오버)다.
    /// </remarks>
    private bool _result;

    /// <summary>이번 대본에서 미니게임이나 육상전이 결과를 남겼는지. 「이동」(43 45)이 본다.</summary>
    private bool _hasResult;

    /// <summary>대본이 <c>26 1C 02</c> 로 빌려 준 아군 병력. 없으면 −1(함대 선원을 쓴다).</summary>
    private int _borrowedMen = -1;

    /// <summary>대본이 <c>26 1C 10</c> 으로 넣은 적 병력. 없으면 −1.</summary>
    private int _foeMen = -1;

    private readonly GameRandom _dice = new(Environment.TickCount);

    /// <summary>
    /// 마지막으로 돌린 대본이 <b>게임 오버</b>(<c>4A</c>)로 끝났는지. 부른 쪽이 보고 놀이를 끝낸다.
    /// </summary>
    /// <remarks>게임은 <c>0x0044AF40(0x5A4D18, 0)</c> 으로 놀이 상태를 끝으로 돌린다(<c>0x0040BDBA</c>).</remarks>
    public static bool LastEndedInGameOver { get; private set; }

    /// <summary>대본을 여기서 멈추라는 뜻으로 <see cref="Step"/> 이 내는 값.</summary>
    private const int Stop = int.MinValue;

    private DisevRunner(Window owner, Game game)
    {
        _owner = owner;
        _game = game;
    }

    /// <summary>
    /// 그 발견물의 대본을 돌린다. 대본이 없으면 false — 부른 쪽이 예전처럼 한 줄만 낸다.
    /// </summary>
    /// <param name="owner">창을 얹을 자리.</param>
    /// <param name="game">이 판.</param>
    /// <param name="discoveryId">발견물 번호 = DISEV 파트 번호.</param>
    public static bool Run(Window owner, Game game, int discoveryId)
    {
        LastEndedInGameOver = false;
        if (Open(game.Directory) is not { } book) return false;
        if (discoveryId < 0 || discoveryId >= book.Count) return false;

        var raw = book.Part(discoveryId);
        if (raw.Length == 0) return false;
        if (DisevPart.Parse(raw, out _) is not { } part) return false;

        var runner = new DisevRunner(owner, game);
        int body = runner.PickBody(part);
        if (body < 0) return false;

        runner.RunChunk(part, body);
        return true;
    }

    /// <summary>
    /// 조건이 맞는 첫 슬롯의 본문 자리. 없으면 -1.
    /// </summary>
    /// <remarks>
    /// 슬롯은 <c>[조건][본문]</c> 짝이 여럿이고, 앞에서부터 조건이 맞는 것을 쓴다.
    /// 조건 덩이가 비었거나(바로 <c>FF</c>) 뜻을 모르는 것뿐이면 <b>맞은 것으로 친다</b> —
    /// 카르낙 거석군의 「조건 없음 · 항상 발생」이 그 꼴이다.
    /// </remarks>
    private int PickBody(DisevPart part)
    {
        foreach (var slot in part.Slots)
        {
            var (from, to) = part.ChunkRange(slot.Condition);
            if (Passes(DisevScript.Parse(part.Data, from, to))) return slot.Body;
        }
        return part.Slots.Count > 0 ? part.Slots[0].Body : -1;
    }

    /// <summary>조건 덩이가 통과인지. 모르는 조건은 통과로 친다.</summary>
    private bool Passes(List<DisevScript.Op> ops)
    {
        foreach (var op in ops)
        {
            if (op.Kind == "덩이/갈래 끝") break;
            if (!Holds(op)) return false;
        }
        return true;
    }

    /// <summary>조건 한 줄이 참인지. 아는 것만 본다.</summary>
    private bool Holds(DisevScript.Op op)
    {
        var raw = DisevScript.ParseHex(op.Hex);
        if (raw == null) return true;

        long Field(int at, int width) =>
            DisevForm.Read(raw, new DisevForm.Field("", at, width));

        switch (op.Kind)
        {
            case "발견 완료 조건":
                return _game.Player.HasFound((int)Field(2, 2));
            case "미발견 조건":
                return !_game.Player.HasFound((int)Field(2, 2));
            case "아이템 소지 조건":
                return _game.Player.Items.Contains((int)Field(2, 2));
            case "아이템 비소지 조건":
                return !_game.Player.Items.Contains((int)Field(2, 2));
            case "연도 조건":
                return _game.Player.Date.Year >= Field(2, 2);
            case "연도 상한 조건":
                return _game.Player.Date.Year <= Field(2, 2);
            case "연도 범위 조건":
                return _game.Player.Date.Year >= Field(2, 2)
                    && _game.Player.Date.Year <= Field(5, 2);
            case "무작위 확률 조건":
            {
                long denominator = Field(2, 4);
                return denominator > 0 && _game.Random.Next((int)denominator) < Field(7, 4);
            }
            default:
                // 뜻을 모르는 조건은 막지 않는다 — 막으면 대본이 통째로 안 돈다.
                return true;
        }
    }

    /// <summary>본문 덩이를 차례대로 밟는다.</summary>
    private void RunChunk(DisevPart part, int start)
    {
        var (from, to) = part.ChunkRange(start);
        var ops = DisevScript.Parse(part.Data, from, to);

        // 자리로 줄을 찾을 수 있게 해 둔다 — 분기가 바이트 자리로 뛴다.
        var at = new Dictionary<int, int>();
        for (int i = 0; i < ops.Count; i++) at[ops[i].Offset] = i;

        // 대본이 꼬여 제자리를 맴돌 수 있다. 줄 수의 몇 곱으로 끊는다.
        int budget = Math.Max(64, ops.Count * 8);

        for (int i = 0; i < ops.Count && budget-- > 0; )
        {
            var op = ops[i];
            if (op.Kind == "덩이/갈래 끝") return;

            int jump = Step(op);
            if (jump == Stop) return;
            if (jump == 0) { i++; continue; }

            // 상대 이동은 <b>그 명령이 끝난 자리</b>에서 잰다.
            int target = op.Offset + op.Length + jump;
            if (!at.TryGetValue(target, out int next)) return;   // 덩이 밖이면 멈춘다
            i = next;
        }
    }

    /// <summary>
    /// 명령 한 줄을 치른다. 뛰어야 하면 상대 이동값을, 아니면 0 을 낸다.
    /// </summary>
    private int Step(DisevScript.Op op)
    {
        var raw = DisevScript.ParseHex(op.Hex);
        if (raw == null) return 0;

        long Field(int at, int width) =>
            DisevForm.Read(raw, new DisevForm.Field("", at, width));

        switch (op.Kind)
        {
            case "대사":
                Speak(raw);
                return 0;

            case "AVI 재생":
                // 파트 안의 두 꼴 — 00 02 [u16] 은 슬롯이 +2, 02 [u16] 은 +1 이다.
                MoviePlayer.Play(_owner, DiscoveryDialog.MovieOf(
                    _game.Directory, (int)Field(op.Length == 4 ? 2 : 1, 2)));
                return 0;

            case "음원 재생":
                PlaySound((int)Field(2, 2));
                return 0;

            // 00 0C [u16 n] — DISCOVER.CDS 파트 n 을 가운데에 틀고 돌아온다(0x00408429).
            case "CG 애니메이션 재생" when op.Length == 4:
                DiscoveryClipPlayer.Play(_owner, _game.Clips, (int)Field(2, 2));
                return 0;

            // 그림은 바로 안 낸다 — 다음 대사와 한 창에 함께 낸다.
            case "DSTILL 이미지 재생":
                _pendingStill = (int)Field(1, 2);
                _pendingIsEvent = false;
                return 0;
            case "EVSTILL 이미지 표시":
                // <b>딴 파일이다.</b> 예전에는 이 번호를 DSTILL 에 대고 찾아 엉뚱한 그림이
                // 나왔다 — EVSTILL.CDS 는 사건 스틸 열여섯 장으로 따로 있다.
                _pendingStill = (int)Field(2, 2);
                _pendingIsEvent = true;
                return 0;

            case "능력치 증가":
                Adjust((int)Field(2, 2), +(int)Field(5, 4));
                return 0;
            case "능력치 감소":
                Adjust((int)Field(2, 2), -(int)Field(5, 4));
                return 0;

            case "아이템 획득":
                Obtain((int)Field(2, 2));
                return 0;
            case "아이템 상실":
                _game.Player.Drop((int)Field(2, 2));
                return 0;

            case "발견물 등록/발견 처리":
            {
                // 해석기는 <c>01 0B</c> 만 보고 이 명령으로 친다 — 다른 자료 속에 우연히 든 두 바이트도
                // 걸린다. 스톤헨지 대본의 0x129 자리(「01 0B 0A 95」)가 그래서 발견물 38154 로 적혔다.
                // 게임 명령은 발견물 274칸 가운데 하나만 켜므로 표에 없는 번호는 버린다.
                // <b>발견과 그 물건은 이 명령만 준다</b> — 발견 판정 0x0048D3F0 은 대본을 돌린 뒤 결과
                // 코드만 보고(0·1 이면 인스턴스 +0x17 비트 1) 발견을 따로 적지 않는다. 존왕의 술잔은
                // 낚시에 지면 「놓쳤습니다」 뒤 4E 로 끝나 여기까지 안 온다. 물건 알림은 대본 대사가 한다.
                int id = (int)Field(2, 2);
                if (_game.Discoveries is { } log && log.Table.Find(id) != null) log.Discover(_game.Player, id);
                return 0;
            }

            case "금화 증가":
                _game.Player.Earn((int)Field(2, 4));
                return 0;
            case "금화 감소":
                _game.Player.Pay((int)Field(2, 4));
                return 0;

            // 43 45 — 조건이 「마지막 결과」 그대로라 <b>결과가 거짓일 때만</b> 뛴다(0x0040B1B4).
            // 러너는 예/아니오 물음을 아직 안 풀어 결과를 모르는 때가 많다. 그때는 예전처럼
            // 늘 뛰고, 미니게임·육상전이 결과를 남긴 뒤에만 게임대로 가른다. 파르테논(파트 23)의
            // +0x2C0 이 그 자리다 — 이긴 뒤 늘 뛰면 「우리가 이겼습니다!」와 방패를 건너뛴다.
            case "이동":
                return _hasResult && _result ? 0 : (int)(short)Field(2, 2);

            // 43 12 0E — 그 힌트를 얻었거나 보고까지 했으면 뛴다(0x0040902F).
            case "힌트 조건 분기":
            {
                int hint = (int)Field(3, 2);
                bool held = _game.Player.HasHint(hint)
                            || (_game.Discoveries?.IsHintDone(_game.Player, hint) ?? false);
                return held ? (int)(short)Field(5, 2) : 0;
            }

            // 26 1C [칸] — 육상전에 넘길 두 묶음만 받는다(0x0040A15C 의 뜀표 0x0040C314).
            //   칸 2  아군 임시 묶음의 병력(0x0040A1F9 → 0x0045FF40)
            //   칸 16 적 대장 묶음의 병력(0x0040A242 → 0x0045FF40)
            // 그 밖의 칸은 아직 안 옮겼다.
            case "능력치/기한 설정":
            {
                int slot = (int)Field(2, 2);
                int value = op.Length >= 13
                    ? (int)Field(9, 4) + _game.Random.Next((int)Math.Max(1, Field(5, 4)))
                    : (int)Field(5, 4);
                if (slot == 2) _borrowedMen = value;
                else if (slot == 16) _foeMen = value;
                return 0;
            }

            // 육상전 — 이겼는지를 남기고, 전멸했으면 그 자리에서 게임 오버로 멈춘다.
            case "육상전(인물)":
            case "육상전(도시)":
            {
                var battle = op.Kind == "육상전(인물)"
                    ? LeaderBattle((int)Field(2, 2))
                    : CityBattle((int)Field(2, 2));
                _result = LandBattleScene.Run(_owner, _game, battle, _dice);
                _hasResult = true;
                if (battle.Wiped)
                {
                    LastEndedInGameOver = true;
                    return Stop;
                }
                return 0;
            }

            // 조건이 맞으면 뛴다. 조건 부분은 Holds 와 같은 눈으로 본다.
            case "발견물 조건 분기":
                return _game.Player.HasFound((int)Field(3, 2)) ? (int)(short)Field(5, 2) : 0;
            case "아이템 조건 분기":
                return _game.Player.Items.Contains((int)Field(3, 2)) ? (int)(short)Field(5, 2) : 0;
            // 00 1E [n] — 특수 조우 연출(0x004085D2). 지도 위에서 사건 애니메이션 장면을 튼다.
            // 지도 창이 아닌 데서 돌면(개발 창 따위) 그릴 자리가 없어 건너뛴다. 8 넘으면 게임도 건너뛴다.
            case "특수 조우 연출":
            {
                int n = (int)Field(2, 2);
                if (n < DisevScript.Encounters.Length && _owner is UI.Views.ShipMapWindow map)
                    map.PlayEventScene(DisevScript.Encounters[n].Scene);
                return 0;
            }

            // 43 2C 1C 03 00 1A [금액] — 조건 2C 는 「소지금 < 금액」(0x0040A359)이고 43 은 조건이 <b>거짓일 때</b>
            // 뛴다(0x0040BCF9). 곧 금액 <b>이상</b> 있으면 뛴다. 예전에는 거꾸로 모자랄 때 뛰었다.
            case "소지금 비교 분기":
                return _game.Player.Gold >= Field(6, 4) ? (int)(short)Field(10, 2) : 0;

            // 미니게임 한 판 — 이겼는지를 들고 있다가 조건 47 이 읽는다.
            case "미니게임":
                _result = PlayMinigame((int)Field(2, 2));
                _hasResult = true;
                return 0;

            // 0E 14|1A [u32 판자] 04 [u16 n] — 코인 게임·발라몬의 탑(0x00408DF7). 가운데가 04 가 아니거나
            // 번호가 4·5 가 아니면 아무것도 안 하고 결과도 안 건드린다.
            case "퍼즐 미니게임" when Field(6, 1) == 0x04:
                switch ((DisevMinigame)Field(7, 2))
                {
                    case DisevMinigame.Coin:
                        _result = CoinPuzzleDialog.Play(_owner, _game.Random);
                        _hasResult = true;
                        break;
                    case DisevMinigame.Tower:
                        _result = TowerPuzzleDialog.Play(_owner, _game.Random, (int)Field(2, 4));
                        _hasResult = true;
                        break;
                }
                return 0;

            // 조건 47 은 「마지막 결과가 0 인가」라 <b>이겼으면 뛴다</b>(0x0040B1C8 → 0x0040BCF9).
            case "예/아니오 응답 분기":
                return _result ? (int)(short)Field(2, 2) : 0;

            // 게임 오버 — 대본을 멈추고 부른 쪽에 알린다.
            case "게임 오버":
                LastEndedInGameOver = true;
                return Stop;

            // 결과 코드를 적고 대본을 끝낸다(0x0040BDD5 벌). 스핑크스에게 쫓겨나면 여기서 멎는다.
            case "이벤트 결과 코드 0":
            case "이벤트 결과 코드 1":
            case "이벤트 결과 코드 2":
                return Stop;

            // 외부 분기는 그 파일을 안 뜯어서 안 뛴다 — 다음 줄로 그냥 간다.
            case "STORY0.CDS 외 분기":
            case "STORY1.CDS 외 분기":
            default:
                return 0;
        }
    }

    /// <summary>
    /// 미니게임 한 판(<c>0x00408D16</c> 의 뜀표). 이겼으면 true.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0  성배 퍼즐     0x004684D0   0 이 아니면 이김
    ///   1  스핑크스 퀴즈 0x0047BFE0   1 이면 이김
    ///   2  미궁 64       0x0042C8A0   (이 판에는 안 붙어 있다 — 이긴 것으로 친다)
    ///   3  낚시          0x0047BDD0
    ///   6  큐브 퍼즐     0x0049B3C0
    /// </code>
    /// 낚시·큐브는 창이 결과를 안 돌려줘 <b>이긴 것으로 친다</b> — 대본이 막히는 것보다 낫다.
    /// </remarks>
    private bool PlayMinigame(int game)
    {
        switch ((DisevMinigame)game)
        {
            case DisevMinigame.Grail:
                return GrailPuzzleDialog.Play(_owner, _game.Player, _game.Random, _game.Sfx);
            case DisevMinigame.Sphinx:
                return SphinxQuizDialog.Play(_owner, _game.Random);
            // 미궁은 딴 어셈블리(CdsHelper.Maze)라 띄우는 쪽이 걸어 둔 자리로 부른다.
            // 게임은 돌파 보상을 치른 갈래에서만 결과 1 을 박는다(0x0042B154) — 덫·실패·포기는 0.
            // 걸려 있지 않으면 대본이 막히지 않게 이긴 것으로 친다.
            case DisevMinigame.Maze:
                return UI.Views.ShipMapWindow.MazeGame?.Invoke(_owner, _game.Random) ?? true;
            // 낚시는 대어일 때만 이긴 것이다(0x0047AD6C).
            case DisevMinigame.Fishing:
                return FishingGameDialog.Play(_owner, _game.Random);
            case DisevMinigame.Cube:
                CubePuzzleDialog.Play(_owner, _game.Player, _game.Random);
                return true;
            // 4·5 와 7 넘는 번호는 뜀표가 곧장 다음 명령으로 간다 — 결과를 안 건드린다(0x0040C1B0).
            default:
                return _result;
        }
    }

    /// <summary>
    /// <c>2F 0D [인물]</c> 의 판 — 그 인물이 적 대장이다(<c>0x0040A40B</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0040a41d  아군 = 26 1C 02 가 세운 임시 묶음, 없으면 함대(0x005AA2B8)
    ///   0040a423  적   = 인물 핸들 0x1000|번호(0x00477AF0), 병력은 26 1C 10 이 넣은 것
    ///   0040a442  지형 = 배 자리의 부류(0x00426740) → 7 도시 · 2 초지 · 4 황무지 · 그 밖 숲
    ///   0040a459  0x0044AA30(3, 아군, 적, 0, 지형)
    ///   0040a47e  [ebp-0x1C] = (돌려준 값 == 0)   ; 0 이김 · 1 물러남 · 2 전멸(게임 오버)
    /// </code>
    /// 파르테논 신전(파트 23)은 26 1C 02 = 70, 26 1C 10 = 100~139, 적 대장 206 이다.
    /// <b>지형</b>은 우리가 배 자리 부류를 안 들고 있어 도시 안이면 도시, 아니면 숲으로 둔다.
    /// </remarks>
    private LandBattle LeaderBattle(int person)
    {
        var player = _game.Player;
        var aide = AideInfo();
        int myMen = (_borrowedMen >= 0 ? _borrowedMen : player.Crew) + 1;

        // 적 총원 = 묶음 +0x0C + 1(0x004A050D). 대본이 안 넣었으면 백으로 친다.
        int foeMen = (_foeMen >= 0 ? _foeMen : 100) + 1;

        // 적 대장 능력 — 인물 표의 능력 여섯(0 체력 · 1 지력 · 2 무력 · 4 운).
        var foe = (Might: 75, Mind: 70, Luck: 65, Body: 85);
        (int Sword, int Gunnery, int Shooting)? foeSkills = null;
        try
        {
            if (PersonTable.Open().Find(person) is { } row && row.Stats.Length >= 5)
            {
                foe = (row.Stats[2], row.Stats[1], row.Stats[4], row.Stats[0]);
                // 편성(병종)은 적 대장의 기능 자리로 갈린다 — 인물 표에 적힌 그대로 쓴다.
                if (row.Skills.Length > Support.Local.Models.Skill.Shooting)
                    foeSkills = (row.Skills[Support.Local.Models.Skill.Sword],
                                 row.Skills[Support.Local.Models.Skill.Gunnery],
                                 row.Skills[Support.Local.Models.Skill.Shooting]);
            }
        }
        catch (Exception)
        {
            // 인물 표를 못 읽으면 도시 규모 3~4 의 밑값으로 싸운다.
        }

        // 문화권은 적 대장 나라의 수도 것이다(0x00447070).
        int culture = 0;
        if (_game.PersonTemplates?.Find(person) is { } who
            && _game.Nations?.Find(who.Nation) is { } nation)
            culture = _game.CityRows?.CultureOf(nation.Capital) ?? 0;

        int terrain = player.CityId >= 0 ? 0 : 2;
        // 적 기능은 생성자로 넘긴다 — 편성이 생성자 안에서 지어진다.
        return new LandBattle(Deploy(aide), myMen, foeMen, player, aide, culture, terrain, foe, _dice,
                              foeSkills)
        {
            KeepsCrew = _borrowedMen >= 0,
        };
    }

    /// <summary>
    /// <c>2F 08 [도시]</c> 의 판 — 그 도시를 친다(<c>0x0040A3B2</c> → <c>0x0044AA30(4, 아군, 0, 도시, 7)</c>).
    /// </summary>
    /// <remarks>
    /// 적은 도시 규모로 짓는다(<c>0x00449E50</c> 은 갈래 2·4 가 함께 쓴다). 지형 인자 7 은
    /// 싸움터 0(도시)이다(<c>0x0044A624</c>). 우리 판에 갈래 4 가 따로 없어 마을 공략(2)으로 세운다.
    /// </remarks>
    private LandBattle CityBattle(int city)
    {
        var player = _game.Player;
        var aide = AideInfo();
        int myMen = _borrowedMen >= 0 ? _borrowedMen + 1 : 0;
        var rows = _game.CityRows;
        return new LandBattle(Deploy(aide), player, aide, rows?.ScaleOf(city) ?? 0,
                              rows?.NationOf(city) ?? -1, rows?.CultureOf(city) ?? 0,
                              0, _dice, myMen)
        {
            KeepsCrew = _borrowedMen >= 0,
        };
    }

    /// <summary>부관 신상. 없으면 null.</summary>
    private Support.Local.Models.Player.MateInfo? AideInfo()
    {
        var player = _game.Player;
        return player.Mates.Count > 0 && player.Mates[0].Length > 0
            ? player.MateInfoOf(player.Mates[0]) : null;
    }

    /// <summary>
    /// 부대배치 화면 — 게임도 판을 열기 전에 편다(<c>0x0044A963</c> → <c>0x00446E60</c>).
    /// </summary>
    /// <remarks>게임 화면에는 물리는 길이 없다. 창을 닫으면 제독 부대 하나만 세운다.</remarks>
    private int[] Deploy(Support.Local.Models.Player.MateInfo? aide)
    {
        int city = _game.Player.CityId;
        string where = city >= 0 ? _game.CityName(city) : "";
        // 대본이 병력을 빌려 줬으면(파르테논 70명) 배치 판도 그 수로 짓는다 — 함대 선원이 아니다.
        if (LandDeployDialog.Show(_owner, _game, where, _borrowedMen) is { } line) return line;

        var alone = new int[LandRoster.SlotCount];
        Array.Fill(alone, -1);
        alone[0] = LandRoster.For(_game.Player, aide).KindAt(LandRoster.Leader);
        return alone;
    }

    /// <summary>
    /// 아이템을 하나 얻고 <b>그 물건의 정보 창</b>을 낸다.
    /// </summary>
    /// <remarks>
    /// 게임은 손에 넣은 자리에서 그림·갈래·설명이 든 창을 띄운다 — 소지품 창에서 물건을
    /// 눌렀을 때 뜨는 것과 같은 창이다(<see cref="ItemInfoDialog"/>).
    /// 소지품이 꽉 차서 못 들면 창도 안 낸다.
    /// </remarks>
    private void Obtain(int itemId)
    {
        if (!_game.Player.Take(itemId)) return;
        if (_game.Items?.Find(itemId) is not { } item) return;

        ItemInfoDialog.Show(_owner, item, _game.ItemText?.Of(itemId) ?? "", _game.ItemPictures);
    }

    /// <summary>
    /// 능력치 한 칸을 그만큼 움직인다. 아는 칸만 움직이고 나머지는 지나간다.
    /// </summary>
    /// <remarks>번호는 <see cref="DisevScript.StatNames"/> 표 그대로다.</remarks>
    private void Adjust(int stat, int by)
    {
        switch (stat)
        {
            case 0: _game.Player.Tire(by); break;      // 피로도
            case 1: _game.Player.Cheer(by); break;     // 규율(사기)
            case 3:                                    // 소지금
                if (by >= 0) _game.Player.Earn(by); else _game.Player.Pay(-by);
                break;
            case 17: _game.Player.Fame = Math.Max(0, _game.Player.Fame + by); break;   // 명성
        }
    }

    /// <summary>대사 한 줄을 낸다. 화자에 따라 얼굴이 갈린다.</summary>
    /// <summary>
    /// 대본이 적어 둔 <b>사운드 ID</b> 하나를 낸다.
    /// </summary>
    /// <remarks>
    /// <b>ID 는 파트 번호가 아니다.</b> 표(<c>0x004C3810</c>)가 두 갈래로 나뉜다 —
    /// <c>0~27</c> 은 CD 트랙이고 <c>28~77</c> 은 WAVES.CDS 의 파트다. 파트 번호는
    /// ID 에서 28 을 뺀 값이다(<see cref="WaveBank.FirstSoundId"/>).
    ///
    /// 예전에는 ID 를 <see cref="SoundBank.Play"/> 에 그대로 넘겼다. 그러면 알함브라 궁전의
    /// <c>0E 03 4B 00</c>(ID 75)이 파트 75 를 찾다가 표 밖이라 조용히 넘어갔다 —
    /// 실제로 나야 할 것은 파트 <c>47</c> 이다.
    /// </remarks>
    private void PlaySound(int soundId)
    {
        int track = WaveBank.CdTrackFromSoundId(soundId);
        if (track >= 0) { _game.Bgm.Play(track); return; }

        int part = WaveBank.PartFromSoundId(soundId);
        if (part >= 0) _game.Sfx?.Play(part);
    }

    private void Speak(byte[] raw)
    {
        // 창 플래그 한 바이트가 앞에 붙을 수 있다. 0A 부터가 알맹이다.
        int textStart = raw.Length > 0 && raw[0] == 0x0A ? 1 : 2;
        if (textStart >= raw.Length) return;

        int end = Array.IndexOf(raw, (byte)0, textStart);
        if (end < 0) end = raw.Length;

        var (speaker, body) = DisevScript.DecodeDialogue(raw.AsSpan(textStart, end - textStart));
        if (body.Length == 0) return;

        // <b>부관이 없으면 부관 대사는 통째로 건너뛴다.</b> 말할 사람이 없는데 말이 나오면
        // 안 된다 — 몽생미셸(파트 65)의 +0x0032 가 그 줄이다. 성문에서도 게임이 같은
        // 잣대를 쓴다(0x00468EF0 이 부하 첫 자리를 본다).
        if (speaker == Aide && _game.Player.MateAt(0).Length == 0) return;

        // 앞줄이 그림을 걸어 두었으면 그림과 글을 한 창에 낸다.
        if (_pendingStill >= 0)
        {
            int still = _pendingStill;
            _pendingStill = -1;
            DiscoveryDialog.Show(_owner, _pendingIsEvent ? _game.EventStills : _game.Stills,
                                 still, body);
            return;
        }

        TalkDialog.Say(_owner, FaceOf(speaker), "", body);
    }

    /// <summary>
    /// 그 화자의 얼굴. 모르는 화자면 null 이고, 그러면 얼굴 없이 글만 나온다.
    /// </summary>
    /// <remarks>
    /// 「검사관」은 CP932 로 <c>監察官</c> 이라 <b>감찰관</b>이다 — 계약할 때 딸려 온 그
    /// 사람이고 얼굴이 늘 232 다(<see cref="Town.Inspector"/>).
    /// 「부관」은 부하 첫 자리라 그 사람 제 얼굴을 쓴다.
    /// </remarks>
    private uint[]? FaceOf(string? speaker) => speaker switch
    {
        null or "" => null,
        Aide => MateFace(),
        "검사관" or "감찰관" => _game.Faces?.TryGetBgra(Town.Inspector.Face, female: false),
        _ => FacilityFace(speaker),
    };

    /// <summary>부관 화자 이름. 대본에는 CP932 로 <c>副官</c> 이라 적혀 있다.</summary>
    private const string Aide = "부관";

    /// <summary>
    /// 시설 화자의 건물 코드. 화자표(<c>0x0056823C[건물][문화권]</c>)를 그대로 탄다.
    /// </summary>
    /// <remarks>
    /// 몽생미셸(파트 65)의 <c>+0x00A9</c> 가 화자 <b>교회</b> 라 신부 얼굴이 붙는다 —
    /// 예전에는 모르는 화자로 흘려 얼굴 없이 냈다. 건물 코드는 볼트
    /// <c>15.분석-건물 화면 엔진</c> 의 그 차례다.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, int> FacilitySpeakers =
        new Dictionary<string, int>
        {
            ["교역소"] = 1, ["왕궁"] = 2, ["교회"] = 3, ["술집"] = 4,
            ["여관"] = 5, ["조선소"] = 6, ["조합"] = 9, ["성문"] = 10,
        };

    /// <summary>시설 화자의 얼굴. 그 시설 화자가 아니면 null 이다.</summary>
    /// <remarks>
    /// 문화권은 <b>지금 있는 도시</b>의 것이다 — 바다 위라 도시를 모르면 유럽(0)으로 둔다.
    /// </remarks>
    private uint[]? FacilityFace(string speaker)
    {
        if (!FacilitySpeakers.TryGetValue(speaker, out int code)) return null;

        int city = _game.Player.CityId;
        int culture = city >= 0 ? _game.CityRows?.CultureOf(city) ?? 0 : 0;
        return _game.SpeakerFace(code, culture);
    }

    /// <summary>부하 첫 자리의 얼굴. 판이 들고 있는 것을 그대로 쓴다.</summary>
    private uint[]? MateFace() => _game.AideFace;
}
