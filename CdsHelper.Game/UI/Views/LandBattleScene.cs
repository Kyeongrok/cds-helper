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

    /// <summary>판을 늘려 건 배수 — 차림표를 판 구석에 붙일 때 여백을 이만큼 곱한다.</summary>
    private readonly double _scale;
    private readonly Canvas _board = new()
    {
        Width = LandArt.FieldWidth,
        Height = LandArt.FieldHeight,
    };

    private LandBattleScene(Engine.Game game, LandBattle battle, double scale)
    {
        _battle = battle;
        _scale = scale;
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
            int order = Ask(dice);
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

            // 「돌격」은 턴 첫머리에 한 번 소리를 낸다(0x0044932E).
            if (order == LandBattle.Charge) _game?.Sfx?.Play(LandUnits.Sound.Charge);

            var lines = fight.Turn(order, _battle.FoeOrder(dice));
            Play(lines, fight.Opening);

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
    /// 병사수를 찍을 때인지 — <b>차림표가 떠 있는 동안만</b> 참이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>+0x3C</c> 의 0x80 비트로 이것을 가른다. <c>0x00449CE8</c> 이 「제N턴」을
    /// 내걸기 직전에 세우고 <c>0x00449D82</c> 가 명령을 받자마자 지운다. 묘책 차림표도
    /// 같다(<c>0x00449B26</c> · <c>0x00449B5D</c>).
    /// </remarks>
    private bool _showMen;

    /// <summary>
    /// 「제N턴」을 내걸고 공격명령을 묻는다 — <b>이 동안만 병사수가 보인다</b>.
    /// </summary>
    /// <remarks>
    /// 일기토 칸은 <b>차림표를 열 때마다 굴린다</b> — 적 대장이 나보다 셀수록 열린다
    /// (<c>0x00447930</c>). 예전에는 첫 턴이면 늘 열어 두었다.
    /// </remarks>
    private int Ask(GameRandom dice)
    {
        _showMen = true;
        Redraw();
        try
        {
            NoticeDialog.Show(this, _battle.TurnWord, "");
            return ChoiceDialog.Pick(this, $" {LandBattle.OrderTitle} ",
                                     _battle.OrderRows(canDuel: _battle.DuelOffered(dice),
                                                       canRuse: _battle.AnyRuseLeft),
                                     Corner);
        }
        finally
        {
            _showMen = false;
            Redraw();
        }
    }

    /// <summary>
    /// 싸움 차림표를 <b>판 오른아래</b>에 붙인다.
    /// </summary>
    /// <remarks>
    /// 가운데에 세우면 창이 판 한복판을 가려 어느 부대가 어디 섰는지가 안 보인다. 게임도
    /// 공격명령·묘책 차림표를 판 오른아래에 낸다. 여백은 <b>판 점</b>으로 잡고 늘려 건
    /// 배수를 곱한다 — 그래야 창을 키워도 구석에서 떨어진 만큼이 같아 보인다.
    /// </remarks>
    private void Corner(Window box) => GameUi.PlaceAtCorner(box, this, MenuPad * _scale);

    /// <summary>차림표를 판 구석에서 띄우는 만큼(판 점).</summary>
    private const double MenuPad = 16;

    /// <summary>
    /// 「묘책」 — 기습·함정·암살자 가운데 하나를 건다(<c>0x004490D0</c>).
    /// </summary>
    /// <remarks>
    /// 한 판에 하나씩만 쓴다. 어그러지면 제 발등을 찍으므로 문화권마다의 성공률
    /// (<c>0x00549B80</c>)이 그대로 값이 된다.
    /// </remarks>
    private void Wile(LandFight fight, GameRandom dice)
    {
        int pick;
        _showMen = true;
        Redraw();
        try { pick = ChoiceDialog.Pick(this, $" {LandBattle.RuseTitle} ", _battle.RuseRows(), Corner); }
        finally { _showMen = false; Redraw(); }

        if (pick < 0 || pick == LandBattle.Judgement) return;

        var said = fight.Ruse(pick, dice, out bool asked);
        foreach (var line in said)
        {
            if (line.Sound >= 0) _game?.Sfx?.Play(line.Sound);
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

    /// <summary>
    /// 한 턴에 일어난 일을 보여 주고 판을 다시 그린다.
    /// </summary>
    /// <remarks>
    /// <b>부대 하나가 한 차례에 한 일을 묶어서</b> 보인다 — 총병·포병은 맞은편 앞열을
    /// 통째로 치므로 줄이 여럿인데, 게임은 <b>다 쏘고 나서</b> 피해를 한꺼번에 띄운다.
    /// 한 발 쏘고 숫자 띄우고를 되풀이하지 않는다.
    ///
    /// 그 한 묶음의 차례가 셋이다 — <b>나가서 (여러 번) 치고</b>, 나간 그 자리에 선 채로
    /// <b>숫자가 떴다 지고</b>, 그러고 나서 <b>돌아온다</b>.
    /// </remarks>
    private void Play(IReadOnlyList<LandFight.Line> lines, IReadOnlyList<int> opening)
    {
        // 한 턴은 이미 다 굴려 놓은 것이라 판은 <b>끝난 뒤</b>의 병사수를 들고 있다.
        // 그림을 도는 동안에는 줄이 담아 온 그때그때의 병사수로 그린다.
        _menNow = opening;

        for (int at = 0; at < lines.Count; )
        {
            int actor = lines[at].Actor;
            int end = at + 1;
            while (actor >= 0 && end < lines.Count && lines[end].Actor == actor) end++;

            var bout = new List<LandFight.Line>();
            for (int k = at; k < end; k++) bout.Add(lines[k]);
            at = end;

            if (_quick)
            {
                foreach (var line in bout)
                    if (line.Sound >= 0) _game?.Sfx?.Play(line.Sound);
                continue;
            }

            Swing(bout);
            Flash(bout);

            // 피해 숫자가 진 <b>다음에</b> 쓰러진 부대가 말을 남기고, 그러고 나서 사라진다
            // (0x00447F50 이 0x00446C00 으로 말을 골라 말풍선을 띄우고 칸을 비운다).
            foreach (var line in bout)
                if (line.Felled >= 0 && line.Fell.Length > 0) Balloon(line.Felled, line.Fell);

            _menNow = bout[^1].Men ?? _menNow;
            Redraw();
            Home();

            // 깎인 병사수는 창으로 안 낸다. 남은 말은 <b>부대 곁에 말풍선</b>으로 낸다 —
            // 어느 부대가 한 말인지 모르는 것(묘책 같은 것)만 창으로 낸다.
            foreach (var line in bout)
            {
                if (line.Damage > 0 || line.Text.Length == 0) continue;
                if (line.Actor >= 0) { Balloon(line.Actor, line.Text); continue; }
                Redraw();
                NoticeDialog.Show(this, line.Text, "");
            }
        }

        _menNow = null;
        Redraw();
    }

    /// <summary>
    /// 그림을 도는 동안 쓸 병사수 열둘. null 이면 판이 든 지금 값을 그대로 쓴다.
    /// </summary>
    private IReadOnlyList<int>? _menNow;

    /// <summary>
    /// 맞은 부대들 위에 <b>깎인 병사수</b>를 <b>한꺼번에</b> 띄웠다 지운다.
    /// </summary>
    /// <remarks>
    /// 게임 화면에서 보이는 그 큰 흰 숫자다 — 부대배치 판이 쓰는 것과 같은 조각
    /// (LANDDATA 파트 52, 24x24 열 자)이다. 총병이 앞열 셋을 쏘면 <b>셋이 같이</b> 뜬다.
    ///
    /// 흔들림과 숫자는 <b>딴 마당이다</b>. 치는 몸짓이 끝나면 맞은 부대가 먼저 좌우로
    /// 흔들리고(표 <c>0x00549C08</c> 의 −8·8·8·−8 을 넉 장 — <c>0x00449250</c> 이 맞은 칸의
    /// <c>+0x1C</c> 에 0x80 을 세우고 <c>0x00448640</c> 이 지운다), <b>그것이 멎고 나서야</b>
    /// 숫자가 떠오른다(<c>0x004462A0</c> 의 <c>ebx = 4</c>). 다 뜨고 나서 다섯 눈금을 더
    /// 쉰다(<c>0x00428000(5, 0)</c>).
    ///
    /// 갈무리에서도 숫자가 오르는 넉 장 동안 부대 그림은 <b>한 점도 안 움직인다</b> —
    /// 그 넉 장을 겹쳐 재면 어긋남이 0 이다. 둘을 한 고리에서 같이 돌리면 안 된다.
    /// </remarks>
    private void Flash(IReadOnlyList<LandFight.Line> bout)
    {
        var hurt = new List<int>();
        var hits = new List<(int Slot, string Men)>();
        foreach (var line in bout)
        {
            if (line.Damage <= 0) continue;
            if (line.Target < 0 || line.Target >= StandAt.Length) continue;
            if (!hurt.Contains(line.Target)) hurt.Add(line.Target);
            hits.Add((line.Target, line.Damage.ToString()));
        }

        if (hits.Count == 0) return;

        // ① 치는 몸짓이 끝나면 <b>한 장 멎었다가</b> 맞은 부대가 좌우로 흔들린다.
        //    이 동안 숫자는 아직 안 뜬다.
        Rest(SwingMs);

        int drift = 0;
        foreach (int step in Shakes)
        {
            // 표는 <b>자리가 아니라 걸음</b>이다 — 더해 가야 0·−8·0·+8·0 이 된다.
            drift += step;
            foreach (int slot in hurt) _shake[slot] = drift;
            Redraw();
            Rest(SwingMs);
        }
        foreach (int slot in hurt) _shake[slot] = 0;
        Redraw();

        // ② 흔들림이 멎고 나서 숫자가 뜬다.
        var shown = new List<(FrameworkElement Glyph, int Left, int Top)>();
        foreach (var (slot, men) in hits)
        {
            var (x, y) = StandAt[slot];
            int left = x + (LandArt.DeployWidth - men.Length * Digit) / 2;
            int top = y + (LandArt.DeployWidth - Digit) / 2;

            foreach (char c in men)
            {
                if (Number(c - '0') is { } glyph)
                {
                    Panel.SetZIndex(glyph, FlashDepth);
                    _board.Children.Add(At(glyph, left - Outline, top - Outline));
                    shown.Add((glyph, left - Outline, top - Outline));
                }
                left += Digit;
            }
        }

        if (shown.Count == 0) { Rest(RestMs); return; }

        // ③ 넉 장에 걸쳐 왼위로 떠오른다. 마지막 자리에 선 채로 다섯 눈금을 더 쉰다.
        foreach (var (dx, dy) in Rises)
        {
            foreach (var (glyph, left, top) in shown) At(glyph, left + dx, top + dy);
            Rest(SwingMs);
        }
        Rest(RestMs);

        foreach (var (glyph, _, _) in shown) _board.Children.Remove(glyph);
    }

    /// <summary>피해 숫자를 얹는 높이.</summary>
    private const int FlashDepth = DigitDepth + 10;

    // ── 말풍선 — 0x00445B20 ───────────────────────────────────────────────────

    /// <summary>
    /// 부대 곁에 <b>말풍선</b>을 띄웠다 지운다.
    /// </summary>
    /// <remarks>
    /// 게임은 싸움 중의 말을 창으로 안 내고 판 위에 말풍선으로 낸다(<c>0x00445B20</c>).
    /// 둥근 상자의 <b>네 귀</b>를 조각으로 놓고 사이를 흰 칸으로 메운다 — 조각은
    /// <see cref="LandArt.TryGetBubble"/> 를 본다.
    ///
    /// <b>자리</b>도 그 자리에서 읽었다. 글자 수(CP949 바이트의 절반)를 <c>n</c> 이라 하면
    /// <code>
    ///   0x00445B70  적이 말하면   x 어긋남 = −(n + 1) * 16   ; 부대 왼쪽에 편다
    ///   0x00445B80  아군이 말하면 x 어긋남 = 96              ; 부대 오른쪽에 편다
    ///   0x00445B8B  y 어긋남 = 0                             ; 부대 칸 꼭대기에 맞춘다
    /// </code>
    /// 곧 풍선은 <b>말하는 부대 바깥쪽</b>으로 펴지고 꼬리가 그 부대를 가리킨다.
    ///
    /// 머무는 시간은 아직 게임에서 못 찾았다 — 읽을 만큼으로 잡아 두었다.
    /// </remarks>
    private void Balloon(int slot, string text)
    {
        if (_art == null || slot < 0 || slot >= StandAt.Length) return;

        int cells = Cells(text);
        int side = LandArt.BubbleSide;
        int wide = (cells + 2) * side;
        bool mine = slot < LandBattle.FirstFoe;

        var (ux, uy) = StandAt[slot];
        int x = mine ? ux + LandArt.DeployWidth : ux - (cells + 1) * side;
        int y = uy;
        x = Math.Clamp(x, 0, Math.Max(0, LandArt.FieldWidth - wide));

        var put = new List<UIElement>();
        void Lay(UIElement what, int at, int top)
        {
            Panel.SetZIndex(what, BubbleDepth);
            _board.Children.Add(At((FrameworkElement)what, at, top));
            put.Add(what);
        }

        // 가운데는 흰 칸으로 메우고 네 귀에 조각을 놓는다.
        Lay(new Border
        {
            Width = cells * side,
            Height = side * 2,
            Background = Brushes.White,
        }, x + side, y);

        Corner(LandArt.BubbleTopLeft, x, y, Lay);
        Corner(LandArt.BubbleTopRight, x + wide - side, y, Lay);
        Corner(mine ? LandArt.BubbleMineLeft : LandArt.BubbleFoeLeft, x, y + side, Lay);
        Corner(mine ? LandArt.BubbleMineRight : LandArt.BubbleFoeRight,
               x + wide - side, y + side, Lay);

        // 글은 흰 바탕에 검은 벌로, 풍선 가운데에 놓는다.
        var words = new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight)
        {
            Text = text,
            Bold = true,
            FallbackBrush = Brushes.Black,
        };
        Lay(words, x + side, y + (side * 2 - GameUi.ItemTextHeight) / 2);

        Rest(BubbleMs);
        foreach (var what in put) _board.Children.Remove(what);
    }

    /// <summary>말풍선 귀 한 장을 놓는다. 조각을 못 읽으면 그냥 넘어간다.</summary>
    private void Corner(int piece, int x, int y, Action<UIElement, int, int> lay)
    {
        if (_art?.TryGetBubble(piece) is not { } bgra) return;
        lay(Picture(bgra, LandArt.BubbleSide, LandArt.BubbleSide), x, y);
    }

    /// <summary>
    /// 그 말이 먹는 <b>칸 수</b>. 게임은 CP949 바이트를 반으로 나눠 센다 — 한글 한 자가
    /// 한 칸(16점)이고 로마자는 반 칸이다.
    /// </summary>
    private static int Cells(string text)
    {
        double wide = text.Sum(c => c < 0x80 ? 0.5 : 1.0);
        return Math.Max(1, (int)Math.Ceiling(wide));
    }

    /// <summary>말풍선이 머무는 밀리초와 그것을 얹는 높이.</summary>
    private const int BubbleMs = 900, BubbleDepth = FlashDepth + 10;

    // ── 치는 몸짓 ──────────────────────────────────────────────────────────────

    /// <summary>지금 치고 있는 자리와 그 몸짓. −1 이면 아무도 안 친다.</summary>
    private int _acting = -1, _actFrame;

    /// <summary>친 부대가 제자리에서 나가 있는 만큼.</summary>
    private int _actDx, _actDy;

    /// <summary>맞은 부대가 흔들린 만큼. 안 맞았으면 0 이다.</summary>
    private readonly int[] _shake = new int[LandBattle.Slots];

    /// <summary>
    /// 눈금 하나 — <b>50밀리초</b>다.
    /// </summary>
    /// <remarks>
    /// 게임의 시계 <c>0x004BA4BB</c> 가 <c>GetTickCount() / 50</c> 을 낸다. 몸짓을 돌리는
    /// 자리는 죄다 <c>0x00428000(2, 0)</c> 으로 <b>두 눈금</b>을 쉬므로 한 장이 100밀리초다.
    /// </remarks>
    private const int Tick = 50;

    /// <summary>몸짓 한 장이 머무는 밀리초 — 두 눈금이다.</summary>
    private const int SwingMs = Tick * 2;

    /// <summary>피해 숫자를 다 보이고 더 쉬는 밀리초 — 다섯 눈금(<c>0x00448700</c>).</summary>
    private const int RestMs = Tick * 5;

    /// <summary>
    /// 치는 몸짓은 <b>넉 장</b>이다(<c>0x00446210</c>).
    /// </summary>
    /// <remarks>
    /// 조각 한 벌에 몸짓이 여덟 들어 있지만 싸움터가 쓰는 것은 앞 넉 장뿐이다 —
    /// <c>0x00446210</c> 이 <c>+0x114</c> 를 0 부터 3 까지만 올린다. 제독 조각(파트 14)을
    /// 떠 보면 0·1 이 선 자세, 2·3 이 찌르는 자세다.
    /// </remarks>
    private const int SwingFrames = 4;

    /// <summary>소리는 <b>셋째 장</b>에서 난다(<c>0x0044623B</c> 의 <c>ebp == 2</c>).</summary>
    private const int SoundFrame = 2;

    /// <summary>
    /// 맞은 부대를 좌우로 흔드는 값(표 <c>0x00549C08</c>).
    /// </summary>
    /// <remarks>
    /// <b>자리가 아니라 걸음이다.</b> 더해 가야 −8 · 0 · +8 · 0 이 되어 왼쪽으로 한 번,
    /// 가운데로, 오른쪽으로 한 번, 다시 가운데로 돌아온다. 자리로 박으면 −8·+8·+8·−8 이라
    /// 가운데를 안 거치고 좌우로 튄다.
    ///
    /// 갈무리로 확인했다 — 다섯 장을 겹쳐 재면 어긋남이 0 · −14 · 0 · +14 · 0 이고
    /// 셋째·다섯째 장은 첫 장과 <b>점 하나까지 같다</b>. 배수 1.71 로 나누면 판 점 8 이라
    /// 표의 값과 맞아떨어진다.
    /// </remarks>
    private static readonly int[] Shakes = [-8, 8, 8, -8];

    /// <summary>
    /// 피해 숫자가 넉 장 동안 <b>왼위로 떠오르는</b> 자리(판 점).
    /// </summary>
    /// <remarks>
    /// 이 값만은 <b>게임 코드가 아니라 갈무리에서 쟀다</b> — 숫자 조각 두 자의 자리 사이가
    /// 41점으로 찍힌 판이라 배수가 1.71 이고, 장마다 14점씩 움직인 것이 판 점으로 8 이다.
    /// 세로는 넉 장 내내 한 걸음씩이고 가로는 <b>한 장 늦게</b> 따라붙는다.
    ///
    /// 이 넉 장은 <b>흔들림이 다 끝난 뒤</b>다(<see cref="Flash"/>). 그래서 첫 자리가
    /// (0, 0) 이다 — 맞은 자리에 한 장 떠 있다가 오르기 시작한다.
    /// </remarks>
    private static readonly (int Dx, int Dy)[] Rises = [(0, 0), (0, -8), (-8, -16), (-16, -24)];

    /// <summary>
    /// 한 걸음에 나아가는 만큼 — <b>한 칸의 반</b>이다.
    /// </summary>
    /// <remarks>자리 여섯이 (64, 48) 씩 어긋나 있으니 두 걸음이 딱 한 칸이다.</remarks>
    private const int StrideX = 32, StrideY = 24;

    /// <summary>
    /// 목표 앞까지 걸어 나가는 걸음 수 — <b>앞열은 둘, 후열은 넷</b>이다. 돌아올 때도 같다.
    /// </summary>
    /// <remarks>
    /// <c>0x00446310</c> 은 목표를 아예 안 본다. 부대 <c>+0x08</c>(후열 표시)만 보고
    /// 되풀이 수를 정한다.
    /// <code>
    ///   0044633e  cmp [부대+0x08], 1        ; 앞열이면 0
    ///   00446351  edi = 후열 ? 4 : 2        ; 그만큼 되풀이하며 +0x114 를 올린다
    /// </code>
    /// 그래서 <b>어느 자리에서 나가든 서는 데가 같다</b> — 제 세로줄 바로 앞칸이다.
    /// <code>
    ///   아군  (176,160) (240,208) (304,256)      적  (368,208) (304,160) (240,112)
    /// </code>
    /// 예전에 화면 두 장을 재어 「목표까지 거리의 반쯤」으로 두었던 것은, 후열 부대가
    /// 넉 걸음 나간 것을 잰 것이었다.
    /// </remarks>
    private static int StridesOf(int slot) => LandUnits.IsFront(slot) ? 2 : 4;

    /// <summary>
    /// 한 줄을 몸짓으로 보인다 — <b>나가서 치는 데까지</b>다.
    /// </summary>
    /// <remarks>
    /// 부대 조각 한 벌이 96x48 여덟 장(2열 4행)인데 <b>싸움터가 쓰는 것은 앞 넉 장</b>이다.
    ///
    /// <b>맞붙는 병종</b>(기병·제독·장군처럼 손에 무기를 든 것 —
    /// <see cref="LandUnits.Kind.Melee"/>)은 <b>제 세로줄 앞칸까지 걸어 나가</b> 거기서
    /// 친다. 총·포·지원은 제자리에서 몸짓만 돌린다(<c>0x00448A5E</c> 가 <c>+0x1C</c> 에
    /// 0x100 을 세운다).
    ///
    /// <b>돌아오는 것은 여기서 안 한다</b> — 게임은 나간 그 자리에 선 채로 피해 숫자를
    /// 보이고, 그것이 진 뒤에 돌아온다(<see cref="Home"/>).
    /// </remarks>
    private void Swing(IReadOnlyList<LandFight.Line> bout)
    {
        int slot = bout[0].Actor;
        if (slot < 0 || slot >= StandAt.Length) return;
        if (!_battle.Units[slot].Standing) return;

        // 노린 데가 있는 줄 하나가 몸짓 한 바퀴다.
        var blows = bout.Where(line => line.Target >= 0).ToList();

        // <b>지원 갈래는 노린 데가 없어도 제자리에서 돈다</b> — 비·회복·춤이 그것이다.
        // 게임도 0x004485A0 을 그대로 부르고, 지원(21~23)만 <b>두 바퀴</b>를 돌린다
        // (0x004485E7 어름의 <c>eax = 2</c>). 소리도 바퀴마다 한 번씩 난다.
        int rounds = 1;
        if (blows.Count == 0)
        {
            bool support = LandUnits.KindOf(_battle.Units[slot].Kind) == LandUnits.Kind.Support;
            if (!support || bout[0].Text.Length == 0) return;   // 「비에 젖어…」 같은 말뿐인 줄
            blows = [bout[0]];
            rounds = 2;
        }

        var (dx, dy) = StepOut(slot);
        _acting = slot;
        _actFrame = 0;
        _actDx = _actDy = 0;

        // ① 제 세로줄 앞칸까지 걸어 나간다 — 걸음마다 몸짓이 한 장씩 넘어간다
        //    (0x00446310 이 +0x114 를 1 부터 올리고 넉 장을 넘으면 붙든다).
        if (dx != 0 || dy != 0)
        {
            int strides = StridesOf(slot);
            for (int i = 1; i <= strides; i++)
            {
                _actFrame = Math.Min(i, SwingFrames - 1);
                _actDx = dx * i;
                _actDy = dy * i;
                Redraw();
                Rest(SwingMs);
            }
        }

        // ② 그 자리에서 <b>친 수만큼</b> 몸짓 넉 장을 돌린다 — 총병은 앞열 수만큼 쏜다.
        //    소리는 장마다가 아니라 <b>셋째 장</b>에서 한 번 난다.
        foreach (var line in blows)
            for (int r = 0; r < rounds; r++)
                for (int f = 0; f < SwingFrames; f++)
                {
                    if (f == SoundFrame && line.Sound >= 0) _game?.Sfx?.Play(line.Sound);
                    _actFrame = f;
                    Redraw();
                    Rest(SwingMs);
                }
    }

    /// <summary>
    /// 나갔던 부대가 <b>제자리로 돌아온다</b>. 피해 숫자가 진 뒤에 부른다.
    /// </summary>
    private void Home()
    {
        if (_acting < 0) return;

        int dx = _actDx, dy = _actDy;
        if (dx != 0 || dy != 0)
        {
            int strides = StridesOf(_acting);
            for (int i = strides - 1; i >= 0; i--)
            {
                _actFrame = Math.Min(Math.Max(i, 1), SwingFrames - 1);
                _actDx = dx * i / strides;
                _actDy = dy * i / strides;
                Redraw();
                Rest(SwingMs);
            }
        }

        _acting = -1;
        _actFrame = 0;
        _actDx = _actDy = 0;
        Redraw();
    }

    /// <summary>
    /// 한 걸음에 나아가는 만큼. 맞붙는 병종이 아니면 둘 다 0 이다.
    /// </summary>
    /// <remarks>
    /// 나가고 돌아오는 것은 <b>근접 갈래만</b> 한다 — <c>0x00448760</c>·<c>0x00448790</c>
    /// 을 부르는 데가 창병(<c>0x004487C0</c>)과 여느 근접(<c>0x00448900</c>) 둘뿐이고,
    /// <c>0x00446310</c> 도 부대 <c>+0x00</c>(갈래)이 0 이 아니면 그냥 돌아간다.
    /// 아군은 오른위로, 적은 왼아래로 간다.
    /// </remarks>
    private (int Dx, int Dy) StepOut(int slot)
    {
        if (LandUnits.KindOf(_battle.Units[slot].Kind) != LandUnits.Kind.Melee) return (0, 0);
        return slot < LandBattle.FirstFoe ? (StrideX, -StrideY) : (-StrideX, StrideY);
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

        // 끝맺음 소리 — <b>이김과 물러남에만</b> 있다. 진 자리(+0x3C 가 4)는 0x00449890
        // 으로 빠져 소리가 없다(0x004499B5 · 0x004499FF).
        if (won) _game?.Sfx?.Play(LandUnits.Sound.Won);
        else if (retreated) _game?.Sfx?.Play(LandUnits.Sound.Retreat);

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

            // 그림을 도는 동안에는 그때그때의 병사수로 본다 — 그래야 쓰러진 부대가
            // 명령을 누르자마자가 아니라 <b>제 차례에</b> 사라진다.
            int men = _menNow is { } snap && i < snap.Count ? snap[i] : unit.Men;
            if (unit.Kind < 0 || men <= 0) continue;

            var (x, y) = StandAt[i];

            // 치고 있는 부대는 몸짓을 갈아 끼우고, 맞붙는 병종이면 앞으로 나가 있다.
            int frame = 0;
            if (i == _acting)
            {
                frame = _actFrame;
                (x, y) = (x + _actDx, y + _actDy);
            }

            // 맞은 부대는 숫자가 뜨는 동안 좌우로 흔들린다(0x00549C08).
            x += _shake[i];

            if (Sprite(unit.Kind, friend: i < LandBattle.FirstFoe, frame) is { } art)
            {
                // 나가서 치는 부대는 남의 자리에 서므로 <b>맨 앞으로</b> 올린다 —
                // 안 그러면 치러 간 부대가 맞는 부대 뒤에 깔린다.
                if (i == _acting) Panel.SetZIndex(art, ActDepth);
                _board.Children.Add(Mark(At(art, x, y)));
            }

            // 병사수는 <b>차림표가 떠 있는 동안만</b> 찍는다. 게임은 0x00449CE8 이
            // 차림표를 열기 직전에 +0x3C 에 0x80 을 세우고 0x00449D82 가 닫자마자
            // 지운다 — 그래서 몸짓이 도는 동안에는 숫자가 하나도 안 보인다.
            if (!_showMen) continue;

            // 병사수는 칸 위쪽에 넉 자리 폭으로 가운데를 맞춰 찍는다.
            //
            // <b>숫자는 늘 맨 앞이다.</b> 부대를 차례대로 놓으므로 뒤에 놓인 부대 그림이
            // 앞서 찍은 숫자를 덮는다 — 그림이 칸을 꽉 채우게 되면서 도드라졌다.
            string count = men.ToString();
            int left = x + (DigitSlots - count.Length) * Digit / 2;
            foreach (char c in count)
            {
                if (Number(c - '0') is { } glyph)
                {
                    Panel.SetZIndex(glyph, DigitDepth);
                    _board.Children.Add(Mark(At(glyph, left - Outline, y - Outline)));
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
        return bgra == null ? null : Picture(Taller(bgra, w, h), w, h * UnitZoomY);
    }

    /// <summary>
    /// 줄을 하나씩 겹쳐 <b>조각 자체를</b> 세로로 늘린다.
    /// </summary>
    /// <remarks>
    /// 늘리는 일을 <see cref="Image"/> 크기에 맡기면(<c>Height = h * 2</c>) 보간이 세로로만
    /// 두 배 더 먹어 가로보다 세로가 더 뭉갠다 — 결이 한쪽으로 쏠려 계단이 더 도드라진다.
    /// 여기서 미리 겹쳐 두면 <b>점이 정사각</b>이 되어, 보간은 판 배율만큼만 고르게 든다.
    ///
    /// 없는 줄을 지어내는 것은 아니다. 조각이 48줄뿐이라 한 점짜리 가는 선(총열이 그렇다)은
    /// 어차피 두 줄로 굵어진다 — 그 계단은 어떤 보간으로도 못 없앤다.
    /// </remarks>
    private static uint[] Taller(uint[] bgra, int w, int h)
    {
        var tall = new uint[w * h * UnitZoomY];
        for (int y = 0; y < h; y++)
            for (int again = 0; again < UnitZoomY; again++)
                Array.Copy(bgra, y * w, tall, (y * UnitZoomY + again) * w, w);
        return tall;
    }

    /// <summary>
    /// 숫자 한 자 — 조각에 <b>검은 테를 한 겹 둘러</b> 낸다. 조각을 못 구하면 게임 글꼴로
    /// 물러선다.
    /// </summary>
    /// <remarks>
    /// 조각에도 어두운 테가 한 점 있기는 하나, 싸움터가 밝은 돌바닥이거나 숫자가 부대
    /// 그림에 겹치면 흰 획이 바탕에 묻혀 읽기 어렵다. 그래서 <b>안 비치는 점의 둘레 여덟
    /// 칸</b>을 까맣게 깔고 그 위에 조각을 그대로 얹는다 — 획은 손대지 않고 테만 두꺼워진다.
    ///
    /// 테를 두른 만큼 조각이 <see cref="DigitBox"/> 로 커지므로, 놓는 자리도 두께만큼
    /// 당겨야 숫자가 있던 데에 그대로 선다.
    /// </remarks>
    private FrameworkElement? Number(int digit)
    {
        if (digit is >= 0 and <= 9 && Glyph(digit) is { } bgra)
            return Picture(bgra, DigitBox, DigitBox);
        return GameUi.GameFontLabel(digit.ToString(), GameFont.ButtonColor, 1,
                                    GameUi.ItemTextHeight);
    }

    /// <summary>
    /// 테를 두른 숫자 조각. 한 번 지어 두고 다시 쓴다 — 한 턴에 수십 번 부른다.
    /// </summary>
    /// <remarks>못 구한 자리는 빈 배열로 적어 둔다. 그래야 파트를 되풀이해 풀지 않는다.</remarks>
    private uint[]? Glyph(int digit)
    {
        _glyphs[digit] ??= _art?.TryGetDigit(digit) is { } bgra ? Outlined(bgra) : [];
        return _glyphs[digit] is { Length: > 0 } made ? made : null;
    }

    /// <summary>테를 두른 숫자 열 자 — 처음 쓸 때 하나씩 짓는다.</summary>
    private readonly uint[]?[] _glyphs = new uint[]?[10];

    /// <summary>조각 둘레에 까만 테를 한 겹 두른다.</summary>
    private static uint[] Outlined(uint[] glyph)
    {
        var box = new uint[DigitBox * DigitBox];

        // ① 안 비치는 점마다 그 둘레 여덟 칸까지 까맣게 깐다.
        for (int y = 0; y < Digit; y++)
            for (int x = 0; x < Digit; x++)
            {
                if (glyph[y * Digit + x] >> 24 == 0) continue;
                for (int dy = 0; dy <= Outline * 2; dy++)
                    for (int dx = 0; dx <= Outline * 2; dx++)
                        box[(y + dy) * DigitBox + x + dx] = Ink;
            }

        // ② 그 위에 조각을 그대로 얹는다 — 획은 조각이 가진 색 그대로다.
        for (int y = 0; y < Digit; y++)
            for (int x = 0; x < Digit; x++)
                if (glyph[y * Digit + x] >> 24 != 0)
                    box[(y + Outline) * DigitBox + x + Outline] = glyph[y * Digit + x];

        return box;
    }

    /// <summary>숫자에 두르는 테의 두께와, 그만큼 커진 조각의 한 변.</summary>
    private const int Outline = 1, DigitBox = Digit + Outline * 2;

    /// <summary>테 색 — 아주 검다(BGRA).</summary>
    private const uint Ink = 0xFF000000;

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

    /// <summary>
    /// 조각 한 장을 <b>보간해서</b> 건다.
    /// </summary>
    /// <remarks>
    /// 예전에는 점을 안 뭉개려고 <c>NearestNeighbor</c> 에 <c>EdgeMode.Aliased</c> 였다.
    /// 그런데 부대 조각은 세로로 두 배 늘려 걸므로(<see cref="UnitZoomY"/>) 점이 정사각이
    /// 아니고, 거기에 판 배율이 다시 곱해져 <b>단이 가로보다 세로로 긴 계단</b>이 진다.
    /// 원본 갈무리를 재면 이음의 94%가 한 점이고 가로·세로 결이 5.32 대 5.47 로 같다 —
    /// 보간이 들어간 그림의 모양이다. 그래서 이쪽도 <c>Fant</c> 로 맞춘다.
    ///
    /// <b>색자리는 <c>Pbgra32</c> 여야 한다.</b> 곧은 알파(<c>Bgra32</c>)로 두고 보간하면
    /// 비치는 자리의 색이 검정(0,0,0,0)이라 부대 둘레에 어두운 테가 낀다. 조각을 푸는 쪽이
    /// 비침을 0 으로, 나머지를 알파 255 로 적으므로 그 값이 <b>곱해 둔 알파로 이미 맞다</b> —
    /// 이름표만 바꿔 주면 된다.
    /// </remarks>
    /// <param name="drawW">화면에 걸 너비. 안 주면 그림 그대로다.</param>
    /// <param name="drawH">화면에 걸 높이. 부대 그림만 두 배로 늘려 건다.</param>
    private static Image Picture(uint[] bgra, int w, int h, int drawW = 0, int drawH = 0)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = drawW > 0 ? drawW : w,
            Height = drawH > 0 ? drawH : h,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
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
