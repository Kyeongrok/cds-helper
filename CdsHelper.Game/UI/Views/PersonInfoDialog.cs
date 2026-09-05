using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「인물정보」 — 제독 자신을 보여 주는 판.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0046DF70</c> 이다. 줄 글은 그쪽 서식 그대로다(<c>0x00570EE8</c> 벌).
/// <code>
///   "체  력/%4d"      "    명성치/%8d"
///   "지  력/%4d"      "    악명치/%8d"
///   "무  력/%4d    직업  /%s"
///   "매  력/%4d"
///   "연령  /%2d세"    "          생년월일/%4d년%2d월%2d일"
///   "별자리/%-6s        혈액형  /%s"
///   "국적  /%s"
///   "소지금/%10ld닢"  "저금  /%10ld닢"  "빚    /%10ld닢"
///   "특기"  "취소"
/// </code>
/// <b>판이 밤색이 아니라 강청색이다</b> — 화면에서 뽑은 값이 바탕
/// <c>(92, 111, 147)</c>, 테 <c>(54, 65, 86)</c> 다. 정보 판 가운데 이것만 색이 다르다.
///
/// 왼쪽 위에 초상화가 서고, 능력치는 <b>넷만</b> 뜬다(운·신앙심은 안 보인다).
/// "특기" 를 누르면 기술과 어학이 따로 열린다.
/// </remarks>
internal sealed class PersonInfoDialog : InfoDialog
{
    /// <summary>
    /// 판 크기(그림 점). 게임 갈무리를 재어 맞췄다 — 판 바탕이 <b>430 x 270</b> 이고,
    /// 좌우 여백 14 씩과 아래 단추 줄을 빼면 속이 이만큼이다.
    /// </summary>
    private const double BoardWidth = 402, BoardHeight = 224;

    /// <summary>
    /// 초상화를 몇 배로 그릴지. 게임은 <b>조각 그대로</b> 80x96 이다 —
    /// 두 배로 걸면 얼굴만 커져 글자와 어긋난다.
    /// </summary>
    private const int FaceScale = 1;

    /// <summary>이 판은 글씨가 검정이다. 밤색 판들과 다른 자리다.</summary>
    private const byte BlackInk = GameFont.BlackColor;

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private PersonInfoDialog(Player player, Portraits? faces)
    {
        var head = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        head.Children.Add(BlackLine($"  {player.Name}"));
        head.Children.Add(BlackLine($"  체  력/{player.AbilityOf(Ability.Body),4}" +
                                $"    명성치/{player.Fame,8}"));
        head.Children.Add(BlackLine($"  지  력/{player.AbilityOf(Ability.Mind),4}" +
                                $"    악명치/{player.Infamy,8}"));
        head.Children.Add(BlackLine($"  무  력/{player.AbilityOf(Ability.Might),4}" +
                                $"    직업  /{player.Work.Name}"));
        head.Children.Add(BlackLine($"  매  력/{player.AbilityOf(Ability.Charm),4}"));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (Face(player, faces) is { } portrait) top.Children.Add(portrait);
        top.Children.Add(head);

        var rows = new StackPanel();
        rows.Children.Add(top);
        rows.Children.Add(Gap(14));
        rows.Children.Add(BlackLine($"  연령  /{player.Age,2}세          " +
                                $"생년월일/{player.BirthYear,4}년{player.BirthMonth,2}월{player.BirthDay,2}일"));
        rows.Children.Add(BlackLine($"  별자리/{GameUi.Pad(player.Zodiac, 12)}혈액형  /{player.BloodName}"));
        rows.Children.Add(BlackLine($"  국적  /{player.NationName}"));
        rows.Children.Add(BlackLine($"  소지금/{player.Gold,10}닢"));
        rows.Children.Add(BlackLine($"  저금  /{player.Savings,10}닢"));
        rows.Children.Add(BlackLine($"  빚    /{player.Debt,10}닢"));

        Build("", rows, BoardWidth, BoardHeight,
              new GameButton("특기", () => ShowSkills(player)), new GameButton("취소", Close));
    }

    /// <summary>
    /// 부하 하나의 판. 제독 판과 같은 틀이되 <b>세이브가 아는 것만</b> 적는다 —
    /// 직업·소지금·저금·빚·국적은 부하에게 없는 칸이라 아예 안 낸다.
    /// </summary>
    private PersonInfoDialog(Player.MateInfo who, string role, Portraits? faces)
    {
        var head = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        head.Children.Add(BlackLine($"  {who.Name}"));
        head.Children.Add(BlackLine($"  체  력/{who.Body,4}    명성치/{who.Fame,8}"));
        head.Children.Add(BlackLine($"  지  력/{who.Mind,4}    자리  /{role}"));
        head.Children.Add(BlackLine($"  무  력/{who.Might,4}"));
        head.Children.Add(BlackLine($"  매  력/{who.Charm,4}"));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (Face(faces?.TryGetBgra(who.Face, female: false)) is { } portrait)
            top.Children.Add(portrait);
        top.Children.Add(head);

        var rows = new StackPanel();
        rows.Children.Add(top);
        rows.Children.Add(Gap(14));
        rows.Children.Add(BlackLine($"  연령  /{who.Age,2}세"));

        Build("", rows, BoardWidth, BoardHeight, new GameButton("취소", Close));
    }

    /// <summary>이 판의 글 한 줄 — 검정 글씨다.</summary>
    private static GameUi.GameLabel BlackLine(string text) => Label(text, BlackInk);

    /// <summary>왼쪽 위 초상화. 얼굴을 못 읽었으면 안 세운다.</summary>
    private static UIElement? Face(Player player, Portraits? faces) =>
        // 서른여섯부터는 중년 얼굴로 바뀐다 — 다만 그 짝이 있을 때만이다
        // (PortraitAges). 더 넣은 얼굴처럼 짝이 없으면 젊은 얼굴 그대로 늙는다.
        Face(faces?.TryGetBgra(PortraitAges.At(player.Face, player.Age, false, faces),
                               female: false));

    /// <summary>이미 꺼내 둔 얼굴 점으로 초상화를 세운다.</summary>
    private static UIElement? Face(uint[]? px)
    {
        if (px == null) return null;

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width * FaceScale,
            Height = Portraits.Height * FaceScale,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        return new Border
        {
            BorderBrush = SteelEdge,
            BorderThickness = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Top,
            Child = image,
        };
    }

    /// <summary>「특기」 — 기술 열셋과 어학 열넷을 두 칸으로 늘어놓는다.</summary>
    private void ShowSkills(Player player) => SkillSheetDialog.Show(this, player);

    /// <summary>인물정보 판을 연다.</summary>
    /// <param name="gameDirectory">초상화를 읽을 게임 폴더. 없으면 얼굴 없이 뜬다.</param>
    public static void Show(Window owner, Player player, string gameDirectory = "")
    {
        var faces = Portraits.Open(gameDirectory);
        new PersonInfoDialog(player, faces) { Owner = owner }.ShowDialog();
    }

    /// <summary>부하 하나의 인물정보 판을 연다.</summary>
    /// <param name="role">그가 앉은 자리("부관" 따위). 판에 한 줄로 적는다.</param>
    public static void ShowMate(Window owner, Player.MateInfo who, string role,
                                string gameDirectory = "")
    {
        var faces = Portraits.Open(gameDirectory);
        new PersonInfoDialog(who, role, faces) { Owner = owner }.ShowDialog();
    }
}
