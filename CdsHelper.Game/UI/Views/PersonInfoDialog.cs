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
    /// <summary>판 크기. 게임 화면 비율에 맞춘다.</summary>
    private const double BoardWidth = 620, BoardHeight = 380;

    /// <summary>초상화를 몇 배로 그릴지.</summary>
    private const int FaceScale = 2;

    /// <summary>강청색 판. 게임 화면에서 뽑았다.</summary>
    private static readonly Brush Steel = Frozen(Color.FromRgb(92, 111, 147));

    private static readonly Brush SteelEdge = Frozen(Color.FromRgb(54, 65, 86));

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private PersonInfoDialog(Player player, Portraits? faces)
    {
        var head = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        head.Children.Add(Label($"  {player.Name}"));
        head.Children.Add(Label($"  체  력/{player.AbilityOf(Ability.Body),4}" +
                                $"    명성치/{player.Fame,8}"));
        head.Children.Add(Label($"  지  력/{player.AbilityOf(Ability.Mind),4}" +
                                $"    악명치/{player.Infamy,8}"));
        head.Children.Add(Label($"  무  력/{player.AbilityOf(Ability.Might),4}" +
                                $"    직업  /{player.Work.Name}"));
        head.Children.Add(Label($"  매  력/{player.AbilityOf(Ability.Charm),4}"));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (Face(player, faces) is { } portrait) top.Children.Add(portrait);
        top.Children.Add(head);

        var rows = new StackPanel();
        rows.Children.Add(top);
        rows.Children.Add(Gap(14));
        rows.Children.Add(Label($"  연령  /{player.Age,2}세          " +
                                $"생년월일/{player.BirthYear,4}년{player.BirthMonth,2}월{player.BirthDay,2}일"));
        rows.Children.Add(Label($"  별자리/{GameUi.Pad(player.Zodiac, 12)}혈액형  /{player.BloodName}"));
        rows.Children.Add(Label($"  국적  /{player.NationName}"));
        rows.Children.Add(Label($"  소지금/{player.Gold,10}닢"));
        rows.Children.Add(Label($"  저금  /{player.Savings,10}닢"));
        rows.Children.Add(Label($"  빚    /{player.Debt,10}닢"));

        Build("인물정보", rows, BoardWidth, BoardHeight,
              new GameButton("특기", () => ShowSkills(player)), new GameButton("취소", Close));
    }

    /// <summary>왼쪽 위 초상화. 얼굴을 못 읽었으면 안 세운다.</summary>
    private static UIElement? Face(Player player, Portraits? faces)
    {
        var px = faces?.TryGetBgra(player.Face, female: false);
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

    /// <summary>「특기」 — 기술 열셋과 어학 열넷을 늘어놓는다.</summary>
    private void ShowSkills(Player player)
    {
        var lines = new List<string>();
        foreach (string name in Skill.Names)
            lines.Add($"{GameUi.Pad(name, 20)}{player.LevelOf(name),3}");
        lines.Add("");
        foreach (string name in Skill.Languages)
            lines.Add($"{GameUi.Pad(name, 20)}{player.TongueOf(name),3}");

        HintListDialog.Show(this, lines, "특기", "아직 익힌 것이 없다.");
    }

    /// <summary>인물정보 판을 연다.</summary>
    /// <param name="gameDirectory">초상화를 읽을 게임 폴더. 없으면 얼굴 없이 뜬다.</param>
    public static void Show(Window owner, Player player, string gameDirectory = "")
    {
        var faces = gameDirectory.Length == 0 ? null : Portraits.Open(gameDirectory);
        new PersonInfoDialog(player, faces) { Owner = owner }.ShowDialog();
    }
}
