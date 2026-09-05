using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
internal sealed class LandBattleScene : Window
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

    private LandBattleScene(Engine.Game game, LandBattle battle, int scale)
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
        double areaW = owner?.ActualWidth ?? LandArt.FieldWidth;
        double areaH = owner?.ActualHeight ?? LandArt.FieldHeight;
        int scale = Math.Max(1, Math.Min(3, (int)Math.Min(areaW * 0.95 / LandArt.FieldWidth,
                                                          areaH * 0.95 / LandArt.FieldHeight)));

        var scene = new LandBattleScene(game, battle, scale) { _game = game, _dice = dice };
        if (owner != null) scene.Owner = owner;
        scene.Show();
        try { return scene.Fight(); }
        finally { scene.Close(); }
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

        // 판이 열릴 때 한 번 — 아이템을 지녔으면 작렬탄을 받는다(0x00448DD0).
        if (_battle.ShellWord.Length > 0) NoticeDialog.Show(this, _battle.ShellWord, "");

        // 첫 턴에만 일기토를 걸 수 있다(0x0044A604 가 +0x54 를 4 로 두고, 한 번
        // 싸우고 나면 0x00449BC5 어름이 끈다).
        bool first = true;

        while (true)
        {
            NoticeDialog.Show(this, _battle.TurnWord, "");

            int order = ChoiceDialog.Pick(this, $" {LandBattle.OrderTitle} ",
                                          _battle.OrderRows(canDuel: first,
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

            first = false;

            Play(fight.Turn(order, _battle.FoeOrder(dice)));

            if (fight.Over is { } won)
            {
                // 마을 공략에서는 적의 새 병력이 딱 한 번 붙는다(0x00449930).
                if (won && _battle.Reinforce(dice))
                {
                    NoticeDialog.Show(this, LandBattle.ReinforceWord, "");
                    fight = new LandFight(_battle, dice);
                    Redraw();
                    first = true;
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
            if (line.Text.Length == 0 || _quick) continue;
            Redraw();
            NoticeDialog.Show(this, line.Text, "");
        }
        Redraw();
    }

    /// <summary>부대와 병사수를 다시 그린다.</summary>
    private void Redraw()
    {
        for (int i = _board.Children.Count - 1; i >= 0; i--)
            if (_board.Children[i] is FrameworkElement { Tag: UnitLayer })
                _board.Children.RemoveAt(i);
        Stand();
    }

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
            if (Sprite(unit.Kind, friend: i < LandBattle.FirstFoe) is { } art)
                _board.Children.Add(Mark(At(art, x, y)));

            // 병사수는 칸 위쪽에 넉 자리 폭으로 가운데를 맞춰 찍는다.
            string men = unit.Men.ToString();
            int left = x + (DigitSlots - men.Length) * Digit / 2;
            foreach (char c in men)
            {
                if (Number(c - '0') is { } glyph) _board.Children.Add(Mark(At(glyph, left, y)));
                left += Digit;
            }
        }
    }

    /// <summary>그 병종의 첫 몸짓. 못 구하면 null.</summary>
    private Image? Sprite(int kind, bool friend)
    {
        if (_art == null) return null;

        var bgra = _art.TryGetUnit(kind, friend, _battle.Culture, frame: 0, out int w, out int h);
        return bgra == null ? null : Picture(bgra, w, h);
    }

    /// <summary>숫자 한 자. 조각을 못 구하면 게임 글꼴로 물러선다.</summary>
    private FrameworkElement? Number(int digit)
    {
        if (_art?.TryGetDigit(digit) is { } bgra) return Picture(bgra, Digit, Digit);
        return GameUi.GameFontLabel(digit.ToString(), GameFont.ButtonColor, 1,
                                    GameUi.ItemTextHeight);
    }

    private static Image Picture(uint[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill };
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
