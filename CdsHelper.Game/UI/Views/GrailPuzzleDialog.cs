using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「성배 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00467D50</c> 이다. 규칙과 판정은 <see cref="GrailPuzzle"/> 에 모아 두었다.
/// <code>
///   0x00559068  게임 설명
///   0x0056DFB0  "%d번째"          — 지금 몇 수인지
///   0x0056DFC8  "한 수 되돌립니까?"
///   0x0056DFE8  "다시 할 수 없습니다"
///   0x0056E048  "게임을 포기하겠습니까?"
/// </code>
/// <b>바탕은 게임 그림 그대로다</b> — MGGRAPH.CDS 파트 51(368x432)이고
/// <c>tools/extract_minigame_art.py</c> 가 뽑아 <c>asset/minigame/grail-bg.png</c> 에
/// 둔다. 그릇 자리도 게임이 쓰는 좌표를 그대로 쓴다.
/// <code>
///   0x0046810E  바가지 셋 — x = 12 · 60 · 108, y = 0x90 (144)
///   0x004681AE  성배 열   — x = 0x00559040 의 열 값, y = 0x13E (318)
///   0x00468077  큰 항아리 — (0xD5, 0x74) = (213, 116)
/// </code>
/// 값은 게임처럼 <b>분수</b>로 적는다 — 위가 든 물, 아래가 용량이다. 잡은 그릇에는
/// 흰 네모를 두른다.
///
/// 그릇을 하나 누르면 <b>주는 쪽</b>이 잡히고, 다음에 누른 것이 <b>받는 쪽</b>이 된다.
/// 오른쪽 단추로 잡은 것을 놓는다.
/// </remarks>
internal sealed class GrailPuzzleDialog : InfoDialog
{
    /// <summary>게임 그림의 크기. 자리 값이 다 이 눈금이다.</summary>
    private const int SceneWidth = 368, SceneHeight = 432;

    /// <summary>그림을 몇 배로 늘릴지. <b>정수배</b>라야 점이 안 뭉갠다.</summary>
    private const int Zoom = 2;

    /// <summary>바가지 셋의 왼쪽 끝과 줄 높이(<c>0x0046810E</c>).</summary>
    private static readonly int[] DipperX = [12, 60, 108];
    private const int DipperY = 142, DipperBoxW = 46, DipperBoxH = 62;

    /// <summary>성배 열의 왼쪽 끝(<c>0x00559040</c>)과 줄 높이(<c>0x004681AE</c>).</summary>
    private static readonly int[] GrailX = [19, 49, 80, 112, 145, 179, 214, 250, 287, 325];
    private const int GrailY = 316, GrailBoxW = 40, GrailBoxH = 50;

    /// <summary>큰 항아리 자리(<c>0x00468077</c>) 언저리.</summary>
    private const int JarX = 196, JarY = 100, JarBoxW = 132, JarBoxH = 158;

    private static readonly Brush Ring = Frozen(Colors.White);
    private static readonly Brush Done = Frozen(Color.FromRgb(0x6C, 0xE8, 0x6C));

    private readonly GrailPuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Dictionary<int, Border> _spot = [];
    private readonly Dictionary<int, GameUi.GameLabel> _now = [];
    private readonly GameUi.GameLabel _count = new(GameFont.WhiteColor) { Bold = true };
    private readonly GameButton _undo;

    private int _pick = -1;

    private GrailPuzzleDialog(int problem)
    {
        _game = new GrailPuzzle(problem);
        _undo = new GameButton("한 수 되돌림", AskUndo);

        if (Backdrop() is { } picture) _scene.Children.Add(picture);

        _count.FallbackBrush = Ring;
        Canvas.SetLeft(_count, 14);
        Canvas.SetTop(_count, 12);
        _scene.Children.Add(_count);

        Spot(GrailPuzzle.Jar, JarX, JarY, JarBoxW, JarBoxH, label: false);
        for (int i = 0; i < GrailPuzzle.Dippers; i++)
            Spot(GrailPuzzle.FirstDipper + i, DipperX[i], DipperY, DipperBoxW, DipperBoxH);
        for (int i = 0; i < GrailPuzzle.Grails; i++)
            Spot(GrailPuzzle.FirstGrail + i, GrailX[i], GrailY, GrailBoxW, GrailBoxH);

        _scene.LayoutTransform = new ScaleTransform(Zoom, Zoom);

        Build("성배 퍼즐", _scene, SceneWidth * Zoom + 30, SceneHeight * Zoom + 100,
              _undo,
              new GameButton("게임 설명", Explain),
              new GameButton("항복", AskGiveUp));

        MouseRightButtonUp += (_, _) => { _pick = -1; Sync(); };
        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _pick = -1; Sync(); } };

        Sync();
    }

    /// <summary>게임 그림. 못 읽으면 안 깐다 — 자리 네모만으로도 놀 수는 있다.</summary>
    private static UIElement? Backdrop()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "minigame", "grail-bg.png");
        if (!File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = SceneWidth, Height = SceneHeight };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    /// <summary>그릇 한 자리 — 누르는 칸과, 게임처럼 <b>분수</b>로 적는 값.</summary>
    private void Spot(int slot, int x, int y, int width, int height, bool label = true)
    {
        var box = new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Tap(slot); };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        _scene.Children.Add(box);
        _spot[slot] = box;

        if (!label) return;

        // 든 물 / 가로줄 / 용량. 게임도 이렇게 두 줄로 쌓아 적는다.
        bool grail = slot >= GrailPuzzle.FirstGrail;
        var now = new GameUi.GameLabel(GameFont.WhiteColor) { Bold = true, FallbackBrush = Ring };
        var cap = new GameUi.GameLabel(GameFont.WhiteColor) { Bold = true, FallbackBrush = Ring };
        cap.Text = $"{_game.SizeAt(slot)}";
        _now[slot] = now;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(now);
        stack.Children.Add(new Border
        {
            Height = 2,
            Width = 16,
            Background = Ring,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(cap);

        // 성배는 잔 밑동에, 바가지는 자루 왼쪽에 붙는다.
        Canvas.SetLeft(stack, x + (grail ? 3 : 9));
        Canvas.SetTop(stack, y + (grail ? 20 : 0));
        _scene.Children.Add(stack);
    }

    /// <summary>그릇을 눌렀다 — 처음이면 잡고, 두 번째면 붓는다.</summary>
    private void Tap(int slot)
    {
        if (_game.Over != null) return;

        if (_pick < 0)
        {
            if (_game.WaterAt(slot) == 0) return;   // 빈 그릇은 못 잡는다
            _pick = slot;
            Sync();
            return;
        }

        if (_pick == slot) { _pick = -1; Sync(); return; }

        _game.Pour(_pick, slot);
        _pick = -1;
        Sync();

        if (_game.Over != null) Close();
    }

    private void AskUndo()
    {
        if (!_game.CanUndo)
        {
            NoticeDialog.Show(this, "다시 할 수 없습니다", "경고");
            return;
        }
        if (!ConfirmDialog.Ask(this, "한 수 되돌립니까?", "취소")) return;

        _game.Undo();
        _pick = -1;
        Sync();
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "성공조건 [바로 앞에 있는 10개의 성배를 성수로 채워라.]" + Environment.NewLine +
            Environment.NewLine +
            "대·중·소의 물바가지를 잘 써서 큰 항아리 속의 성수로 모든 성배를 채워라." +
            Environment.NewLine +
            "탐험자가 움직일 수 있는 것은 물바가지 뿐이다. 큰 항아리는 물을 풀 수도 있고 " +
            "다시 놓을 수도 있다. 바가지와 바가지의 이동으로는 물이 넘칠 일은 없다." +
            Environment.NewLine +
            "성배에서 물이 넘치게 되면 당신은 죽게 된다.", "게임 설명");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "게임을 포기하겠습니까?", "항복")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        _count.Text = $"{_game.Moves}번째";

        foreach (var (slot, box) in _spot)
        {
            bool grail = _game.KindAt(slot) == GrailPuzzle.KindGrail;
            bool full = grail && _game.WaterAt(slot) == _game.SizeAt(slot);

            box.BorderBrush = slot == _pick ? Ring : full ? Done : Brushes.Transparent;
            if (_now.TryGetValue(slot, out var now)) now.Text = $"{_game.WaterAt(slot)}";
        }

        _undo.On = _game.CanUndo;
    }

    /// <summary>
    /// 놀이를 한 판 하고, <c>0x004684D0</c> 이 하듯 결과를 알린 뒤 상금을 준다.
    /// </summary>
    /// <remarks>
    /// 게임은 「대실패」와 「다시 한번 찬스」 뒤에 "다시 도전하겠습니까?" 를 묻고
    /// 그러겠다면 문제를 <b>새로 굴려</b> 다시 시작한다(<c>0x00468511</c> 로 돌아간다).
    /// </remarks>
    public static void Play(Window owner, Player player, Random rng)
    {
        while (true)
        {
            var dialog = new GrailPuzzleDialog(rng.Next(GrailPuzzle.Problems.Length))
            {
                Owner = owner,
            };
            dialog.ShowDialog();

            switch (dialog._game.Over ?? GrailPuzzle.Result.GaveUp)
            {
                case GrailPuzzle.Result.GaveUp:
                    NoticeDialog.Show(owner, "근성이 없는 녀석이로군···", "성스러운 항아리");
                    return;

                case GrailPuzzle.Result.Spilled:
                    NoticeDialog.Show(owner, "성배에서 물이 넘쳤다!", "대실패");
                    NoticeDialog.Show(owner, "재주가 없는 녀석이로군···한번 더 찬스를 주겠다",
                                      "성스러운 항아리");
                    if (!ConfirmDialog.Ask(owner, "다시 도전하겠습니까?", "메시지")) return;
                    break;

                case GrailPuzzle.Result.Slow:
                    NoticeDialog.Show(owner, "성수를 가득 채운 항아리가 뭔가 말하기 시작했습니다!",
                                      "메시지");
                    NoticeDialog.Show(owner,
                                      "재주가 없는 녀석이로군···으음···다시 한번 찬스를 주겠다",
                                      "성스러운 항아리");
                    if (!ConfirmDialog.Ask(owner, "다시 도전하겠습니까?", "메시지")) return;
                    break;

                case GrailPuzzle.Result.Good:
                    NoticeDialog.Show(owner, "모든 성배를 성수로 채웠다!", "성공");
                    return;

                case GrailPuzzle.Result.Great:
                    NoticeDialog.Show(owner, "성배로부터 눈부신 빛이 넘치기 시작했다!", "멋지게 성공");
                    NoticeDialog.Show(owner, $"금화 {GrailPuzzle.Prize} 닢을 손에 넣었습니다!", "성공");
                    player.Earn(GrailPuzzle.Prize);
                    return;
            }
        }
    }
}
