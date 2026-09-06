using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 싸움터 — 배치가 끝나면 이 화면으로 넘어간다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0044A870</c> 이 펴는 판이다. 자리를 손으로 짐작하지 않았다 —
/// <c>0x00445258</c>~<c>0x004453B9</c> 가 열두 자리를 못 박는 그 값 그대로다.
/// <code>
///   아군  (112,208) (176,256) (240,304) / (48,256) (112,304) (176,352)
///   적    (432,160) (368,112) (304, 64) / (496,112) (432, 64) (368, 16)
/// </code>
/// 싸움터 그림은 LANDDATA 파트 1~4(640x480), 부대는 파트 8~50 에서 온다.
/// 병사수는 부대배치 화면과 같은 24x24 숫자 조각이다.
///
/// 턴을 도는 것은 <see cref="LandFight"/> 가 맡고 이 창은 <b>보여 주기</b>만 한다 —
/// 한 턴이 남긴 줄을 하나씩 세우고 그때마다 판을 다시 그린다. 「애니메이션」을 끄면
/// 줄을 안 세우고 몰아서 끝낸다.
/// </remarks>
internal sealed class LandBattleScene : GameWindow
{
    /// <summary>
    /// 부대 열둘이 서는 자리(<c>0x00445258</c>~<c>0x004453B9</c>).
    /// 앞 여섯이 아군, 뒤 여섯이 적이다.
    /// </summary>
    private static readonly (int X, int Y)[] StandAt =
    [
        (112, 208), (176, 256), (240, 304),
        (48, 256), (112, 304), (176, 352),
        (432, 160), (368, 112), (304, 64),
        (496, 112), (432, 64), (368, 16),
    ];

    /// <summary>숫자 한 자의 한 변과, 넉 자리 폭에 가운데로 모는 셈(<c>0x0049FD14</c>).</summary>
    private const int Digit = LandArt.DigitSide, DigitSlots = 4;

    private readonly LandArt? _art;
    private readonly LandBattle _battle;
    private readonly Canvas _board = new()
    {
        Width = LandArt.FieldWidth,
        Height = LandArt.FieldHeight,
    };

    private LandBattleScene(Engine.Game game, LandBattle battle, double scale)
    {
        _battle = battle;
        _art = game.Directory.Length > 0 ? LandArt.Open(game.Directory) : null;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        Width = LandArt.FieldWidth * scale;
        Height = LandArt.FieldHeight * scale;

        if (Field() is { } field) _board.Children.Add(At(field, 0, 0));
        else _board.Background = new SolidColorBrush(Color.FromRgb(0x35, 0x3B, 0x30));

        Stand();

        _board.RenderTransform = new ScaleTransform(scale, scale);
        Content = new Canvas
        {
            Width = LandArt.FieldWidth * scale,
            Height = LandArt.FieldHeight * scale,
            Children = { _board },
        };
    }

    /// <summary>
    /// 판을 펴고 싸운다. <b>이겨서 도시에 들어가게 되면 참</b>이다.
    /// </summary>
    public static bool Run(Window? owner, Engine.Game game, LandBattle battle, GameRandom dice)
    {
        // 배치 판과 같은 셈이다 — 화면 점에 딱 떨어져야 점이 안 뭉갠다.
        int zoom = GameUi.PixelFit(owner, LandArt.FieldWidth, LandArt.FieldHeight);
        double scale = GameUi.PixelZoom(owner, zoom);

        var scene = new LandBattleScene(game, battle, scale) { _game = game, _dice = dice };
        if (owner != null) scene.Owner = owner;

        // 싸움 곡으로 갈아 끼웠다가 끝나면 듣던 것으로 되돌린다.
        int was = game.Bgm.Track;

        scene.Show();
        try { return scene.Fight(); }
        finally { scene.Close(); game.Bgm.Play(was); }
    }

    private Engine.Game? _game;
    private GameRandom? _dice;

    /// <summary>
    /// 턴을 돈다 — 「제 N턴」을 알리고 공격명령을 물어 한 턴씩 굴린다(<c>0x00449C80</c>).
    /// </summary>
    /// <returns>이겨서 도시에 들어가게 되면 참.</returns>
    private bool Fight()
    {
        var dice = _dice ?? new GameRandom(Environment.TickCount);
        var fight = new LandFight(_battle, dice);

        // 싸우는 동안은 그 곡이 돈다. 끝나면 부르는 쪽이 제 곡으로 되돌린다.
        _game?.Bgm.Play(BgmPlayer.BattleTrack);

        // 판이 열릴 때 한 번 — 아이템을 지녔으면 작렬탄을 받는다(0x00448DD0).
        if (_battle.ShellWord.Length > 0) NoticeDialog.Show(this, _battle.ShellWord, "");

        while (true)
        {
            NoticeDialog.Show(this, _battle.TurnWord, "");

            // 일기토는 <b>차림표를 열 때마다 굴린다</b> — 적 대장이 나보다 셀수록 열린다
            // (0x00447930). 예전에는 첫 턴이면 늘 열어 두었다.
            int order = ChoiceDialog.Pick(this, $" {LandBattle.OrderTitle} ",
                                          _battle.OrderRows(canDuel: _battle.DuelOffered(dice),
                                                            canRuse: _battle.AnyRuseLeft));
            if (order < 0) continue;                 // 물러도 차림표가 다시 뜬다

            if (order == LandBattle.Retreat)
            {
                if (!ConfirmDialog.Ask(this, "퇴각해도 좋습니까?")) continue;
                Settle(won: false, retreated: true, dice);
                return false;
            }

            // 「애니메이션」은 켜고 끄는 것이라 턴이 안 간다(0x00449190).
            if (order == LandBattle.Animate) { _quick = !_quick; continue; }

            // 「일기토」는 판을 한 판에 가른다 — 이기면 그대로 이긴다(0x004478A0).
            if (order == LandBattle.Duel)
            {
                if (!Asked()) continue;
                bool beat = Fought(dice);
                fight.End(beat);
                Settle(beat, retreated: false, dice);
                return beat;
            }

            // 「묘책」은 턴을 안 쓴다 — 걸어 두고 명령을 다시 고른다(0x004490D0).
            if (order == LandBattle.Ruse)
            {
                Wile(fight, dice);
                continue;
            }

            Play(fight.Turn(order, _battle.FoeOrder(dice)));

            if (fight.Over is { } won)
            {
                // 마을 공략에서는 적의 새 병력이 딱 한 번 붙는다(0x00449930).
                if (won && _battle.Reinforce(dice))
                {
                    NoticeDialog.Show(this, LandBattle.ReinforceWord, "");
                    fight = new LandFight(_battle, dice);
                    Redraw();
                    continue;
                }
                Settle(won, retreated: false, dice);
                return won;
            }
            if (!_battle.NextTurn())
            {
                // 열 턴이 다하면 물러난 것으로 친다(0x00449420).
                NoticeDialog.Show(this, "날이 저물었다. 이번에는 물러선다.", "");
                Settle(won: false, retreated: true, dice);
                return false;
            }
        }
    }

    /// <summary>애니메이션을 끄면 한 줄씩 안 세우고 몰아서 낸다.</summary>
    private bool _quick;

    /// <summary>
    /// 「묘책」 — 기습·함정·암살자 가운데 하나를 건다(<c>0x004490D0</c>).
    /// </summary>
    /// <remarks>
    /// 한 판에 하나씩만 쓴다. 어그러지면 제 발등을 찍으므로 문화권마다의 성공률
    /// (<c>0x00549B80</c>)이 그대로 값이 된다.
    /// </remarks>
    private void Wile(LandFight fight, GameRandom dice)
    {
        int pick = ChoiceDialog.Pick(this, $" {LandBattle.RuseTitle} ", _battle.RuseRows());
        if (pick < 0 || pick == LandBattle.Judgement) return;

        var said = fight.Ruse(pick, dice, out bool asked);
        foreach (var line in said)
        {
            if (line.Text.Length == 0) continue;
            // 기습이 먹히면 그대로 물음이 된다("선제 공격을 가하겠습니까?").
            if (asked) ConfirmDialog.Ask(this, line.Text);
            else NoticeDialog.Show(this, line.Text, "");
        }
        Redraw();
    }

    /// <summary>
    /// 「일기토」 — 제독이 적 대장과 맞선다(<c>0x004478A0</c> → <c>0x004AA700</c>).
    /// </summary>
    /// <remarks>
    /// 게임도 여느 일대일 결투 판을 그대로 불러 쓴다. 이기면 싸움이 그 자리에서
    /// 끝나고, 지면 그대로 진다. 마을 공략에서는 <b>적이 먼저 거는 일은 없다</b>
    /// (<c>0x004479D7</c> 이 갈래 2·4 를 걸러 낸다).
    /// </remarks>
    /// <summary>일기토를 걸겠냐고 묻는다 — 게임도 「상대해 주마!」로 먼저 이른다.</summary>
    private bool Asked() => ConfirmDialog.Ask(this, "상대해 주마!");

    private bool Fought(GameRandom dice)
    {
        if (_game is not { } game) return false;

        var me = game.Player;
        var mine = new Duel.Fighter(me.Name.Length > 0 ? me.Name : "제독",
                                    me.AbilityOf(Ability.Body), me.AbilityOf(Ability.Might),
                                    me.LevelOf(Skill.Names[Skill.Sword]),
                                    me.AbilityOf(Ability.Luck), 0, 0);
        var foe = new Duel.Fighter("적장", _battle.FoeBody, _battle.FoeMight,
                                   _battle.SkillAt(LandBattle.FirstFoe, Skill.Sword),
                                   _battle.FoeLuck, 0, 0);

        var duel = new Duel(mine, foe, shield: false, dice.Next());
        return DuelDialog.Show(this, duel, dice, null);
    }

    /// <summary>한 턴에 일어난 일을 보여 주고 판을 다시 그린다.</summary>
    private void Play(IReadOnlyList<LandFight.Line> lines)
    {
        var sfx = _game?.Sfx;
        foreach (var line in lines)
        {
            if (line.Sound >= 0) sfx?.Play(line.Sound);
            if (!_quick) Swing(line);

            // 깎인 병사수는 <b>맞은 부대 위에 흰 숫자</b>로 잠깐 떴다 사라진다 —
            // 물음창으로 내지 않는다. 예전에는 "…의 공격 — … 50명" 을 창으로 냈다.
            if (line.Damage > 0)
            {
                if (!_quick) Flash(line.Target, line.Damage);
                continue;
            }

            if (line.Text.Length == 0 || _quick) continue;
            Redraw();
            NoticeDialog.Show(this, line.Text, "");
        }
        Redraw();
    }

    /// <summary>
    /// 맞은 부대 위에 <b>깎인 병사수</b>를 잠깐 띄웠다 지운다.
    /// </summary>
    /// <remarks>
    /// 게임 화면에서 보이는 그 큰 흰 숫자다 — 부대배치 판이 쓰는 것과 같은 조각
    /// (LANDDATA 파트 52, 24x24 열 자)이다.
    /// </remarks>
    private void Flash(int slot, int damage)
    {
        if (slot < 0 || slot >= StandAt.Length) return;

        var (x, y) = StandAt[slot];
        string men = damage.ToString();
        int left = x + (LandArt.DeployWidth - men.Length * Digit) / 2;
        int top = y + (LandArt.DeployWidth - Digit) / 2;

        var shown = new List<UIElement>();
        foreach (char c in men)
        {
            if (Number(c - '0') is { } glyph)
            {
                Panel.SetZIndex(glyph, FlashDepth);
                _board.Children.Add(At(glyph, left, top));
                shown.Add(glyph);
            }
            left += Digit;
        }

        Rest(FlashMs);
        foreach (var glyph in shown) _board.Children.Remove(glyph);
    }

    /// <summary>피해 숫자가 머무는 밀리초와 그것을 얹는 높이.</summary>
    private const int FlashMs = 420, FlashDepth = DigitDepth + 10;

    // ── 치는 몸짓 ──────────────────────────────────────────────────────────────

    /// <summary>지금 치고 있는 자리와 그 몸짓. −1 이면 아무도 안 친다.</summary>
    private int _acting = -1, _actFrame;

    /// <summary>친 부대가 제자리에서 나가 있는 만큼.</summary>
    private int _actDx, _actDy;

    /// <summary>몸짓 한 장이 머무는 밀리초.</summary>
    private const int SwingMs = 70;

    /// <summary>
    /// 맞붙는 부대가 <b>목표 앞에서 멈추는</b> 거리.
    /// </summary>
    /// <remarks>
    /// 게임은 반쯤 다가가다 마는 것이 아니라 <b>칠 부대 바로 앞까지 걸어가서</b> 친다.
    /// 자리 사이가 가로로 64 점이니 그만큼 앞에 서면 딱 맞붙은 꼴이 된다. 목표가 이미
    /// 이보다 가까우면 제자리에서 친다.
    /// </remarks>
    private const double StandOff = 64;

    /// <summary>
    /// 한 줄을 몸짓으로 보인다 — <b>여덟 장을 차례로</b> 돌린다.
    /// </summary>
    /// <remarks>
    /// 부대 조각 한 벌이 96x48 여덟 장(2열 4행)인데 우리는 첫 장만 쓰고 있었다. 여덟 장이
    /// 곧 치는 몸짓이라 차례로 갈아 끼우면 된다.
    ///
    /// <b>맞붙는 병종</b>(기병·제독·장군처럼 손에 무기를 든 것 —
    /// <see cref="LandUnits.Kind.Melee"/>)은 몸짓만 짓지 않고 <b>상대 쪽으로 나갔다
    /// 돌아온다</b>. 총·포는 제자리에서 쏜다.
    /// </remarks>
    private void Swing(LandFight.Line line)
    {
        int slot = line.Actor;
        if (slot < 0 || slot >= StandAt.Length) return;
        if (!_battle.Units[slot].Standing) return;

        // 맞붙는 병종이면 <b>칠 부대 바로 앞까지</b> 나갔다 온다.
        int dx = 0, dy = 0;
        if (LandUnits.KindOf(_battle.Units[slot].Kind) == LandUnits.Kind.Melee
            && line.Target >= 0 && line.Target < StandAt.Length)
        {
            var (fx, fy) = StandAt[slot];
            var (tx, ty) = StandAt[line.Target];
            double span = Math.Sqrt((double)(tx - fx) * (tx - fx) + (double)(ty - fy) * (ty - fy));
            double gone = span > StandOff ? (span - StandOff) / span : 0;
            dx = (int)((tx - fx) * gone);
            dy = (int)((ty - fy) * gone);
        }

        _acting = slot;
        try
        {
            int frames = LandArt.FrameCols * LandArt.FrameRows;
            for (int f = 0; f < frames; f++)
            {
                _actFrame = f;

                // 앞 절반에 나가고 뒤 절반에 돌아온다.
                double gone = f < frames / 2 ? (f + 1) / (frames / 2.0)
                                             : (frames - 1 - f) / (frames / 2.0);
                _actDx = (int)(dx * gone);
                _actDy = (int)(dy * gone);

                Redraw();
                Rest(SwingMs);
            }
        }
        finally
        {
            _acting = -1;
            _actFrame = 0;
            _actDx = _actDy = 0;
        }
    }

    /// <summary>
    /// 그만큼 쉬면서 화면은 그리게 둔다.
    /// </summary>
    /// <remarks>
    /// 싸움 한 턴이 <c>Fight</c> 안에서 곧게 돌아가므로(물음창도 그 안에서 뜬다) 여기서
    /// 실을 재우면 화면이 멎는다. 물음창이 하는 것과 같이 <b>속 고리를 하나 돌린다</b>.
    /// </remarks>
    private void Rest(int ms)
    {
        var frame = new DispatcherFrame();
        var clock = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Render,
                                        (_, _) => frame.Continue = false, Dispatcher);
        clock.Start();
        Dispatcher.PushFrame(frame);
        clock.Stop();
    }

    /// <summary>부대와 병사수를 다시 그린다.</summary>
    private void Redraw()
    {
        for (int i = _board.Children.Count - 1; i >= 0; i--)
            if (_board.Children[i] is FrameworkElement { Tag: UnitLayer })
                _board.Children.RemoveAt(i);
        Stand();
    }

    /// <summary>치러 나간 부대를 얹는 높이. 숫자보다는 아래다.</summary>
    private const int ActDepth = DigitDepth - 1;

    /// <summary>부대와 숫자에 붙이는 표 — 다시 그릴 때 이것만 걷는다.</summary>
    private const string UnitLayer = "unit";

    /// <summary>
    /// 싸움이 끝나고 값을 치른다(<c>0x00449870</c>).
    /// </summary>
    /// <remarks>
    /// 부상병은 이기든 물러나든 돌아온다. 전리품과 명성·악명은 이겼을 때만이다.
    /// 몰살했을 때 살려 주는 문(<c>0x0056D6C8</c> "적이 봐 준 것 같습니다")은
    /// 마을 공략에서는 안 열리므로, 아군이 다 쓰러지면 그대로 진 것이다.
    /// </remarks>
    private void Settle(bool won, bool retreated, GameRandom dice)
    {
        if (_game is not { } game) return;

        // 모의전은 <b>값을 안 치른다</b> — 선원도 돈도 명성도 그대로 둔다.
        // 미니 게임에서 셈만 돌려 보는 자리이기 때문이다.
        if (_battle.IsMock)
        {
            NoticeDialog.Show(this, won ? "모의전에서 이겼다" : retreated ? "모의전을 물렸다"
                                                                          : "모의전에서 졌다", "");
            return;
        }

        var spoils = _battle.Finish(won, dice);
        var player = game.Player;

        int left = Math.Max(0, _battle.MenOn(foe: false) - 1) + spoils.Back;
        player.SetCrew(left);
        if (spoils.Back > 0)
            NoticeDialog.Show(this, $"{spoils.Back}명의 부상병이 복귀했다", "");

        if (!won)
        {
            if (!retreated) NoticeDialog.Show(this, "부대는 모두 쓰러졌다…", "");
            return;
        }

        player.SetGold(player.Gold + spoils.Loot);
        NoticeDialog.Show(this, $"전리품으로서 금화 {spoils.Loot}닢을 손에 넣었다", "");
        player.Fame += spoils.Fame;
        player.Infamy += spoils.Infamy;
        if (spoils.Might > 0) NoticeDialog.Show(this, "싸움에서 무력이 올랐다", "");
    }

    // ── 그리기 ─────────────────────────────────────────────────────────────────

    /// <summary>싸움터 한 장. 못 읽으면 null.</summary>
    private Image? Field()
    {
        if (_art?.TryGetField(_battle.Terrain) is not { } bgra) return null;
        return Picture(bgra, LandArt.FieldWidth, LandArt.FieldHeight);
    }

    /// <summary>부대 열둘을 세운다.</summary>
    private void Stand()
    {
        for (int i = 0; i < LandBattle.Slots && i < StandAt.Length; i++)
        {
            var unit = _battle.Units[i];
            if (!unit.Standing) continue;

            var (x, y) = StandAt[i];

            // 치고 있는 부대는 몸짓을 갈아 끼우고, 맞붙는 병종이면 앞으로 나가 있다.
            int frame = 0;
            if (i == _acting)
            {
                frame = _actFrame;
                (x, y) = (x + _actDx, y + _actDy);
            }

            if (Sprite(unit.Kind, friend: i < LandBattle.FirstFoe, frame) is { } art)
            {
                // 나가서 치는 부대는 남의 자리에 서므로 <b>맨 앞으로</b> 올린다 —
                // 안 그러면 치러 간 부대가 맞는 부대 뒤에 깔린다.
                if (i == _acting) Panel.SetZIndex(art, ActDepth);
                _board.Children.Add(Mark(At(art, x, y)));
            }

            // 병사수는 칸 위쪽에 넉 자리 폭으로 가운데를 맞춰 찍는다.
            //
            // <b>숫자는 늘 맨 앞이다.</b> 부대를 차례대로 놓으므로 뒤에 놓인 부대 그림이
            // 앞서 찍은 숫자를 덮는다 — 그림이 칸을 꽉 채우게 되면서 도드라졌다.
            string men = unit.Men.ToString();
            int left = x + (DigitSlots - men.Length) * Digit / 2;
            foreach (char c in men)
            {
                if (Number(c - '0') is { } glyph)
                {
                    Panel.SetZIndex(glyph, DigitDepth);
                    _board.Children.Add(Mark(At(glyph, left, y)));
                }
                left += Digit;
            }
        }
    }

    /// <summary>그 병종의 몸짓 한 장. 못 구하면 null.</summary>
    private Image? Sprite(int kind, bool friend, int frame = 0)
    {
        if (_art == null) return null;

        var bgra = _art.TryGetUnit(kind, friend, _battle.Culture, frame, out int w, out int h);
        return bgra == null ? null : Picture(bgra, w, h, w, h * UnitZoomY);
    }

    /// <summary>숫자 한 자. 조각을 못 구하면 게임 글꼴로 물러선다.</summary>
    private FrameworkElement? Number(int digit)
    {
        if (_art?.TryGetDigit(digit) is { } bgra) return Picture(bgra, Digit, Digit);
        return GameUi.GameFontLabel(digit.ToString(), GameFont.ButtonColor, 1,
                                    GameUi.ItemTextHeight);
    }

    /// <summary>
    /// 부대 그림을 세로로 늘리는 배수.
    /// </summary>
    /// <remarks>
    /// LANDDATA 의 부대 조각은 96x48 인데 게임은 <b>96x96 으로 늘려</b> 찍는다
    /// (<c>0x004B6963(0x60, 0x60)</c>). 배치 화면에서 말 세 마리 무리를 재면 원본이
    /// 121x120 으로 거의 정사각인데 조각 그대로 찍으면 2:1 로 납작해진다 —
    /// 싸움터도 같은 조각이라 같이 늘린다.
    /// </remarks>
    private const int UnitZoomY = 2;

    /// <summary>병사수 숫자가 앉는 층. 부대 그림(0)보다 위다.</summary>
    private const int DigitDepth = 50;

    /// <param name="drawW">화면에 걸 너비. 안 주면 그림 그대로다.</param>
    /// <param name="drawH">화면에 걸 높이. 부대 그림만 두 배로 늘려 건다.</param>
    private static Image Picture(uint[] bgra, int w, int h, int drawW = 0, int drawH = 0)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = drawW > 0 ? drawW : w,
            Height = drawH > 0 ? drawH : h,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>다시 그릴 때 걷을 것에 표를 붙인다.</summary>
    private static FrameworkElement Mark(FrameworkElement what)
    {
        what.Tag = UnitLayer;
        return what;
    }

    private static FrameworkElement At(FrameworkElement what, int x, int y)
    {
        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        return what;
    }
}
