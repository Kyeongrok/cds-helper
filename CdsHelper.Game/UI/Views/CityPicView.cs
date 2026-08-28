using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Inn;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 입항한 도시의 그림(CITYCG.CDS)을 지도 한가운데에 띄운다. 게임처럼 건물(항구·조선소)을
/// 누르면 그 건물의 명령 창이 열린다.
/// </summary>
/// <remarks>
/// <see cref="PortDialog"/> 와 같은 수를 쓴다 — 창(HWND)을 따로 쓰므로 D3D 자식 창 위에
/// 제대로 뜬다(airspace 를 안 탄다). 그림은 400x320 도트 그림이라 정수배로만 늘린다.
///
/// 건물 자리·이름·가르치는 기능은 게임 EXE 의 건물 표(<see cref="CityBuildingTable"/>)에서
/// 그대로 온다. 표에 항구가 없는 도시라면 그림 아무 데나 눌러도 항구 명령 창이 열리게 해
/// 두었다 — 출항할 길은 어디서나 있어야 한다.
/// </remarks>
public sealed class CityPicView : Window
{
    /// <summary>건물 이름표와 명령 창을 얹는 자리. 그림과 같은 격자 칸에 둔다.</summary>
    private readonly Canvas _layer = new();

    /// <summary>건물 이름표들. 명령 창이 열리면 다 감춘다.</summary>
    private readonly List<Border> _tags = [];

    /// <summary>이 판. 게임 폴더 · 표 · 소리를 여기서 얻는다.</summary>
    private readonly Engine.Game _game;

    /// <summary>배를 사면 여기서 돈이 빠진다. 판이 든 것을 그대로 쓴다.</summary>
    private readonly Player _player;

    /// <summary>건물에 들어갈 때 곡을 바꾼다. 없으면(시험용) 아무것도 안 한다.</summary>
    private readonly BgmPlayer? _bgm;

    /// <summary>건물 표. 가르치는 기능을 이름으로 풀 때 쓴다.</summary>
    private readonly CityBuildingTable _table;

    /// <summary>후원자 자료(patrons.json). 한 번만 읽어 둔다.</summary>
    private static List<Patron>? _patrons;

    /// <summary>초상화(게임 자료). 설득할 때에야 연다.</summary>

    // 도서관 열람에 쓴다. 책 표를 못 읽었으면 열람 줄이 흐린 채로 남는다.
    private readonly BookTable? _library;
    private readonly string _gameDirectory;
    private readonly string _cityName;
    private readonly int _cityId;

    /// <summary>이 도시의 문화권("이슬람", "북유럽" …). 건물 사진을 고르는 데 쓴다.</summary>
    private readonly string _culture;

    /// <summary>그림 배율. 건물 사진도 같은 배율로 놓아야 자리가 맞는다.</summary>
    private readonly int _scale;

    /// <summary>
    /// 이 도시에서 도는 곡. 문화권마다 다르다 — 시설에서 나오면 이 곡으로 돌아간다.
    /// </summary>
    private readonly int _cityTrack;

    /// <summary>출항을 골랐는지. 창을 그냥 닫으면 false.</summary>
    public bool Sailed { get; private set; }

    /// <summary>펼치기 시작하는 크기(제 크기의 몇 곱).</summary>
    private const double OpenFrom = 0.1;

    /// <summary>다 펼쳐졌을 때의 자리와 크기.</summary>
    private double _openLeft, _openTop, _openWidth, _openHeight;

    /// <summary>
    /// 도시 그림이 가운데서 펼쳐지며 열린다.
    /// </summary>
    /// <remarks>
    /// 창(HWND)을 따로 쓰므로 창의 크기와 자리를 함께 움직인다 — 크기만 키우면 왼쪽 위가
    /// 붙박이라 한쪽으로 자라는 것처럼 보인다. 네 값을 같은 박자로 움직여야 가운데가 안 흔들린다.
    ///
    /// 끝나면 <b>애니메이션을 떼고</b> 값을 손으로 박는다. 안 떼면 그 값이 물려 있어
    /// 나중에 <see cref="GameUi.CarryOwnedWindows"/> 가 창을 옮기려 해도 먹히지 않는다.
    /// </remarks>
    private void PlayOpening(CityOpenEffect effect)
    {
        var span = TimeSpan.FromMilliseconds(effect switch
        {
            CityOpenEffect.Expand => 220,
            CityOpenEffect.Zoom => 320,     // 넘쳤다 돌아올 참이 있어야 해서 조금 길다
            _ => 250,
        });
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        DoubleAnimation To(double from, double to) =>
            new(from, to, span) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };

        // 끝나면 물려 있던 값을 떼고 손으로 박는다.
        void Settle()
        {
            foreach (var prop in new[] { WidthProperty, HeightProperty, LeftProperty, TopProperty, OpacityProperty })
                BeginAnimation(prop, null);
            Width = _openWidth;
            Height = _openHeight;
            Left = _openLeft;
            Top = _openTop;
            Opacity = 1;
        }

        DoubleAnimation lead;
        switch (effect)
        {
            case CityOpenEffect.Expand:
                double w0 = _openWidth * OpenFrom, h0 = _openHeight * OpenFrom;
                lead = To(w0, _openWidth);
                BeginAnimation(HeightProperty, To(h0, _openHeight));
                BeginAnimation(LeftProperty, To(_openLeft + (_openWidth - w0) / 2, _openLeft));
                BeginAnimation(TopProperty, To(_openTop + (_openHeight - h0) / 2, _openTop));
                lead.Completed += (_, _) => Settle();
                BeginAnimation(WidthProperty, lead);
                break;

            case CityOpenEffect.Slide:
                lead = To(SystemParameters.VirtualScreenWidth, _openLeft);
                lead.Completed += (_, _) => Settle();
                BeginAnimation(LeftProperty, lead);
                break;

            case CityOpenEffect.Fade:
                lead = To(0, 1);
                lead.Completed += (_, _) => Settle();
                BeginAnimation(OpacityProperty, lead);
                break;

            case CityOpenEffect.Zoom:
                ZoomIn(span);
                break;
        }
    }

    /// <summary>
    /// 파워포인트의 "확대/축소" 처럼 그림만 키운다 — 커지면서 흐림이 걷히고 끝에서 살짝 넘친다.
    /// </summary>
    /// <remarks>
    /// 창은 건드리지 않고 안의 그림에만 <see cref="ScaleTransform"/> 을 건다. 창의 크기·자리를
    /// 움직이지 않으므로 <see cref="GameUi.CarryOwnedWindows"/> 와도 부딪히지 않는다.
    /// 넘쳤다 돌아오는 맛은 <see cref="BackEase"/> 가 낸다.
    /// </remarks>
    /// <remarks>
    /// <b>깜빡이지 않게 하는 요령이 둘 있다.</b>
    ///
    /// 하나, 애니메이션을 <see cref="FillBehavior.HoldEnd"/> 로 둔다. <c>Stop</c> 으로 두면
    /// 끝나는 순간 값이 <i>처음 값</i>으로 되돌아간다 — 흐림처럼 짧게 끝나는 것을 <c>Stop</c>
    /// 으로 두면 중간에 그림이 한 번 사라졌다 돌아온다.
    ///
    /// 둘, 다 끝난 뒤 값을 먼저 박고 그 다음에 애니메이션을 뗀다. 차례가 바뀌면 떼는 순간
    /// 한 틱 동안 옛 값이 드러난다.
    ///
    /// 창 바탕도 건드리지 않는다. <c>AllowsTransparency</c> 가 켜진 창은 바탕을 갈면 통째로
    /// 다시 그려져 눈에 띈다 — 다 자란 그림이 창을 꽉 채우므로 비워 둔 채로 두어도 된다.
    /// </remarks>
    private void ZoomIn(TimeSpan span)
    {
        if (Content is not FrameworkElement box) return;

        var scale = new ScaleTransform(ZoomFrom, ZoomFrom);
        box.RenderTransformOrigin = new Point(0.5, 0.5);
        box.RenderTransform = scale;
        box.Opacity = 0;

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        DoubleAnimation Grow() => new(ZoomFrom, 1, span) { EasingFunction = ease };

        var lead = Grow();
        lead.Completed += (_, _) =>
        {
            // 값부터 박고 나서 뗀다.
            box.Opacity = 1;
            box.BeginAnimation(OpacityProperty, null);
            box.RenderTransform = Transform.Identity;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, lead);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Grow());
        // 흐림은 먼저 걷힌다 — 끝까지 끌면 넘쳤다 돌아오는 동안 반투명해 보인다.
        box.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(span.TotalMilliseconds * 0.6)));
    }

    /// <summary>확대/축소가 시작하는 배율.</summary>
    private const double ZoomFrom = 0.3;

    private CityPicView(Engine.Game game, string cityName, BitmapSource picture, int scale,
                          int cityId, Rect mapArea, int cityTrack, string culture)
    {
        // 판이 든 것을 그대로 든다 — 표를 여기서 따로 열지 않는다.
        _game = game;
        _player = game.Player;
        _bgm = game.Bgm;
        _table = game.Buildings!;      // 부르는 쪽에서 없으면 창을 아예 안 연다
        _library = game.Books;
        _gameDirectory = game.Directory;

        _culture = culture;
        _scale = scale;
        _cityTrack = cityTrack;
        _cityName = cityName;
        _cityId = cityId;

        Title = cityName;                 // 화면에는 안 나온다 — 창 목록에서만 쓴다
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;       // 그림에 가려 안 보인다

        // 창 크기는 그림 크기 그대로다. 제목 줄이 없어(WindowStyle.None) 테가 붙지 않는다.
        double fullW = CityPictures.Width * scale, fullH = CityPictures.Height * scale;

        // 지도를 덮는 남색 막은 이 창이 아니라 지도(D3D) 쪽에서 씌운다 — 그래야 이 그림을
        // 끌어 옮겨도 막이 따라오지 않는다. 게임도 막과 그림이 따로다.
        // 처음 자리는 게임처럼 지도 한가운데다(옮기는 것은 손으로).
        if (mapArea.Width > 0 && mapArea.Height > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 다 펼쳐졌을 때의 자리. 펼치는 동안에는 이 한가운데를 축으로 커진다.
            _openLeft = mapArea.X + (mapArea.Width - fullW) / 2;
            _openTop = mapArea.Y + (mapArea.Height - fullH) / 2;
            _openWidth = fullW;
            _openHeight = fullH;

            // 효과는 개발 창에서 고른다. 크기를 움직이려면 SizeToContent 를 꺼야 한다.
            SizeToContent = SizeToContent.Manual;
            Width = fullW;
            Height = fullH;
            Left = _openLeft;
            Top = _openTop;

            var effect = GameSettings.CityOpenEffect;
            if (effect == CityOpenEffect.Expand)
            {
                Width = fullW * OpenFrom;
                Height = fullH * OpenFrom;
                Left = _openLeft + (fullW - Width) / 2;
                Top = _openTop + (fullH - Height) / 2;
            }
            else if (effect == CityOpenEffect.Slide)
            {
                Left = SystemParameters.VirtualScreenWidth;      // 화면 오른쪽 바깥
            }
            else if (effect == CityOpenEffect.Fade)
            {
                AllowsTransparency = true;   // 이것을 켜야 Opacity 가 창에 먹는다
                Opacity = 0;
            }
            else if (effect == CityOpenEffect.Zoom)
            {
                // 창은 제 크기 그대로 두고 그림만 키운다. 창 바탕이 검으면 다 자라기 전에
                // 검은 네모가 먼저 보이므로, 바탕을 비우고 그림만 뜨게 한다.
                AllowsTransparency = true;
                Background = Brushes.Transparent;
            }

            if (effect != CityOpenEffect.None) Loaded += (_, _) => PlayOpening(effect);
        }
        else
        {
            // 지도 자리를 모르면 owner 한가운데에 제 크기로 띄운다(펼치지 않는다).
            //
            // 크기를 <b>손으로 박는다</b>. 예전에는 SizeToContent 에 맡겼는데, 이 창의 속은
            // Viewbox(Stretch.Fill) 라 창에 맞춰 늘어난다 — 크기를 속에서 재고 속을 다시
            // 창에 맞추는 두 규칙이 서로를 밀어, 창이 화면만 하게 부풀어 오르는 일이 있었다
            // (도시 그림이 전체 화면으로 뜨던 것이 이것이다).
            SizeToContent = SizeToContent.Manual;
            Width = fullW;
            Height = fullH;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // 펼침 효과는 안 쓰지만, 뒤에 무엇이 이 값을 보더라도 창 크기와 어긋나지 않게 둔다.
            _openWidth = fullW;
            _openHeight = fullH;
        }

        var image = new Image
        {
            Source = picture,
            Width = CityPictures.Width * scale,
            Height = CityPictures.Height * scale,
            Stretch = Stretch.Fill,
        };
        // 도트 그림이라 늘릴 때 섞으면 뭉개진다 — 게임 화면처럼 각을 살린다.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        var picBox = new Grid
        {
            Width = image.Width,
            Height = image.Height,
            Children = { image, _layer },
        };

        // 게임 건물 표에 적힌 그대로 얹는다 — 그 도시에 있는 건물만, 게임이 쓰는 자리에.
        bool harborPlaced = false;
        foreach (var building in _table.InCity(cityId))
        {
            AddSpot(building, scale);
            if (building.Kind == "항구") harborPlaced = true;
        }

        // 표에 항구가 없는 도시는 아무 데나 눌러도 항구 명령 창이 열린다(건물 판이 먼저 먹는다).
        if (!harborPlaced)
        {
            var harbor = Facility.For("항구");
            picBox.Cursor = Cursors.Hand;
            picBox.MouseLeftButtonUp += (_, _) =>
                ShowMenu(() => BuildMenu(harbor, harbor.Name, 0, harbor.Name), harbor.BgmTrack);
        }

        // 게임 화면에는 제목 줄도 안내 줄도 없다. 그림 한 장이 곧 창이다.
        // 펼치는 동안 창이 작아지므로 그림도 같이 줄어야 한다 — Viewbox 가 창에 맞춰 준다.
        // 다 펼쳐지면 창과 그림이 같은 크기라 배율이 1 이 되어, 건물 누르는 자리도 그대로 맞는다.
        Content = new Viewbox { Child = picBox, Stretch = Stretch.Fill };

        // 제목 줄이 없어도 옮길 수는 있어야 한다 — 그림의 아무 데나 잡으면 끌린다.
        // 건물 판과 명령 창은 누르는 자리라 제 몫으로 삼키므로 여기까지 오지 않는다.
        // 항구를 못 찾은 그림은 그림 전체가 누르는 자리라 끌기를 달지 않는다.
        if (harborPlaced)
            picBox.MouseLeftButtonDown += (_, _) =>
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
            };

        // 그림을 옮기면 옆에 붙은 커맨드 창도 같이 옮긴다. 함대 창이 옮겨져 이 그림이
        // 끌려갈 때에도 같은 길로 이어진다.
        GameUi.CarryOwnedWindows(this);

        // 오른쪽 단추는 게임처럼 도시 커맨드 창을 연다. 창을 닫는 것은 ESC 다.
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCityMenu(cityName, ToScreen(e.GetPosition(this)));
        };
        Closed += (_, _) => CloseCityMenu();   // 그림 창을 닫으면 커맨드 창도 같이 닫는다
    }

    /// <summary>
    /// 건물 하나를 누를 수 있게 한다. 커서를 올리면 이름표가 밑에 뜨고, 누르면 명령 창이 열린다.
    /// </summary>
    private void AddSpot(CityBuildingTable.Building building, int scale)
    {
        var facility = Facility.For(building.Kind);
        var tag = GameUi.NameTag(building.Kind);   // 지도 이름표에는 종류가 뜬다("술집")
        _layer.Children.Add(tag);
        _tags.Add(tag);

        // 표의 상자는 96x80 이라 건물끼리 겹친다. 가운데는 그대로 두고 누를 자리만 좁힌다.
        var a = new Rect(building.CenterX - HitWidth / 2.0, building.CenterY - HitHeight / 2.0,
                         HitWidth, HitHeight);
        var spot = new Border
        {
            Width = a.Width * scale,
            Height = a.Height * scale,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        Canvas.SetLeft(spot, a.X * scale);
        Canvas.SetTop(spot, a.Y * scale);
        spot.MouseEnter += (_, _) => ShowTag(tag, a, scale);
        spot.MouseLeave += (_, _) => tag.Visibility = Visibility.Collapsed;
        // 건물을 누른 것은 여기서 삼킨다 — 안 그러면 그림 끌기가 먼저 걸려 메뉴가 안 열린다.
        spot.MouseLeftButtonDown += (_, e) => e.Handled = true;
        spot.MouseLeftButtonUp += (_, e) => { e.Handled = true; Enter(building); };
        _layer.Children.Add(spot);
    }

    /// <summary>
    /// 건물 하나에 들어간다. 그림에서 눌러도, 커맨드의 "맵 포인트에 들어간다" 로 골라도
    /// 이 길을 지난다.
    /// </summary>
    private void Enter(CityBuildingTable.Building building)
    {
        var facility = Facility.For(building.Kind);
        if (!PassFameGate(building, facility)) return;   // 문 앞에서 돌아섰다
        Greet(facility);
        ShowPhoto(facility.Kind, building.Code);
        // 명령 창 제목은 건물 이름이다 — 게임도 "베렌의 탑", "홍경정" 으로 낸다.
        ShowMenu(() => BuildMenu(facility, building.Name, building.TeachMask, building.Kind),
                 facility.BgmTrack);
    }

    /// <summary>
    /// 도서관 사서의 얼굴 번호(MALE.CDS). 표에 적힌 것을 읽은 것이 아니라 게임 화면의 얼굴을
    /// 초상화 414장과 맞대어 찾았다 — 집사 얼굴(<see cref="SponsorTable.StewardFace"/>)을
    /// 찾은 것과 같은 길이다.
    /// </summary>
    private const int LibrarianFace = 161;

    /// <summary>한 장이 머무는 참. 다섯 장을 이으면 1.1초쯤 된다.</summary>
    private static readonly TimeSpan FrameSpan = TimeSpan.FromMilliseconds(220);

    private bool _playing;

    /// <summary>
    /// 후원자가 앉은 건물의 <b>명성 관문</b>. 통과하면 true, 문 앞에서 돌아섰으면 false 다.
    /// </summary>
    /// <remarks>
    /// 명성이 모자라면 설득이 엎어지고 <b>명령 창이 아예 안 열린다</b> — 게임도 그 자리에서
    /// 도시 그림으로 돌아가고 집사가 돌려보내는 소리(효과음 파트 1)를 낸다.
    ///
    /// <b>왕궁과 교회는 뺀다.</b> 그 둘은 후원자를 못 만나도 건물 자체에는 들어간다 —
    /// 왕궁에는 알현·의뢰 같은 제 줄이 있고 교회는 수련하는 데다. 막히는 것은 후원자만
    /// 앉아 있는 곳(총독부·상관·저택 따위)이다.
    /// </remarks>
    private bool PassFameGate(CityBuildingTable.Building building, Facility facility)
    {
        var patron = PatronAt(building.Kind);
        if (patron == null) return true;

        bool passed = _player.Fame >= patron.Fame;
        PlayFameCheck(passed);
        if (passed) return true;

        if (facility.Kind is FacilityKind.Palace or FacilityKind.Church) return true;

        _game.Sfx?.Play(SoundBank.TurnedAwayPart);
        return false;
    }

    /// <summary>
    /// 후원자가 앉은 건물에 들어설 때 도는 <b>설득 애니메이션</b>(MPEFFECT 5번).
    /// </summary>
    /// <remarks>
    /// 게임은 이것을 명성 관문 안에서 돌린다 — <c>0x0044E740</c> 이 후원자의 필요 명성과 내
    /// 명성을 견주고, 그 결과를 그대로 애니메이션의 인자로 넘긴다(우리도 <paramref name="passed"/>
    /// 로 받는다). 그림 넉 장이 곧 결말까지
    /// 담고 있어서, <b>통과면 청을 들어주는 셋째 장에서 멈추고 모자라면 엎어지는 끝 장까지</b>
    /// 간다. 자세한 것은 볼트 <c>22.분석-애니메이션(MPEFFECT·EVANIME)</c> 참고.
    ///
    /// 계약을 이미 맺은 뒤에는 게임도 관문을 건너뛰므로 여기서도 안 돈다 — 그 자리는
    /// <see cref="Patron"/> 쪽에 아직 없어 후원자가 앉아 있기만 하면 돈다.
    /// </remarks>
    private void PlayFameCheck(bool passed)
    {
        if (_playing) return;                       // 도는 동안 또 누르면 겹친다

        var effects = _game.Effects;
        if (effects == null) return;

        double side = EffectAnim.Size * _scale;
        var image = new Image { Width = side, Height = side, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        // 그림 한가운데에 놓는다. 예전에는 누른 건물 위에 놓았는데, 건물이 구석에 있으면
        // 애니메이션도 구석으로 밀려 났다 — 게임은 늘 화면 가운데에서 돈다.
        Canvas.SetLeft(image, (CityPictures.Width * _scale - side) / 2);
        Canvas.SetTop(image, (CityPictures.Height * _scale - side) / 2);
        Panel.SetZIndex(image, 30);
        _layer.Children.Add(image);

        // 청하는 두 장을 두 번 흔들고 결말 장을 낸다 — 넉 장을 한 번씩만 넘기면 눈에
        // 들어오기 전에 지나간다. 결말은 명성이 되면 받아 드는 장, 모자라면 엎어지는 장이다.
        int[] order = [.. Plead, passed ? Granted : Refused];

        // 같은 장이 두 번 나오므로 한 번만 풀어 둔다.
        var art = new BitmapSource?[EffectAnim.FrameCount];

        _playing = true;
        try
        {
            foreach (int f in order)
            {
                if (art[f] == null)
                {
                    var bgra = effects.TryGetBgra(EffectAnim.Persuade, f);
                    if (bgra == null) continue;

                    var bmp = BitmapSource.Create(EffectAnim.Size, EffectAnim.Size, 96, 96,
                                                  PixelFormats.Bgra32, null, bgra, EffectAnim.Size * 4);
                    bmp.Freeze();
                    art[f] = bmp;
                }
                image.Source = art[f];
                Wait(FrameSpan);
            }
        }
        finally
        {
            _layer.Children.Remove(image);
            _playing = false;
        }
    }

    /// <summary>
    /// 청하는 두 장. 이것을 두 번 되풀이해 흔든 뒤 결말 장으로 넘어간다(모두 0부터 센다).
    /// </summary>
    private static readonly int[] Plead = [0, 1, 0, 1];

    /// <summary>결말 장 — 받아 드는 셋째 장과 엎어지는 넷째 장.</summary>
    private const int Granted = 2, Refused = 3;

    /// <summary>
    /// 그동안 화면이 멎지 않게 하면서 한 참 기다린다. 애니메이션을 다 돌리고 나서 명령 창을
    /// 열어야 하는데, <c>Thread.Sleep</c> 으로 막으면 그림이 아예 안 바뀐다.
    /// </summary>
    private static void Wait(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(span, DispatcherPriority.Normal,
                                        (_, _) => frame.Continue = false,
                                        Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    /// <summary>
    /// 시설마다 말을 거는 사람의 얼굴 번호.
    /// </summary>
    /// <remarks>
    /// <b>눈으로 맞출 것이 아니라 표에 있었다.</b> 시설 객체는 화자를
    /// <c>0x004A2500</c> 에서 <c>0x00477C20(건물종류)</c> 으로 한 번에 정하고, 그 속이
    /// <c>0x0056823C</c> 를 <c>[건물종류 * 13 + 문화권]</c> 으로 읽는다.
    /// <code>
    ///   종류 2  취차    229  (문화권 무관)
    ///   종류 3  교회    292  (문화권 무관)
    ///   종류 8  도서관  161 · 311 · 312 · 370 · 349 · 389 · 339 …
    ///   종류 9  조합     44 · 319 · 351 · 368 · 388 …
    /// </code>
    /// 취차 229 · 사서 161 · 조합장 44 는 화면과 맞대어 찾아 둔 값인데 표와 그대로 맞았다.
    /// <b>교회는 292</b> 다 — 십자가를 건 젊은 수도사다.
    ///
    /// 문화권 칸은 아직 안 쓴다. 우리가 내는 건물은 유럽 것뿐이고, 교회·취차는 어느
    /// 문화권에서나 같은 얼굴이다.
    /// </remarks>
    private const int InstructorFace = 44, ChurchFace = 292;

    /// <summary>
    /// "수련" — 맡은 사람이 먼저 묻고, 창을 닫을 때 아무것도 안 배웠으면 한마디 한다.
    /// </summary>
    /// <remarks>
    /// <b>건물마다 사람도 말도 다르다.</b> 게임은 문구를 <c>0x00490D90(교회, 조합, 그밖)</c>
    /// 으로 고른다 — 건물 종류로 셋 중 하나를 집는 갈래표다(<c>0x00490DDC</c>).
    /// <code>
    ///   0x0055A7E8  교회    "주의 배움의 터전에 잘 오셨습니다. 어떤 학문, 기능을 배우고 싶습니까?"
    ///   0x0055A830  조합    "기술을 습득하고 싶나?"
    ///   0x0055A848  그 밖   "가르쳐 드릴 것은 한가지 밖에 없습니다만."   (배울 것이 하나일 때)
    ///   0x0055A878          "무엇을 배우고 싶은가?"
    /// </code>
    /// 우리가 "수련" 을 내는 곳은 교회와 조합 둘이라 그 둘만 갈랐다.
    /// </remarks>
    private void Teach(uint teachMask, FacilityKind kind)
    {
        bool church = kind == FacilityKind.Church;
        var face = _game.Faces?.TryGetBgra(church ? ChurchFace : InstructorFace, female: false);

        TalkDialog.Say(this, face, "", church
            ? "주의 배움의 터전에 잘 오셨습니다. 어떤 학문, 기능을 배우고 싶습니까?"
            : "기술을 습득하고 싶나?");

        if (!SkillLearnDialog.Show(this, _player, _table.Teaches(teachMask)))
            TalkDialog.Say(this, face, "", church
                ? "죄송하지만, 여기서는 수련이 불가능합니다."
                : "용건이 없다면 오지 말게!");
    }

    /// <summary>
    /// 건물에 들어설 때 사람이 먼저 말을 거는 곳이 있다. 도서관 사서가 그렇다 —
    /// 게임도 서가를 보여 주기 전에 이 한마디를 내고 확인을 받는다.
    /// </summary>
    /// <remarks>
    /// 대답은 받지 않는다. 게임 화면에도 단추가 "확인" 하나뿐이라 물음이 아니라 인사다 —
    /// 확인을 누르면 도서관 명령 창(열람·나온다)이 열린다.
    /// </remarks>
    private void Greet(Facility facility)
    {
        if (facility.Kind == FacilityKind.Library)
        {
            TalkDialog.Say(this, _game.Faces?.TryGetBgra(LibrarianFace, female: false),
                           "", "책을 찾고 계십니까?");
            return;
        }

        // 항구에서는 부관이 먼저 말을 건다. 자리가 비었으면 아무도 안 나온다.
        if (facility.Kind == FacilityKind.Harbor) GreetMate();
    }

    /// <summary>
    /// 항구에 들어설 때 부관이 건네는 한마디. 부관 자리가 비었으면 아무 일도 없다.
    /// </summary>
    /// <remarks>
    /// 부하는 이름만 들고 있어(<see cref="Player.Mates"/>) 얼굴은 세이브 인물표에서 되짚는다
    /// (<see cref="TavernRoster.Find"/>). 못 찾으면 얼굴 없이 말만 낸다 — 그림이 없다고
    /// 말까지 막을 일은 아니다. 게임 화면에도 단추가 "확인" 하나뿐이라 물음이 아니라 인사다.
    /// </remarks>
    private void GreetMate()
    {
        string mate = _player.MateAt(MateSlot);
        if (mate.Length == 0) return;

        // 얼굴도 우리가 적어 둔 자료에서 꺼낸다 — 게임 세이브가 없어도 부관은 말을 건다.
        TalkDialog.Say(this, MateFace(mate), "", "제독, 바다에 나가시겠습니까?");
    }

    /// <summary>
    /// 그 부하의 얼굴. 적어 둔 자료의 얼굴번호로 꺼낸다 — 없으면 게임 세이브의 인물표를
    /// 한 번 뒤져 채운다(<see cref="ShowMate"/> 와 같은 길이다).
    /// </summary>
    private uint[]? MateFace(string name)
    {
        var who = _player.MateInfoOf(name);
        if (who == null && Roster()?.Find(name) is { } person)
        {
            who = MateInfoOf(person);
            _player.RememberMate(who.Value);
        }
        return who is { } mate && mate.Face is >= 0 and < 0xFFFF
            ? _game.Faces?.TryGetBgra(mate.Face, female: false)
            : null;
    }

    /// <summary>부관이 앉는 자리. <see cref="Player.MateRoles"/> 의 첫 자리다.</summary>
    private const int MateSlot = 0;

    /// <summary>지금 떠 있는 건물 사진. 명령 창을 닫으면 같이 걷는다.</summary>
    private BuildingPhotoWindow? _photoWindow;

    /// <summary>
    /// 게임 640x480 화면에서 타원 사진이 앉는 자리. 도시 그림은 (0,0)~(400,320) 이고
    /// 사진은 (320,240) 부터라 <b>오른쪽 아래 모서리만</b> 겹친다.
    /// </summary>
    private const int PhotoLeft = 320, PhotoTop = 240;

    /// <summary>
    /// 그 건물의 타원 사진을 오른쪽 아래에 띄운다(<see cref="BuildingPhoto"/>).
    /// 술집·여관이면 사진 앞에 손님도 세운다. 사진을 못 구하면 조용히 넘어간다 —
    /// 사진은 덤이고 명령 창은 이미 열린다.
    /// </summary>
    private void ShowPhoto(FacilityKind kind, int buildingCode)
    {
        _photoWindow?.Close();
        _photoWindow = null;

        var photos = _game.Photos;
        if (photos == null) return;

        int k = photos.Pick(_culture, buildingCode);
        if (k < 0) return;

        _photoWindow = BuildingPhotoWindow.Show(this, photos.TryGetBgra(k), GuestArt(kind), _scale,
                                                new Point(Left + PhotoLeft * _scale,
                                                          Top + PhotoTop * _scale));
    }

    /// <summary>
    /// 사진 앞에 세울 손님들. 술집·여관이 아니거나 그림을 못 읽었으면 빈 목록이다.
    /// </summary>
    /// <remarks>
    /// 세이브에 그 도시 그 건물로 적힌 인물을 자리에 앉히고(<see cref="TavernRoster"/>),
    /// 남는 자리는 지나가는 손님으로 채운다. 이름표는 인물이면 그 이름, 아니면 성별이다.
    /// </remarks>
    private IReadOnlyList<BuildingPhotoWindow.GuestArt> GuestArt(FacilityKind kind)
    {
        if (kind is not (FacilityKind.Tavern or FacilityKind.Inn)) return [];

        var book = _game.Guests;
        if (book == null) return [];

        byte building = kind == FacilityKind.Tavern ? TavernRoster.Tavern : TavernRoster.Inn;
        var people = Roster()?.At(_cityId, building) ?? [];
        var keys = new List<int>(people.Count);
        foreach (var p in people) keys.Add(p.Index);

        var art = new List<BuildingPhotoWindow.GuestArt>(TavernGuests.MaxOnScreen);
        foreach (var seat in book.Seat(_culture, _cityId, keys))
        {
            var bgra = book.TryGetBgra(seat.Art);
            if (bgra == null) continue;

            if (seat.Person < 0)
            {
                string label = seat.Art.Female ? "여" : "남";
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height, label,
                            () => MeetStranger(seat.Art.Female)));
            }
            else
            {
                // 낯을 트기 전에는 이름이 안 보인다 — 이름표도 "남"·"여" 다.
                var who = people[seat.Person];
                bool known = Known(who);
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height,
                            known ? who.Name : seat.Art.Female ? "여" : "남",
                            () => MeetPerson(who, seat.Art.Female)));
            }
        }
        return art;
    }

    private TavernRoster? _roster;
    private bool _rosterTried;

    /// <summary>
    /// 게임 세이브에서 술집·여관에 앉은 인물을 한 번만 읽는다. 못 읽으면 인물 없이
    /// 지나가는 손님만 선다.
    /// </summary>
    private TavernRoster? Roster()
    {
        if (_roster != null || _rosterTried) return _roster;
        _rosterTried = true;

        var path = AppSettings.LastSaveFilePath;
        if (string.IsNullOrEmpty(path)) return null;

        _roster = TavernRoster.Open(path);
        if (_roster == null)
            System.Diagnostics.Debug.WriteLine($"[City] 술집 인물 없음: {TavernRoster.LastError}");
        return _roster;
    }

    /// <summary>
    /// 이름 없는 손님을 눌렀을 때. 게임 문구를 그대로 옮겼다(<c>0x0054AC40</c>·<c>0x0054AB98</c>).
    /// </summary>
    private void MeetStranger(bool female)
    {
        string seen = female ? "아름다운 여성이 있다" : "술을 마시고 있는 남자가 있다";
        if (TalkDialog.Ask(this, null, "", seen, "한잔 산다", "무시한다") == 0) BuyDrink();
    }

    /// <summary>
    /// 그 사람과 낯을 텄는지. 세이브에 고용 가능(2)·고용 중(3)으로 적혀 있으면 이미 아는
    /// 사이로 보고, 그 밖에는 술집에서 한잔 사야 이름을 알게 된다.
    /// </summary>
    private bool Known(TavernRoster.Person who) =>
        who.Hire >= TavernRoster.Hireable || _player.HasMet(who.Name);

    /// <summary>
    /// 인물을 눌렀을 때. <b>낯을 텄는지에 따라 두 갈래</b>다 — 게임도 인물 객체의
    /// <c>vtbl[0x34]</c> 하나로 이렇게 가른다(<c>0x0042F3D0</c>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>모르는 사람</b> — "술을 마시고 있는 남자가 있다"(<c>0x0054AC40</c> 자리에
    ///         "남자"·"여" 이름표 <c>0x005609C8</c> 를 끼운 것)로 부르고 <b>한잔 산다</b>만 된다.
    ///         한잔 사면 낯을 튼다.</item>
    ///   <item><b>아는 사람</b> — "[이름]이 있다"(<c>0x0054AC20</c>)로 부르고
    ///         <b>말을 건다</b>가 뜬다. 말을 걸면 용건을 묻는다.</item>
    /// </list>
    /// 말을 걸었을 때 뜰 줄은 게임이 인물 갈래(<c>+0xE8</c>)로 고르는데(<c>0x004A4DE0</c>),
    /// 우리는 세이브의 고용상태(1 대화만 · 2 고용가능 · 3 고용중)로 대신한다.
    /// </remarks>
    private void MeetPerson(TavernRoster.Person who, bool female)
    {
        var face = FaceOf(who);

        if (!Known(who))
        {
            string seen = female ? "아름다운 여성이 있다" : "술을 마시고 있는 남자가 있다";
            if (TalkDialog.Ask(this, null, "", seen, "한잔 산다", "무시한다") != 0) return;
            if (BuyDrink() && _player.Meet(who.Name))
                TalkDialog.Say(this, face, "", $"고맙네. 나는 {who.Name}. 잘 부탁하네.");
            return;
        }

        if (TalkDialog.Ask(this, face, "", $"[{who.Name}]{Subject(who.Name)} 있다",
                           "말을 건다", "무시한다") != 0) return;

        bool hireable = who.Hire == TavernRoster.Hireable;
        string[] choices = hireable
            ? ["정보를 듣는다", "부하로 고용한다", "떠난다"]
            : ["정보를 듣는다", "떠난다"];

        switch (TalkDialog.Ask(this, face, "", "무슨 용건인가?", choices))
        {
            case 0:
                // 게임은 여기서 발견물 실마리를 주는데 우리는 아직 그 자리를 못 흉내낸다.
                // 대신 세이브에 적힌 그 사람 됨됨이를 이른다. 나이는 값이 이상한 칸이
                // 더러 있어(등장 전 인물) 말이 될 때만 말한다.
                TalkDialog.Say(this, face, "", who.Age is > 0 and < 120
                                   ? $"나 말인가. {who.Name}. 올해 {who.Age}이네. 이름값은 {who.Fame} 쯤 하지."
                                   : $"나 말인가. {who.Name}. 이름값은 {who.Fame} 쯤 하지.");
                break;
            case 1 when hireable:
                Hire(who, face);
                break;
        }
    }

    /// <summary>
    /// 부하로 삼는다. 게임은 명성이 그 사람에 못 미치면 물린다 —
    /// <see cref="Support.Local.Models.CharacterData.CanRecruit"/> 와 같은 잣대다.
    /// </summary>
    private void Hire(TavernRoster.Person who, uint[]? face)
    {
        if (_player.HasMate(who.Name))
        {
            TalkDialog.Say(this, face, "", $"{who.Name}{Subject(who.Name)} 이미 자네 사람이 아닌가.");
            return;
        }

        if (_player.MateCount >= Player.MaxMates)
        {
            TalkDialog.Say(this, face, "", "자네 배에는 이미 사람이 넘치지 않는가.");
            return;
        }

        if (_player.Fame < who.Fame)
        {
            TalkDialog.Say(this, face, "", "자네 이름은 들어 본 적이 없군. 더 이름을 알리고 오게.");
            return;
        }

        int slot = AskMateSlot(face);
        if (slot < 0) return;                       // 물렀다

        _player.SetMate(slot, who.Name);
        // 됨됨이를 지금 베껴 둔다 — 나중에 인물정보를 낼 때 게임 세이브를 다시 안 뒤지게.
        _player.RememberMate(MateInfoOf(who));
        TalkDialog.Say(this, face, "",
                       $"좋네. {Player.MateRoles[slot]}(으)로서 자네와 함께 가지.");
    }

    /// <summary>
    /// 어느 자리에 앉힐지 묻는다. <b>빈 자리만</b> 내놓는다 — 찬 자리를 고르게 두면 있던
    /// 사람을 말없이 내보내게 된다. 물렀으면 -1.
    /// </summary>
    /// <remarks>
    /// 자리는 <see cref="Player.MateRoles"/> — 부관·항해사·측량사·통역 넷이다. 앉힌 뒤에
    /// 자리를 바꾸는 것은 여관·술집의 "부하편성" 창(<see cref="MateRosterDialog"/>)이 맡는다.
    ///
    /// 고르는 단추는 폭이 정해져 있어(<c>110</c>) 자리 이름만 넣는다. 누가 어디 앉아 있는지는
    /// 부하편성 창에서 본다.
    /// </remarks>
    private int AskMateSlot(uint[]? face)
    {
        var open = new List<int>();
        for (int i = 0; i < Player.MaxMates; i++)
            if (_player.MateAt(i).Length == 0) open.Add(i);
        if (open.Count == 0) return -1;

        var choices = new string[open.Count + 1];
        for (int i = 0; i < open.Count; i++) choices[i] = Player.MateRoles[open[i]];
        choices[^1] = "그만둔다";

        int picked = TalkDialog.Ask(this, face, "", "어느 자리에 앉히겠나?", choices);
        return picked >= 0 && picked < open.Count ? open[picked] : -1;
    }

    /// <summary>세이브에서 읽은 인물을 우리 부하 자료로 옮긴다.</summary>
    private static Player.MateInfo MateInfoOf(TavernRoster.Person who) =>
        new(who.Name, who.FaceCode, who.Fame, who.Age,
            who.Body, who.Mind, who.Might, who.Charm, who.Luck);

    /// <summary>이름 뒤에 붙는 주격 조사. 받침이 있으면 "이", 없으면 "가".</summary>
    /// <remarks>
    /// 게임도 조사를 따로 끼워 넣는다 — "%s%s 있다"(<c>0x0054ABF0</c>) 의 두 번째 자리다.
    /// </remarks>
    private static string Subject(string name)
    {
        if (name.Length == 0) return "가";
        char last = name[^1];
        if (last is < '가' or > '힣') return "가";       // 한글이 아니면 그냥 둔다
        return (last - '가') % 28 == 0 ? "가" : "이";
    }

    /// <summary>
    /// 그 사람의 얼굴. 세이브의 얼굴코드가 <c>0xFFFF</c> 면 얼굴이 없다는 뜻이라 null 이다.
    /// </summary>
    private uint[]? FaceOf(TavernRoster.Person who) =>
        who.FaceCode is >= 0 and < 0xFFFF
            ? _game.Faces?.TryGetBgra(who.FaceCode, female: false)
            : null;

    /// <summary>한잔 사는 값. 게임은 도시마다 파는 술이 달라 값도 다른데 아직 그 표를 안 읽는다.</summary>
    private const int DrinkPrice = 10;

    /// <summary>한잔 산다. 정말 샀으면 true — 낯을 트는 것은 부르는 쪽이 판단한다.</summary>
    private bool BuyDrink()
    {
        if (_player.Gold < DrinkPrice)
        {
            NoticeDialog.Show(this, "돈 먼저 지불하게.");
            return false;
        }
        _player.SetGold(_player.Gold - DrinkPrice);
        NoticeDialog.Show(this, $"금화 {DrinkPrice}닢으로 한잔 샀다.");
        return true;
    }

    /// <summary>누를 자리의 크기(그림 점). 건물끼리 겹치지 않을 만큼만 잡았다.</summary>
    private const int HitWidth = 44, HitHeight = 38;

    /// <summary>
    /// 건물 <b>위에</b> 이름표를 띄운다. 게임은 밑에 붙이는데, 우리 커서는 이름표를 덮고
    /// 앉아 글자가 가린다 — 커서가 누르는 자리는 늘 이름표 아래가 되게 위로 올렸다.
    /// 그림 꼭대기에 붙은 건물이라 위로 넘칠 때만 밑으로 돌린다.
    /// </summary>
    private void ShowTag(Border tag, Rect area, int scale)
    {
        tag.Visibility = Visibility.Visible;
        tag.UpdateLayout();
        double w = tag.ActualWidth > 0 ? tag.ActualWidth : 52;
        double h = tag.ActualHeight > 0 ? tag.ActualHeight : UiSprites.BandHeight;
        double x = (area.X + area.Width / 2) * scale - w / 2;
        Canvas.SetLeft(tag, Math.Clamp(x, 0, Math.Max(0, CityPictures.Width * scale - w)));

        // 게임은 이름표를 건물 <b>아래</b>에 붙인다. 그림 밑단을 넘칠 때만 위로 올린다.
        double below = (area.Y + area.Height) * scale + 2;
        double bottom = CityPictures.Height * scale;
        Canvas.SetTop(tag, below + h <= bottom ? below : Math.Max(0, area.Y * scale - h - 2));
    }

    /// <summary>시설 명령 창. 띄우고 겹치고 되돌아가는 것은 이쪽이 맡는다.</summary>
    private GameMenuHost? _menu;

    private GameMenuHost Menu
    {
        get
        {
            if (_menu != null) return _menu;
            _menu = new GameMenuHost(this);
            // 창을 그냥 닫아도(줄·ESC·오른쪽 단추) 도시 곡으로 돌아가고 사진도 걷힌다.
            _menu.Closed += () =>
            {
                _photoWindow?.Close();
                _photoWindow = null;
                _bgm?.Play(_cityTrack);
            };
            return _menu;
        }
    }

    /// <summary>
    /// 명령 창을 연다. 건물마다 도는 곡이 다르면 <paramref name="track"/> 으로 준다 —
    /// 안 주면 도시 곡으로 돌아간다(다른 건물로 옮겨 갈 때 술집 곡이 따라오지 않게).
    /// </summary>
    /// <remarks>
    /// 그림 안에 그리지 않는다. 자택처럼 줄이 열한 개나 되는 시설은 그림을 통째로 덮어 버려
    /// 도시가 안 보인다 — 도시 커맨드 창과 같은 까닭으로 제 창을 띄운다.
    ///
    /// <b>자리는 그림 한가운데다.</b> 게임이 그렇게 낸다 — 누른 건물과 상관없이 늘
    /// <c>그리는 영역의 원점 + 크기/2</c> 다(<c>0x00469E80</c>, 볼트
    /// <c>15.분석-시설 화면 엔진</c>). 예전에는 그림 오른쪽에 붙여 냈다.
    /// </remarks>
    private void ShowMenu(Func<GameMenu> build, int? track = null)
    {
        foreach (var tag in _tags) tag.Visibility = Visibility.Collapsed;
        Menu.Open(build);
        _bgm?.Play(track ?? _cityTrack);
    }

    /// <summary>명령 창을 닫고 도시로 돌아간다 — 곡도 도시 것으로 되돌린다(창의 Closed 가 맡는다).</summary>
    private void CloseMenu() => _menu?.Close();

    /// <summary>
    /// 시설에서 "기능" 을 골랐을 때 뜨는 창. 제목이 없고 줄만 넷이다 —
    /// 게임 재개를 고르면 하던 화면으로 돌아간다. 저장·로드는 아직 흉내내지 않는다.
    /// </summary>
    /// <summary>
    /// 함대편성 창. 게임처럼 제목 없이 줄만 쌓고, 마지막 줄만 회녹색 띠가 된다.
    /// "편성 종료" 를 누르면 항구 창으로 되돌아간다 — 창을 닫는 것이 아니라 담긴 것만 갈린다.
    /// </summary>
    private GameMenu FleetMenu() => new(
        [.. Facility.FleetMenu.Select(item => (item, FleetAction(item)))]);

    /// <summary>
    /// 함대편성 줄의 켜짐. 게임의 조건을 그대로 옮겼다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기함 변경  0x0046A220  배가 두 척 이상
    ///   선박 편입  0x0046A240  함대가 여덟 척 미만이고 이 마을에 맡긴 배가 있다
    ///   선박 삭제  0x0046A270  배가 두 척 이상(이 마을이 더 맡을 수 있어야)
    ///   선박 파기  0x0046A2C0  배가 두 척 이상
    /// </code>
    /// 조건이 어긋난 줄은 흐리게 둔다.
    /// </remarks>
    private Action? FleetAction(string item) => item switch
    {
        "기함 변경" when _player.Ships.Count > 1 => ChangeFlagship,
        "선박 편입" when !_player.IsFleetFull
                      && _player.DockedAt(_cityId).Count > 0 => TakeShip,
        "선박 삭제" when _player.Ships.Count > 1
                      && _player.DockedAt(_cityId).Count < Player.MaxDocked => LeaveShip,
        "선박 파기" when _player.Ships.Count > 1 => ScrapShip,
        Facility.FleetExit => Menu.Pop,
        _ => null,
    };

    /// <summary>
    /// 조선소에 배를 판다. 게임의 <c>0x0044B820</c> 자리다.
    /// </summary>
    /// <remarks>
    /// 값은 산 값의 <b>6할</b>에 도시 시세를 먹인 것이다(<see cref="Hull.SellPrice"/> ·
    /// <c>0x00423A30</c> → <c>0x00429DC0</c>). 배가 한 척뿐이면 줄 자체가 흐리고
    /// (<c>0x0044B863</c> 의 <c>cmp esi,1 / jle</c>), <b>기함은 못 판다</b>
    /// (<c>0x00531188</c> "기함을 처분하는 일은 불가능합니다!").
    ///
    /// 게임 화면은 여러 척을 한꺼번에 골라 값을 합쳐 파는 꼴인데(고른 것을 비트로 든다),
    /// 여기서는 한 척씩 판다 — 목록을 다시 열면 이어서 팔 수 있다.
    /// </remarks>
    private void SellShip()
    {
        var owner = Menu.Window ?? this;
        int rate = Market?.Rates.Of(_cityId) ?? 100;
        int PriceOf(Ship s) => Math.Max(1, s.Hull.SellPrice * rate / 100);

        GameDialog.Show(owner, "어느 배를 팔 건가? 봐 주겠네.");

        int at = HintListDialog.Pick(owner,
            [.. _player.Ships.Select((s, i) =>
                $"{(i == _player.Flagship ? "★" : "  ")}{s.Name}  {PriceOf(s),7}닢")],
            "매각", "팔 배가 없습니다");
        if (at < 0) return;

        if (at == _player.Flagship)
        {
            GameDialog.Show(owner, "기함을 처분하는 일은 불가능합니다!");
            return;
        }

        int paid = PriceOf(_player.Ships[at]);
        if (!ConfirmDialog.Ask(owner, $"{paid}닢입니다. 좋습니까?")) return;

        // 파기와 같은 자리에서 뺀다 — 게임도 배를 빼는 길은 하나다. 다른 것은 돈뿐이다.
        if (!_player.Scrap(at))
        {
            GameDialog.Show(owner, "이 이상 배를 처분하는 일은 불가능합니다.");
            return;
        }
        _player.Earn(paid);

        // 한 척만 남았으면 "매각" 줄이 그 자리에서 꺼져야 한다.
        Menu.Refresh();
    }

    /// <summary>
    /// 배를 고친다. 게임의 <c>0x0044B9C0</c> 자리다.
    /// </summary>
    /// <remarks>
    /// 고칠 배는 <b>함대와 이 마을에 맡겨 둔 배</b>를 다 훑어 모은다(<c>0x0044BC50</c>).
    /// 값은 이렇다.
    /// <code>
    ///   0x0044BBF0  손상 = (최대내구 - 지금내구) + (최대돛 - 지금돛)   ; 음수는 0
    ///   0x0044BAA1  값 = (rand(4) + 26) * 손상                        ; 26~29 곱
    ///   0x0044BABD  값 = 값 x 도시 시세 / 100                          ; 적어도 1
    /// </code>
    /// 우리 선체 표에는 돛 값이 없어 <b>내구만</b> 센다.
    ///
    /// 게임 화면은 여러 척을 한꺼번에 골라 값을 합치는데 여기서는 한 척씩 고친다.
    /// </remarks>
    /// <summary>
    /// 이 마을에서 고칠 수 있는 배 — 함대 먼저, 그 뒤가 이 마을이 맡은 배다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044BC50(도시, 0)</c> 이다. 이 목록이 비면 조선소 차림표의 <b>"수리" 줄이
    /// 꺼진다</b>(<c>0x0044BD40</c> 이 <c>0x0044BC50 &gt; 0</c> 을 본다) — 그래서 평소에는
    /// "수리가 필요한 배는 없네!" 를 볼 일이 없다.
    /// </remarks>
    private List<(Ship Ship, bool Docked)> RepairTargets()
    {
        var hurt = new List<(Ship Ship, bool Docked)>();
        foreach (var s in _player.Ships) if (s.NeedsRepair) hurt.Add((s, false));
        foreach (var s in _player.DockedAt(_cityId)) if (s.NeedsRepair) hurt.Add((s, true));
        return hurt;
    }

    private void RepairShip()
    {
        var owner = Menu.Window ?? this;
        int rate = Market?.Rates.Of(_cityId) ?? 100;

        var hurt = RepairTargets();
        if (hurt.Count == 0)
        {
            GameDialog.Show(owner, "수리가 필요한 배는 없네!");
            return;
        }

        int CostOf(Ship s) => Math.Max(1, (RepairRate + _random.Next(4)) * s.Damage * rate / 100);

        int at = HintListDialog.Pick(owner,
            [.. hurt.Select(h => $"{(h.Docked ? "맡김 " : "     ")}{h.Ship.Name}  " +
                                 $"내구 {h.Ship.Hp,3}/{h.Ship.MaxHp,-3}")],
            "수리선박 선택", "수리가 필요한 배는 없네!");
        if (at < 0 || at >= hurt.Count) return;

        var ship = hurt[at].Ship;
        int cost = CostOf(ship);
        if (!ConfirmDialog.Ask(owner, $"수리하는데 금화 {cost}닢 필요하네. 좋나?")) return;

        if (!_player.Pay(cost))
        {
            GameDialog.Show(owner, "소지금이 모자랍니다!");
            return;
        }
        ship.Repair();

        // 마지막 상한 배를 고쳤으면 "수리" 줄이 그 자리에서 꺼져야 한다.
        Menu.Refresh();
    }

    /// <summary>손상 한 점을 고치는 값의 밑수. 게임은 여기에 rand(4) 를 더한다.</summary>
    private const int RepairRate = 26;

    // ── 개조 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 조선소 개조 — 배를 고르고, 그 배의 개조 창을 연다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x00496960</c>)은 배를 고른 뒤 <b>그 마을에서 손댈 수 있는 배인지</b>부터
    /// 본다 — 도시의 문화권(<c>0x004A1820</c>)이 0~2 나 10 이면 다우선(선체 7)을 못 고치고,
    /// 그 밖의 문화권에서는 <b>다우선만</b> 고친다. 못 고치면
    /// "이 배 형은 내가 어떻게 할 수 없다."(<c>0x00532338</c>) 를 내고 도로 고르게 한다.
    /// 우리 선체 다섯에는 다우선이 없어 그 갈래가 안 생긴다 — 그래서 안 옮겼다.
    ///
    /// 배를 고르면 열한 줄짜리 개조 창이 뜨고(<c>0x004966E0</c>), 한 줄을 마치면 게임은
    /// <b>그 줄만 꺼</b>(<c>0x0049690A</c>) 같은 배를 계속 손보게 둔다. 우리도 그렇게 한다 —
    /// 더 못 늘리는 줄은 저절로 흐려진다.
    /// </remarks>
    private void RefitShip()
    {
        var owner = Menu.Window ?? this;
        if (_player.Ships.Count == 0) { GameDialog.Show(owner, "배가 없습니다"); return; }

        int at = HintListDialog.Pick(owner,
            [.. _player.Ships.Select((s, i) => ShipLine(s, i == _player.Flagship))],
            "개조선박의 선택", "배가 없습니다");
        if (at < 0 || at >= _player.Ships.Count) return;

        Menu.Push(() => RefitMenu(_player.Ships[at]));
    }

    /// <summary>배 한 척의 개조 창. 줄은 게임 열한 줄 그대로고, 할 수 없는 줄은 흐리다.</summary>
    private GameMenu RefitMenu(Ship ship) => new(
        [.. Facility.RefitMenu.Select(item => (item, RefitAction(ship, item)))]);

    private Action? RefitAction(Ship ship, string item) => item switch
    {
        Facility.RefitCapacity when ship.CanGrowCapacity => () => DoRefit(ship, item),
        Facility.RefitTonnage when ship.CanGrowTonnage => () => DoRefit(ship, item),
        Facility.RefitReinforce when ship.CanReinforce => () => DoRefit(ship, item),
        Facility.RefitMast when ship.CanAddMast => () => AddMast(ship),
        Facility.RefitSailKind when ship.CanChangeSail && ship.Masts > 0 => () => SwapSail(ship),
        Facility.RefitSail when ship.CanAddSail => () => AddSail(ship),
        Facility.RefitTurrets => () => ChangeTurrets(ship),
        Facility.RefitCannon => () => BuyCannon(ship),
        Facility.RefitRename => () => RenameShip(ship),
        Facility.RefitExit => Menu.Pop,
        _ => null,
    };

    /// <summary>
    /// 마스트 추가 — 돛대를 하나 더 세운다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00494BD0</c> 이다.
    /// <code>
    /// 494a50  코구·다우는 못 늘리고, 카라벨은 둘까지, 그 밖은 셋까지
    /// 494c2c  값 = 선체 구입값 / 5
    /// 494c7b  "적재용량이 조금 주는데 괜찮나?"
    /// 494c9a  카라벨·대형카라벨은 "마스트에는 삼각돛을 달겠네." — 고를 것 없이 삼각돛이다
    /// 494cc5  그 밖은 "마스트에 달 돛의 종류를 정해 주게." → 삼각 · 사각 · 그만둔다
    /// 494b52  적재용량 -= 25 · 필요승원 += 2
    /// </code>
    /// </remarks>
    private void AddMast(Ship ship)
    {
        var owner = Menu.Window ?? this;
        int cost = Math.Max(1, ship.Hull.Price / MastDivisor);

        GameDialog.Show(owner, $"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ConfirmDialog.Ask(owner, "적재용량이 조금 주는데 괜찮나?")) return;

        int sail;
        if (!ship.CanChangeSail)
        {
            if (!ConfirmDialog.Ask(owner, "마스트에는 삼각돛을 달겠네.")) return;
            sail = Ship.Lateen;
        }
        else
        {
            GameDialog.Show(owner, "마스트에 달 돛의 종류를 정해 주게.");
            int at = HintListDialog.Pick(owner, [Ship.SailNames[Ship.Lateen], Ship.SailNames[Ship.Square]],
                                         "돛 종류", "");
            if (at < 0) return;
            sail = at == 0 ? Ship.Lateen : Ship.Square;
            if (!ConfirmDialog.Ask(owner, sail == Ship.Lateen
                    ? "이것은 역풍에 뛰어나네. 이 돛을 달겠네?"
                    : "이것은 순풍에 뛰어나네. 이 돛을 달겠네?")) return;
        }

        var was = ship.Snapshot();
        _player.Pay(cost);

        int mast = ship.AddMast(sail);
        if (mast < 0) return;

        string where = Ship.MastNames[mast], what = Ship.SailNames[sail];
        NoticeDialog.Show(owner, $"{where}에 {what}{GameUi.Josa(what, "을", "를")} 달았습니다");
        ShowRefit(owner, Refit.Between(was, ship.Snapshot()), ship);
    }

    /// <summary>
    /// 돛종류 변경 — 마스트 하나의 돛을 삼각↔사각으로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00494F10</c> 이다. 값은 <b>선체 구입값 / 20</b>(<c>0x004950CB</c>).
    /// <code>
    ///   0x005316A8  "어느 마스트의 돛을 바꿀건가?"
    ///   0x005316D8  "삼각돛을 순풍에 뛰어난 사각돛으로 바꿀 건가?"
    ///   0x00531708  "사각돛을 역풍에 뛰어난 삼각돛으로 바꿀 건가?"
    ///   0x00531738  "금화 %ld닢이 드는데, 좋나?"
    ///   0x00531660  "%s%s %s%s 변경했습니다"
    /// </code>
    /// </remarks>
    private void SwapSail(Ship ship)
    {
        var owner = Menu.Window ?? this;
        var standing = new List<int>();
        for (int i = 0; i < Ship.MastSlots; i++)
            if (ship.Sails[i] != Ship.NoSail) standing.Add(i);
        if (standing.Count == 0) return;

        GameDialog.Show(owner, "어느 마스트의 돛을 바꿀건가?");
        int pick = HintListDialog.Pick(owner,
            [.. standing.Select(i => $"{GameUi.Pad(Ship.MastNames[i], 14)}{Ship.SailNames[ship.Sails[i]]}")],
            "돛종류 변경", "");
        if (pick < 0 || pick >= standing.Count) return;

        int mast = standing[pick];
        bool lateen = ship.Sails[mast] == Ship.Lateen;
        if (!ConfirmDialog.Ask(owner, lateen
                ? "삼각돛을 순풍에 뛰어난 사각돛으로 바꿀 건가?"
                : "사각돛을 역풍에 뛰어난 삼각돛으로 바꿀 건가?")) return;

        int cost = Math.Max(1, ship.Hull.Price / SailDivisor);
        if (!ConfirmDialog.Ask(owner, $"금화 {cost}닢이 드는데, 좋나?")) return;
        if (!_player.Pay(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ship.SwapSail(mast)) return;

        string where = Ship.MastNames[mast], what = Ship.SailNames[ship.Sails[mast]];
        NoticeDialog.Show(owner,
            $"{where}{GameUi.Josa(where, "을", "를")} {what}{GameUi.Josa(what, "으로", "로")} 변경했습니다");
        Menu.Refresh();
    }

    /// <summary>
    /// 돛 추가 — 추진력을 올리고 그만큼 배가 여려진다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00495320</c> 이다. 값은 <b>선체 구입값 / 20</b>.
    /// <code>
    ///   0x005317D8  "이 이상 돛을 단다면 마스트가 부러지네."
    ///   0x00531800  "금화 %ld닢이 드네."
    ///   0x00531818  "마스트에 부담이 되어 배가 조그마한 충격에도 약해지지만, 괜찮겠나?"
    /// </code>
    /// </remarks>
    private void AddSail(Ship ship)
    {
        var owner = Menu.Window ?? this;
        int cost = Math.Max(1, ship.Hull.Price / SailDivisor);

        GameDialog.Show(owner, $"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ConfirmDialog.Ask(owner,
                "마스트에 부담이 되어 배가 조그마한 충격에도 약해지지만, 괜찮겠나?")) return;

        _player.Pay(cost);
        ShowRefit(owner, ship.AddSail(), ship);
    }

    /// <summary>마스트 값을 나누는 수(<c>0x00494C32</c> 의 <c>mov $5,%ecx</c>).</summary>
    private const int MastDivisor = 5;

    /// <summary>돛 값을 나누는 수(<c>mov $0x14,%ecx</c>) — 돛종류 변경도 같다.</summary>
    private const int SailDivisor = 20;

    /// <summary>
    /// 포탑수변경 — 대포를 걸 자리를 늘리거나 줄인다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00496190</c> 이다.
    /// <code>
    /// 4961d6  상한 = min(선체 표 +0x30, 지금 포탑 + 적재용량)
    /// 496213  0x00454AA0("포탑수 결정", "포탑수", "문", 상한, "최대포탑수", "현재의 포탑수")
    /// 49621d  그대로면 "자네와 장난칠 여유없네."          0x00531F10
    /// 496234  값 = (새 - 지금) x 5 x 5 x 8 = 200 x 늘린 수
    /// 49624a  "금화 %ld닢 받겠네."                        0x00531F28
    /// 49625c  줄일 때는 "뗄 거라면 돈은 필요없네."         0x00531F40
    /// 4960d4  넘치는 대포는 "가격의 30프로로 사 주겠네."   0x00531F80
    /// </code>
    /// </remarks>
    private void ChangeTurrets(Ship ship)
    {
        var owner = Menu.Window ?? this;
        GameDialog.Show(owner, "포탑은 몇 개로 할건가?");

        int want = CountDialog.Ask(owner, "포탑수 결정", "포탑수", "문", ship.MaxTurrets, 1, true,
            new CountDialog.Gauge("최대포탑수", ship.MaxTurrets),
            new CountDialog.Gauge("현재의 포탑수", ship.Turrets));
        if (want < 0) return;
        if (want == ship.Turrets) { GameDialog.Show(owner, "자네와 장난칠 여유없네."); return; }

        int cost = Math.Max(0, want - ship.Turrets) * Cannon.TurretPrice;
        GameDialog.Show(owner, cost > 0 ? $"금화 {cost}닢 받겠네." : "뗄 거라면 돈은 필요없네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라네."); return; }
        if (!ConfirmDialog.Ask(owner, "괜찮겠나?")) return;

        var was = ship.Snapshot();
        var gun = Cannon.Of(ship.Gun);
        _player.Pay(cost);

        int spilled = ship.SetTurrets(want);
        if (spilled > 0 && gun != null)
        {
            GameDialog.Show(owner, "지금 싣고 있는 것은 가격의 30프로로 사 주겠네.");
            int back = gun.Price * spilled * Cannon.BuyBackPercent / 100;
            _player.Earn(back);
            GameDialog.Show(owner, $"금화 {back}닢을 벌었습니다.");
        }

        ShowRefit(owner, Refit.Between(was, ship.Snapshot()), ship);
    }

    /// <summary>
    /// 대포구입 — 포탑에 걸 대포를 골라 싣는다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004963E0</c> 이다.
    /// <code>
    /// 49643e  포탑이 0 이면 "포탑이 없으면 대포는 실을 수 없네."   0x005320E8
    /// 496473  "어느 대포를 실을 건가?"                            0x00532110
    /// 4964ff  남는 무게 = 적재중량 - 실은무게 + 지금 대포 무게      ; 다 내렸다 치고 잰다
    /// 496520  실을 수 있는 문수 = min(포탑수, 남는무게 / 대포중량)
    /// 496532  같은 대포를 이미 다 실었으면 "이 대포는 더 이상 실을 수 없네."  0x00532128
    /// 49654b  단가보다 돈이 적으면 "돈이 모자라는군."               0x00532178
    /// 4965b7  "실을 수 있을 만큼 싣겠네."(예/아니오) → 아니면 "얼마나 싣겠나?"
    /// 4965f8  "%s%s %d문 실으면 금화 %d닢이네. 좋은가?"            0x005321F0
    /// </code>
    /// 갈래를 바꿔 실으면 실려 있던 것은 <b>30프로</b>로 되사 준다.
    /// <b>어느 마을에서나 넷 다 판다</b> — 게임은 마을마다 파는 것을 가리는데
    /// (<c>0x00443FD0</c>) 그 표는 아직 안 읽었다.
    /// </remarks>
    private void BuyCannon(Ship ship)
    {
        var owner = Menu.Window ?? this;
        if (ship.Turrets <= 0) { GameDialog.Show(owner, "포탑이 없으면 대포는 실을 수 없네."); return; }

        GameDialog.Show(owner, "어느 대포를 실을 건가?");
        int at = HintListDialog.Pick(owner,
            [.. Cannon.All.Select(c => $"{GameUi.Pad(c.Name, 12)}{c.Price,6}닢{c.Weight,5}")],
            "대포 선택", "대포가 없네.");
        if (at < 0 || at >= Cannon.Count) return;

        var gun = Cannon.All[at];
        // 이 배의 대포를 다 내렸다 치고 함대에 남는 무게 — 게임도 그렇게 잰다.
        int free = _player.Tonnage - _player.LoadedWeight + ship.GunWeight;
        int room = ship.RoomFor(at, free);
        if (at == ship.Gun) room -= ship.Guns;

        if (room <= 0)
        {
            GameDialog.Show(owner, at == ship.Gun ? "이 대포는 더 이상 실을 수 없네."
                                                  : "이 대포는 무거워서 실을 수 없네.");
            return;
        }
        if (!_player.CanAfford(gun.Price)) { GameDialog.Show(owner, "돈이 모자라는군."); return; }

        GameDialog.Show(owner, gun.Word);
        room = Math.Min(room, _player.Gold / gun.Price);

        int want = ConfirmDialog.Ask(owner, "실을 수 있을 만큼 싣겠네.")
            ? room
            : CountDialog.Ask(owner, "얼마나 싣겠나?", "대포수", "문", room, 1, true,
                new CountDialog.Gauge("최대대포수", ship.Turrets),
                new CountDialog.Gauge("현재의 포수", ship.Guns));
        if (want <= 0) return;

        int cost = gun.Price * want;
        string who = gun.Name;
        if (!ConfirmDialog.Ask(owner,
                $"{who}{GameUi.Josa(who, "을", "를")} {want}문 실으면 금화 {cost}닢이네. 좋은가?"))
            return;
        if (!_player.Pay(cost)) { GameDialog.Show(owner, "돈이 모자라네."); return; }

        var was = ship.Snapshot();

        // 갈래가 갈리면 실려 있던 것은 30프로로 되사 준다.
        if (at != ship.Gun && Cannon.Of(ship.Gun) is { } old && ship.Guns > 0)
        {
            GameDialog.Show(owner, "지금 싣고 있는 것은 가격의 30프로로 사 주겠네.");
            int back = old.Price * ship.Guns * Cannon.BuyBackPercent / 100;
            _player.Earn(back);
            GameDialog.Show(owner, $"금화 {back}닢을 벌었습니다.");
            ship.Load(at, want);
        }
        else
        {
            ship.Load(at, ship.Guns + want);
        }

        ShowRefit(owner, Refit.Between(was, ship.Snapshot()), ship);
    }

    /// <summary>개조 결과 상자를 띄우고 개조 창을 다시 짓는다.</summary>
    private void ShowRefit(Window owner, Refit change, Ship ship)
    {
        if (change.Any)
            NoticeDialog.Show(owner, string.Join(Environment.NewLine,
                change.Lines.Select(l => $"{GameUi.Pad(l.Name, 12)}{l.Before,4} → {l.After,4}")));

        Menu.Pop();
        Menu.Push(() => RefitMenu(ship));
    }

    /// <summary>
    /// 선명변경 — 배 이름을 바꾼다. 값은 안 든다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x00495B90</c> → <c>0x00423BE0</c> 으로 <b>선명입력</b> 창을 띄운다.
    /// 미리 갖춰 둔 이름 스물하나가 먼저 뜨고(포인터 표 <c>0x0053C178</c>), 오른쪽 위 작은
    /// 단추를 누르면 글자판이 떠서 하나씩 찍어 지을 수 있다 —
    /// <see cref="ShipNameDialog"/> · <see cref="TextInputDialog"/> 가 그 둘이다.
    ///
    /// 게임은 배를 <b>살 때도</b> 같은 창으로 이름을 받는데 우리 조선소 구입은 아직 안 묻는다 —
    /// 그때는 안 쓴 이름을 하나 집어 준다(<c>Player.SuggestShipName</c>).
    /// </remarks>
    private void RenameShip(Ship ship)
    {
        var owner = Menu.Window ?? this;
        GameDialog.Show(owner, "배의 이름을 정해 주십시오");

        // 그대로 결정했으면 고칠 게 없다 — 창은 그 둘을 가려 주지 않는다.
        if (ShipNameDialog.Ask(owner, ship.Name) is not { } name || name == ship.Name) return;
        if (!ship.Rename(name)) return;

        NoticeDialog.Show(owner, $"{ship.Name}호로 바꾸었다");
        Menu.Refresh();
    }

    /// <summary>
    /// 개조 한 줄을 치른다 — 값을 알리고, 물어보고, 고치고, 바뀐 값을 보여 준다.
    /// </summary>
    /// <remarks>
    /// 차례와 문구는 게임 것 그대로다(<c>0x004955D0</c> 벌).
    /// <code>
    ///   0x00531938  "금화 %ld닢이 드네."
    ///   0x005319A8  "돈이 모자라는 것 같군."
    ///   0x00531950  "용량과 함께 적재용량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?"
    ///   0x00531920  "이 이상은 무리로군."
    /// </code>
    /// 게임처럼 <b>돈 검사를 물어보기 앞</b>에 한다 — 시장 구입과는 차례가 반대다.
    /// </remarks>
    private void DoRefit(Ship ship, string item)
    {
        var owner = Menu.Window ?? this;
        int cost = RefitCost(ship);

        GameDialog.Show(owner, $"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { GameDialog.Show(owner, "돈이 모자라는 것 같군."); return; }
        if (!ConfirmDialog.Ask(owner, RefitWarning(item))) return;

        _player.Pay(cost);
        var change = item switch
        {
            Facility.RefitTonnage => ship.GrowTonnage(),
            Facility.RefitReinforce => ship.Reinforce(),
            _ => ship.GrowCapacity(),
        };

        // 게임이 개조 뒤에 띄우는 "%-12s%4d → %4d" 상자. 배가 바뀌었으니 줄의 흐림도 다시 잡는다.
        ShowRefit(owner, change, ship);
    }

    /// <summary>개조 한 번 값 — 선체 구입값의 <b>15분의 1</b>(<c>0x004955F9</c>).</summary>
    /// <remarks>
    /// 게임 선체값은 만~이십오만 닢이라 개조도 수백~만 닢대다. 우리 <see cref="Hull.Price"/>
    /// 는 조선소 화면에서 옮긴 100~500 짜리 사다리라 <b>자릿수가 다르다</b> — 매각·수리와
    /// 마찬가지로 비율만 게임 것을 쓴다.
    /// </remarks>
    private static int RefitCost(Ship ship) => Math.Max(1, ship.Hull.Price / RefitDivisor);

    /// <summary>개조 값을 나누는 수(<c>mov $0xf,%ecx ; idiv</c>).</summary>
    private const int RefitDivisor = 15;

    /// <summary>그 줄이 무엇을 얻고 무엇을 잃는지 알려 주는 물음. 게임 문구 그대로다.</summary>
    private static string RefitWarning(string item) => item switch
    {
        Facility.RefitTonnage =>
            "적재용량과 함께 중량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?",
        Facility.RefitReinforce =>
            "내구력이 올라가지만, 스피드와 적재중량이 조금 떨어지네. 괜찮겠나?",
        _ => "용량과 함께 적재용량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?",
    };

    /// <summary>
    /// 배 한 척을 줄로 적는다 — 이름과 내구·추진·적재를 붙인다. 상했으면 내구를 "지금/최대"로 낸다.
    /// </summary>
    private static string ShipLine(Ship ship, bool flag)
    {
        string hp = ship.NeedsRepair ? $"{ship.Hp,3}/{ship.MaxHp,-3}" : $"{ship.MaxHp,3}    ";
        return $"{(flag ? "★" : "  ")}{ship.Name}  내구{hp} 추진{ship.Speed,3} 적재{ship.Capacity,4}";
    }

    /// <summary>기함을 바꾼다. 게임의 <c>0x0046A2F0</c> 자리다.</summary>
    private void ChangeFlagship()
    {
        var owner = Menu.Window ?? this;
        var ships = _player.Ships;

        int at = HintListDialog.Pick(owner,
            [.. ships.Select((h, i) => ShipLine(h, i == _player.Flagship))],
            "기함 변경", "바꿀 배가 없습니다");
        if (at < 0) return;

        var name = ships[at].Name;
        if (!ConfirmDialog.Ask(owner, $"기함을 {name}호로 변경하겠습니다. 좋습니까?")) return;

        _player.SetFlagship(at);
    }

    /// <summary>맡겨 둔 배를 함대에 넣는다. 게임의 <c>0x0046A350</c> 자리다.</summary>
    private void TakeShip()
    {
        var owner = Menu.Window ?? this;
        var docked = _player.DockedAt(_cityId);

        int at = HintListDialog.Pick(owner, [.. docked.Select(h => ShipLine(h, false))],
                                     "편입선박 선택", "이 마을에 맡겨 둔 배가 없습니다");
        if (at < 0) return;

        if (!_player.Undock(_cityId, at))
            GameDialog.Show(owner, "이 이상 편입할 수 없습니다.");
    }

    /// <summary>함대의 배를 이 마을에 맡긴다. 게임의 <c>0x0046A400</c> 자리다.</summary>
    private void LeaveShip()
    {
        var owner = Menu.Window ?? this;

        int at = HintListDialog.Pick(owner,
            [.. _player.Ships.Select((h, i) => ShipLine(h, i == _player.Flagship))],
            "선박삭제", "삭제할 배가 없습니다");
        if (at < 0) return;

        if (!_player.Dock(at, _cityId))
            GameDialog.Show(owner, "이 이상 삭제할 수 없습니다.");
    }

    /// <summary>배를 없앤다. 게임의 <c>0x0046A490</c> 자리다 — 되돌릴 수 없어 한 번 묻는다.</summary>
    private void ScrapShip()
    {
        var owner = Menu.Window ?? this;

        int at = HintListDialog.Pick(owner,
            [.. _player.Ships.Select((h, i) => ShipLine(h, i == _player.Flagship))],
            "선박파기", "파기할 배가 없습니다");
        if (at < 0) return;

        var name = _player.Ships[at].Name;
        if (!ConfirmDialog.Ask(owner, $"{name}호를 파기하겠습니다. 좋습니까?")) return;

        if (!_player.Scrap(at))
            GameDialog.Show(owner, "이 이상 파기할 수 없습니다.");
    }

    /// <summary>
    /// 선원편성 창. 모집·해고 두 줄과 돌아가기다 — 게임의 <c>0x004774E0</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// "선원해고" 는 태운 선원이 있어야 눌린다. 게임도 고르는 창을 지으며 그 줄의 켜짐을
    /// <c>0x0040E360() &gt; 0</c>(지금 선원 수)으로 정한다(<c>0x0047753E</c>).
    /// </remarks>
    private GameMenu CrewMenu() => new(
        [.. Facility.CrewMenu.Select(item => (item, CrewAction(item)))]);

    private Action? CrewAction(string item) => item switch
    {
        "선원모집" => HireCrew,
        "선원해고" when _player.Crew > 0 => FireCrew,
        Facility.CrewExit => Menu.Pop,
        _ => null,
    };

    /// <summary>
    /// 선원 한 사람 값. 명성이 높을수록 싸고, 아무리 높아도 10닢 밑으로는 안 내려간다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00477370</c> 그대로다 — <c>(10000 - 명성) / 400</c> 을 하고 10 과 견줘
    /// 큰 쪽을 쓴다. 명성(<c>0x005B614C</c>)이 1700 이면 스무 닢, 6000 을 넘으면 열 닢이다.
    /// </remarks>
    private int CrewPrice => Math.Max(10, (10000 - _player.Fame) / 400);

    /// <summary>
    /// 선원을 모집한다. 게임의 <c>0x00477330</c> 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 정원이 찼으면 아예 묻지 않고 물린다. 값을 못 치르면 다시 묻고, 다 태우고 나서도
    /// 최저 승원에 모자라면 한 번 더 권한다 — 게임도 그 자리에서 되돌아간다.
    /// </remarks>
    private void HireCrew()
    {
        var owner = Menu.Window ?? this;

        while (true)
        {
            if (_player.Crew >= _player.MaxCrew)
            {
                GameDialog.Show(owner,
                    "선원수가 함대의 상한에 달하고 있습니다! 이 이상 고용해도 승선할 수 없습니다.");
                return;
            }

            int price = CrewPrice;
            GameDialog.Show(owner, $"몇 명 모집하겠습니까? 한 사람 당 금화 {price}닢 필요합니다.");

            int want = CountDialog.Ask(owner, "선원고용", "고용할 사람 수", "명",
                                       _player.MaxCrew - _player.Crew, 1, false,
                                       new CountDialog.Gauge("현재의 선원 수", _player.Crew),
                                       new CountDialog.Gauge("최저 선원 수", _player.MinCrew));
            if (want <= 0) return;

            if (price * want > _player.Gold)
            {
                GameDialog.Show(owner, "소지금이 모자랍니다.");
                continue;
            }

            _player.Pay(price * want);
            _player.AddCrew(want);

            // 아직 최저 승원에 모자라면 한 번 더 권한다.
            int lack = _player.MinCrew - _player.Crew;
            if (lack <= 0) return;
            if (!ConfirmDialog.Ask(owner,
                    $"앞으로 적어도 {lack}명은 필요합니다. 좀더 선원을 모집하겠습니까?"))
                return;
        }
    }

    /// <summary>
    /// 선원을 해고한다. 게임의 <c>0x00477460</c> 차례 그대로다 — 삯은 돌려주지 않는다.
    /// </summary>
    private void FireCrew()
    {
        var owner = Menu.Window ?? this;

        GameDialog.Show(owner, "선원을 몇 명 해고시키겠습니까?");

        int want = CountDialog.Ask(owner, "선원해고", "해고할 사람 수", "명", _player.Crew,
                                   1, false,
                                   new CountDialog.Gauge("현재의 선원 수", _player.Crew),
                                   new CountDialog.Gauge("최저 승원 수", _player.MinCrew));
        if (want <= 0) return;

        // 최저 승원을 밑돌게 되면 한 번 물어본다.
        if (_player.Crew - want < _player.MinCrew
            && !ConfirmDialog.Ask(owner, "선원 수가 최저 승원 수를 밑돌고 있습니다. 괜찮습니까?"))
            return;

        _player.AddCrew(-want);
    }

    /// <summary>
    /// 지금 항구에서 알릴 수 있는 발견물. 찾은 차례대로다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00476D20</c> · <c>0x00476DA0</c> 그대로다.
    /// <code>
    ///   발견했고(깃발 0x40) · 아직 발표 안 했고(0x80 없음)
    ///   계약이 있으면 그 계약의 유적 번호와 <b>다른</b> 것만
    /// </code>
    /// 계약으로 맡은 것은 항구에서 못 알린다 — 그쪽은 후원자에게 보고해야 한다.
    /// 그래서 <b>계약 없이 발견한 것</b>이 여기 뜬다.
    /// </remarks>
    private List<DiscoveryTable.Record> Announceable()
    {
        var table = _game.Discoveries?.Table;
        if (table == null) return [];

        int target = _player.Contract is { } c && _game.Hints?.Find(c.Hint) is { } hint
                   ? hint.Discovery : -1;

        var rows = new List<DiscoveryTable.Record>();
        foreach (int id in _player.Discoveries.Order())
        {
            if (_player.HasAnnounced(id)) continue;
            if (table.Find(id) is not { } row) continue;
            if (target >= 0 && row.Hint == target) continue;   // 계약의 목표는 뺀다
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// 발견물을 알린다. 게임의 <c>0x00476E10</c> → <c>0x0047EA80</c> 차례다.
    /// </summary>
    /// <remarks>
    /// 알리면 명성이 <b>보수 ÷ 70</b>(적어도 10)만큼 오른다(<c>0x0047E849</c> 가 보수를
    /// 0x46 으로 나누고 10 과 견준다). 게임은 그 자리에서 피로도도 풀고 규율을 100 으로
    /// 되돌리는데, 그 둘은 아직 우리 쪽에 없다.
    ///
    /// 하나 알리고 나면 목록으로 돌아온다 — 게임도 고른 것을 다 알릴 때까지 돈다.
    /// </remarks>
    private void Announce()
    {
        var owner = Menu.Window ?? this;

        while (true)
        {
            var rows = Announceable();
            if (rows.Count == 0) return;

            int at = HintListDialog.Pick(owner, [.. rows.Select(r => r.Name)],
                                         "발표할 발견물 선택", "알릴 발견물이 없습니다");
            if (at < 0 || at >= rows.Count) return;

            var row = rows[at];
            if (!_player.Announce(row.Id)) continue;

            int fame = Math.Max(FameFloor, row.Reward / FamePerReward);
            _player.Fame += fame;

            GameDialog.Show(owner, $"{row.Name}의 발견을 발표했다!");
            GameDialog.Show(owner, $"명성이 {fame} 올라갔다!");
        }
    }

    /// <summary>알려서 오르는 명성 — 보수를 이만큼으로 나눈다(<c>0x0047E851</c>).</summary>
    private const int FamePerReward = 70;

    /// <summary>아무리 하찮아도 이만큼은 오른다(<c>0x0047E853</c>).</summary>
    private const int FameFloor = 10;

    /// <summary>
    /// 자택의 휴양 창 — 한 달 휴양 · 장기 휴양 · 취소. 게임의 <c>0x00460660</c> 그대로다.
    /// </summary>
    private GameMenu RestMenu() => new(
        [.. Facility.RestMenu.Select(item => (item, RestAction(item)))]);

    private Action? RestAction(string item) => item switch
    {
        "한 달 휴양" => RestOneMonth,
        "장기 휴양" => RestLong,
        Facility.RestExit => Menu.Pop,
        _ => null,
    };

    /// <summary>한 달 쉰다. 물어보고 예라야 쉰다.</summary>
    private void RestOneMonth()
    {
        if (ConfirmDialog.Ask(Menu.Window ?? this, "한 달 동안 휴양하겠습니까?")) Rest(1);
    }

    /// <summary>몇 달이고 쉰다. 게임처럼 한 해까지만 고를 수 있다.</summary>
    private void RestLong()
    {
        var owner = Menu.Window ?? this;

        GameDialog.Show(owner, "몇 개월 동안 휴양하겠습니까?");
        int months = CountDialog.Ask(owner, "휴양", "휴양할 달수", "개월", MaxRestMonths);
        if (months > 0) Rest(months);
    }

    /// <summary>
    /// 그만큼 쉰다. 값은 안 든다 — 내 집이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 <b>날수</b>를 넘긴다 — 달력 달이 아니라
    /// 서른 날이다. 쉬고 나면 아내가 있으면 아내가, 없으면 지문이 셋 중 하나를 낸다
    /// (<c>0x004607FE</c> 의 <c>rand(3)</c>). 우리 쪽에는 아내가 없어 지문만 쓴다.
    ///
    /// 쉬면 <b>하루에 피로 -1, 사기 +3</b> 씩 돌아온다 — 게임은 그것을 날을 넘기는 자리
    /// (<c>0x004A2AD0</c>)에서 함께 하므로 <see cref="Player.AdvanceDays"/> 가 맡는다.
    /// 그래서 한 달만 쉬어도 폭풍 몇 번 분이 한꺼번에 풀린다.
    /// </remarks>
    private void Rest(int months)
    {
        _player.AdvanceDays(RestDaysPerMonth * months);
        GameDialog.Show(Menu.Window ?? this, RestWords[_random.Next(RestWords.Length)]);
    }

    /// <summary>장기 휴양으로 고를 수 있는 가장 긴 달수(<c>0x00460782</c> 의 <c>push 0xC</c>).</summary>
    private const int MaxRestMonths = 12;

    /// <summary>휴양 한 달을 며칠로 세는지. 게임도 서른 날이다.</summary>
    private const int RestDaysPerMonth = 30;

    /// <summary>쉬고 나서 나오는 지문 셋. 게임 것 그대로다(<c>0x00539840</c> 벌).</summary>
    private static readonly string[] RestWords =
        ["피로가 풀렸다!", "체력이 회복되었다!", "기분이 상쾌하다!"];

    /// <summary>
    /// 자택의 저금 창 — 저금한다 · 꺼낸다 · 중지한다. 게임의 <c>0x004609C0</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// 제목이 <c>"저금 %8ld 닢"</c>(<c>0x005398C0</c>) 이라 지금 맡겨 둔 돈이 창 이름에 붙는다.
    /// 줄의 켜짐도 게임과 같다 — 저금은 소지금이, 꺼내기는 저금이 있어야 눌린다.
    /// </remarks>
    private GameMenu SavingsMenu() => new(
        $"저금 {_player.Savings,8} 닢", null,
        [.. Facility.SavingsMenu.Select(item => (item, SavingsAction(item)))]);

    private Action? SavingsAction(string item) => item switch
    {
        "저금한다" when _player.Gold > 0 => Deposit,
        "꺼낸다" when _player.Savings > 0 => Withdraw,
        Facility.SavingsExit => Menu.Pop,
        _ => null,
    };

    /// <summary>
    /// 저금한다. 소지금과 저금 칸이 남은 만큼만 맡길 수 있다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00460AC9</c> 그대로다 — 저금이 이미 백만 닢이면 "더 이상 저금할 수
    /// 없습니다"(<c>0x00539948</c>) 로 물리고, 아니면 <c>min(백만 - 저금, 소지금)</c> 까지 받는다.
    /// </remarks>
    private void Deposit()
    {
        var owner = Menu.Window ?? this;

        int room = Player.MaxGold - _player.Savings;
        if (room <= 0)
        {
            GameDialog.Show(owner, "더 이상 저금할 수 없습니다");
            return;
        }

        int want = CountDialog.Ask(owner, "저금한다", "금  액", "닢",
                                   Math.Min(room, _player.Gold), MoneyStep, full: true,
                                   new CountDialog.Gauge("소지금", _player.Gold),
                                   new CountDialog.Gauge("저  금", _player.Savings));
        if (want <= 0) return;

        GameDialog.Show(owner, $"금화 {_player.Deposit(want)}닢을 저금하겠습니다");
    }

    /// <summary>
    /// 저금을 꺼낸다. 소지금도 백만 닢에서 막히므로 그만큼만 꺼낼 수 있다.
    /// </summary>
    /// <remarks>게임의 <c>0x00460B5F</c> 그대로다.</remarks>
    private void Withdraw()
    {
        var owner = Menu.Window ?? this;

        int room = Player.MaxGold - _player.Gold;
        if (room <= 0)
        {
            GameDialog.Show(owner, "더 이상 꺼낼 수 없습니다");
            return;
        }

        int want = CountDialog.Ask(owner, "저금을 꺼낸다", "금  액", "닢",
                                   Math.Min(room, _player.Savings), MoneyStep, full: true,
                                   new CountDialog.Gauge("소지금", _player.Gold),
                                   new CountDialog.Gauge("저  금", _player.Savings));
        if (want <= 0) return;

        GameDialog.Show(owner, $"금화 {_player.Withdraw(want)}닢을 꺼내겠습니다");
    }

    /// <summary>돈을 ↑↓ 로 움직이는 단위. Shift 를 누르면 천 닢씩 뛴다.</summary>
    private const int MoneyStep = 100;

    private GameMenu SystemMenu() => new(
        [.. Facility.SystemMenu.Select(item => (item, SystemAction(item)))]);

    private Action? SystemAction(string item) => item switch
    {
        "저장" => SaveGame,
        "게임 종료" => QuitToTitle,
        "게임 재개" => CloseMenu,
        _ => null,
    };

    /// <summary>
    /// 놀이를 그만두고 첫 화면으로 돌아간다. 게임도 창을 닫지 않고 첫 화면으로만 되돌아간다.
    /// </summary>
    /// <remarks>
    /// 되돌리는 일은 함대 창이 맡는다 — 이 창은 그 창이 거느린 것이라 곧 닫힌다.
    /// 물어보고 나서 하는 것은 되돌릴 수 없기 때문이다(적어 두지 않은 것은 사라진다).
    /// </remarks>
    private void QuitToTitle()
    {
        if (Owner is not ShipMapWindow map) { CloseMenu(); return; }
        if (!ConfirmDialog.Ask(this, "게임을 그만두고 첫 화면으로 돌아갈까?")) return;

        CloseMenu();
        map.ReturnToTitle();
    }

    /// <summary>
    /// 지금 상태(소지금·날짜·있는 도시·배운 기술)를 적는다. 게임처럼 <b>겹쳐 쓸지 먼저 묻고</b>
    /// 다 적은 뒤에 겹쳐 썼다고 알린다 — 세이브 자리가 하나뿐이라 적는 일은 늘 겹쳐 쓰기다.
    /// 게임 폴더가 아니라 우리 자리에 쓴다 — <see cref="GameSave"/> 참고.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004A2800</c> 그대로다 — 물음(<c>0x00568CB8</c>) · 쓰기 · 알림(<c>0x00568CE0</c>).
    /// 물음에 YES 가 아니면 아무것도 쓰지 않고 그냥 돌아간다.
    /// </remarks>
    private void SaveGame()
    {
        var owner = Menu.Window ?? this;
        if (!ConfirmDialog.Ask(owner, "데이터를 겹쳐 쓰겠습니다. 좋습니까?")) return;

        string error = GameSave.Save(_player);
        ConfirmDialog.Tell(owner, error.Length == 0 ? "데이터를 겹쳐 썼습니다"
                                                    : $"기록하지 못했다 — {error}");
    }

    /// <summary>
    /// 도시 커맨드 창. 도시 화면에서 오른쪽 단추를 누르면 뜬다 — 제목은 도시 이름이고
    /// 제목 줄에 닫기(X)가 있다. 지금은 취소만 살아 있다.
    /// </summary>
    private GameMenu CityMenu(string cityName) => new(cityName, CloseCityMenu,
        ("맵 포인트에 들어간다", EnterMapPoint),
        ("인물 정보", ShowPerson),
        ("함대 정보", ShowFleet),
        ("소지품 정보", ShowBelongings),
        ("도시 정보", ShowCityInfo),
        ("힌트 정보", ShowHints),
        ("계약 정보", ShowContract),
        ("후원자 정보", ShowPatrons),
        ("지도를 본다", () => _cityMenu.Push(MapMenu)),
        ("게임 종료", QuitToTitle),
        ("취소", CloseCityMenu));

    /// <summary>
    /// 인물 정보. <b>부하가 하나라도 있으면</b> 게임처럼 누구를 볼지 먼저 묻고,
    /// 아무도 없으면 곧바로 제독의 판을 낸다.
    /// </summary>
    /// <remarks>도시 안이라 함대좌표는 게임처럼 <c>---</c> 다.</remarks>
    private void ShowPerson()
    {
        if (_player.MateCount == 0)
        {
            CloseCityMenu();
            PersonInfoDialog.Show(this, _player, _gameDirectory);
            return;
        }
        _cityMenu.Push(PersonMenu);
    }

    /// <summary>
    /// 누구의 인물정보를 볼지 고르는 창 — 제독과 부하 네 자리다.
    /// </summary>
    /// <remarks>
    /// 자리는 늘 넷 다 낸다(게임 화면이 그렇다). <b>빈 자리는 흐려 두고 안 먹는다</b> —
    /// 앉은 사람이 없으면 낼 판도 없기 때문이다.
    /// </remarks>
    private GameMenu PersonMenu()
    {
        var rows = new List<(string, Action?)>
        {
            ("플레이어", () =>
            {
                CloseCityMenu();
                PersonInfoDialog.Show(this, _player, _gameDirectory);
            }),
        };

        for (int i = 0; i < Player.MaxMates; i++)
        {
            int slot = i;
            string name = _player.MateAt(slot);
            rows.Add((Player.MateRoles[slot],
                      name.Length == 0 ? null : () => ShowMate(slot)));
        }

        rows.Add(("취소", CloseCityMenu));
        return new GameMenu("", null, [.. rows]);
    }

    /// <summary>
    /// 그 자리에 앉은 부하의 인물정보 판.
    /// </summary>
    /// <remarks>
    /// <b>우리 세이브에 적어 둔 것을 먼저 본다.</b> 없으면(판 20 앞에 들인 부하) 게임
    /// 세이브의 인물표를 뒤져 그 자리에서 채워 넣는다 — 한 번 채우면 다음부터는 우리
    /// 것만으로 뜬다. 게임 세이브가 없거나 이름이 바뀌었어도 이미 적어 둔 부하는 그대로
    /// 보인다.
    /// </remarks>
    private void ShowMate(int slot)
    {
        string name = _player.MateAt(slot);
        var who = _player.MateInfoOf(name);

        if (who == null && Roster()?.Find(name) is { } person)
        {
            who = MateInfoOf(person);
            _player.RememberMate(who.Value);
        }

        CloseCityMenu();

        if (who is { } mate)
            PersonInfoDialog.ShowMate(this, mate, Player.MateRoles[slot], _gameDirectory);
        else
            NoticeDialog.Show(this, $"{name}의 자료를 찾지 못했다");
    }

    /// <summary>함대 정보 판.</summary>
    private void ShowFleet()
    {
        CloseCityMenu();
        FleetInfoDialog.Show(this, _player);
    }

    /// <summary>
    /// 「맵 포인트에 들어간다」 — 이 도시의 건물을 늘어놓고 고른 데로 들어간다.
    /// </summary>
    /// <remarks>
    /// 게임 커맨드의 그 줄이다(<c>0x0053BE10</c>). 누르면 <b>"어디로 들어 가시겠습니까?"</b>
    /// (<c>0x0053BF38</c>) 창이 뜨고 그 도시의 건물이 줄줄이 선다 — 고르면 그 건물의 명령
    /// 창이 열린다. 그림에서 작은 건물을 눈으로 찾아 누르지 않아도 되는 길이다.
    /// 건물이 하나도 없으면 게임 말대로 "맵 포인트 데이터가 없습니다"(<c>0x0053A7FB</c>) 다.
    /// </remarks>
    private void EnterMapPoint()
    {
        var spots = _table.InCity(_cityId);
        if (spots.Count == 0)
        {
            NoticeDialog.Show(this, "맵 포인트 데이터가 없습니다");
            return;
        }

        // 줄은 건물 이름이다 — "베렌의 탑" 처럼 그 도시만의 이름이 뜨고, 없으면 종류를 낸다.
        int at = MapPointDialog.Ask(this,
            [.. spots.Select(b => b.Name.Length > 0 ? b.Name : b.Kind)]);
        if (at < 0 || at >= spots.Count) return;

        CloseCityMenu();
        Enter(spots[at]);
    }

    /// <summary>「지도를 본다」 — 항해지도 · 주변지도 · 돌아간다.</summary>
    private GameMenu MapMenu() => new("지도를 본다", null,
        ("항해지도", () => LookAtMap(wide: true)),
        ("주변지도", () => LookAtMap(wide: false)),
        ("돌아간다", _cityMenu.Pop));

    /// <summary>도시 그림을 잠깐 걷고 지도를 본다. 되돌리는 것은 함대 창이 맡는다.</summary>
    private void LookAtMap(bool wide)
    {
        if (Owner is not ShipMapWindow map) { CloseCityMenu(); return; }
        CloseCityMenu();
        map.LookAtMap(wide, this);
    }

    /// <summary>도시 정보 창을 낸다. 표를 못 읽어도 열린다 — 그 줄만 비는 채로 뜬다.</summary>
    private void ShowCityInfo()
    {
        CloseCityMenu();
        CityInfoDialog.Show(this, _cityName, _cityId, _game.CityRows,
                            _game.Nations, _game.Goods, _game.ItemPictures,
                            Market?.Rates ?? MarketRates.Open());
    }

    /// <summary>
    /// 여관에 묵는다. 게임 차례 그대로 — 값을 부르고, YES 면 그때서야 돈을 본다.
    /// </summary>
    /// <remarks>
    /// 묵고 나면 한 달이 가므로 상단 띠의 날짜가 그만큼 넘어간다.
    /// </remarks>
    private void Stay()
    {
        var inn = _lodging ??= new Lodging(_game.CityRows, MarketRates.Open());
        int price = inn.PriceAt(_cityId);

        if (!ConfirmDialog.Ask(this, $"선불이네. 우리 집은 한 달에 금화 {price}닢인데, 머물고 갈텐가?"))
            return;

        if (inn.Stay(_player, _cityId) != StayResult.Ok)
        {
            NoticeDialog.Show(this, "소지금이 모자랍니다");
            return;
        }

        NoticeDialog.Show(this, "손님, 손님! 일어나세요. 벌써 아침이에요.");
        NoticeDialog.Show(this, Lodging.WakeWord(_random));
    }

    private Lodging? _lodging;

    /// <summary>셋 중 하나를 고르는 데 쓴다. 게임도 rand(3) 으로 고른다.</summary>
    private readonly Random _random = new();

    /// <summary>
    /// 소지품 정보 창을 낸다. 아이템 표를 못 읽어도 열린다 — 이름이 번호로 나올 뿐이다.
    /// </summary>
    private void ShowBelongings()
    {
        CloseCityMenu();

        var table = _game.Discoveries?.Table;
        BelongingsDialog.Show(this, _player, _game.Items, _game.ItemText, _game.ItemPictures,
                              [.. _player.Discoveries.Order()
                                        .Select(id => table?.Find(id)?.Name ?? $"발견물 {id}")]);
    }

    /// <summary>도시 커맨드 창. 그림 안이 아니라 제 창으로 띄운다.</summary>
    /// <summary>여관에서 몸으로 값을 치르는 줄.</summary>
    private const string OddJob = "허드렛일";

    /// <summary>
    /// 이 돈부터는 허드렛일 줄이 안 나온다. 주머니가 넉넉하면 몸으로 갚을 까닭이 없다.
    /// </summary>
    private const int OddJobMaxGold = 300;

    private GameMenuHost? _cityMenuHost;

    private GameMenuHost _cityMenu => _cityMenuHost ??= new GameMenuHost(this);

    /// <summary>
    /// 도시 커맨드 창을 <b>누른 자리</b>에 띄운다. 시설 창(<see cref="ShowMenu"/>)은 게임대로
    /// 그림 한가운데지만, 이쪽은 게임에 없는 창이고 그림 아무 데나 눌러서 내는 것이라
    /// 손이 간 자리에 뜨는 편이 맞다.
    /// </summary>
    private void ShowCityMenu(string cityName, Point at)
    {
        if (_cityMenu.IsOpen) { _cityMenu.Focus(); return; }
        _cityMenu.Open(() => CityMenu(cityName), at);
    }

    /// <summary>창 안의 자리를 화면 자리(WPF 단위)로. 창을 그 자리에 띄울 때 쓴다.</summary>
    private Point ToScreen(Point at)
    {
        var device = PointToScreen(at);
        var source = PresentationSource.FromVisual(this);
        return source == null
            ? device
            : source.CompositionTarget.TransformFromDevice.Transform(device);
    }

    private void CloseCityMenu() => _cityMenu.Close();

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
    private void Persuade(Patron patron)
    {
        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        string shown = sponsor?.Name ?? patron.Name;             // 게임 이름은 가운뎃점이 들어간다
        string sir = sponsor?.Honorific ?? "각하";
        string me = _player.Name;

        var face = FaceOf(patron);
        void Say(string words) => TalkDialog.Say(this, face, "", words);
        void Steward(string words) => TalkDialog.Say(this, StewardFace(), "", words);

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
        _bgm?.Play(BgmPlayer.SponsorTrack);
        try
        {
            Audience(patron, shown, sir, me, face, Say, Steward);
        }
        finally
        {
            _bgm?.Play(_cityTrack);   // 그 자리를 나오면 도시 곡으로 돌아간다
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
        var names = mine.Select(HintNameOf).ToList();
        int row = HintListDialog.Pick(this, names, "제안 선택");
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

        // 안목 판정 — 후원자가 이야기의 크기를 가늠하지 못하면 물린다.
        if (patron.Discernment / 20 + (it.Grade == 5 ? 1 : 2) < it.Grade)
        {
            Say("가능한 한 원조해 주고 싶지만, 너무나 이야기가 막연하네.");
            return;
        }

        int funds = HintTable.FundsFor(it, patron.SupportRatePercent);

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

        int pick = TalkDialog.Ask(this, face, "",
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
    private string PatronRow(Patron patron) =>
        CanReport(patron) ? Facility.Report
      : Contracted(patron) ? Facility.Break
      : Facility.Persuade;

    /// <summary>이 후원자와 이 자리에서 계약 중인지(<c>0x0044E550</c>).</summary>
    private bool Contracted(Patron patron) =>
        _player.Contract is { } c && c.Sponsor == patron.Name && c.City == _cityName;

    /// <summary>
    /// 그 후원자에게 보고할 발견물. 계약의 유적 번호를 가진 것 중 발견했고 아직 안 알린 것이다.
    /// </summary>
    private List<DiscoveryTable.Record> ReportTargets(Patron patron)
    {
        if (_player.Contract is not { } contract) return [];
        if (contract.Sponsor != patron.Name || contract.City != _cityName) return [];
        if (_game.Discoveries?.Table is not { } table) return [];
        if (_game.Hints?.Find(contract.Hint) is not { } hint) return [];

        var rows = new List<DiscoveryTable.Record>();
        foreach (int id in _player.Discoveries.Order())
        {
            if (_player.HasAnnounced(id)) continue;
            if (table.Find(id) is not { } row || row.Hint != hint.Discovery) continue;
            rows.Add(row);
        }
        return rows;
    }

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
    private void Report(Patron patron)
    {
        var contract = _player.Contract;
        var rows = ReportTargets(patron);
        if (contract == null || rows.Count == 0) return;

        var face = FaceOf(patron);
        void Say(string text) => TalkDialog.Say(this, face, "", text);

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

        GameDialog.Show(this, $"금화 {paid}닢을 받았다!");
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
    private void BreakContract(Patron patron)
    {
        if (_player.Contract is not { } contract) return;

        var owner = Menu.Window ?? this;
        var face = FaceOf(patron);
        void Say(string text) => TalkDialog.Say(this, face, "", text);

        bool overdue = contract.IsOverdue(_player.Date);
        if (!ConfirmDialog.Ask(owner, overdue
                ? "기한을 넘겼다. 계약을 그만두겠나?"
                : "계약을 그만두겠나?")) return;

        CloseCityMenu();

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
            GameDialog.Show(this, "제독, 곤란하게 되었습니다... 위험하니 일단 스폰서와는 " +
                                  "가까이 하지 않는 것이 좋을 것 같군요.");
            return;
        }

        Say(overdue
            ? "기대가 빗나갔군! 이번 실패는 잊어주지. 생각이 바뀌기 전에 나가주게."
            : "안됐군요, 무리하게 보내서는 성과도 없을테니, 이 계약은 잊어버립시다.");

        int penalty = contract.Penalty;
        if (!_player.Pay(penalty))
        {
            GameDialog.Show(this, "위약금을 지불할 수 없습니다!");
            Say("바보같은, 위약금을 지불할 수 없다고! 어디까지 어리석은...");
            _player.EndContract();
            GameDialog.Show(this, "제독, 곤란하게 되었습니다... 위험하니 일단 스폰서와는 " +
                                  "가까이 하지 않는 것이 좋을 것 같군요.");
            return;
        }

        _player.EndContract();
        GameDialog.Show(this, $"위약금으로 금화 {penalty}닢을 물었다.");
    }

    /// <summary>
    /// 계약을 깨는 것을 후원자가 눈감아 주는지(<c>0x0044F8B0</c>).
    /// </summary>
    private bool Forgiven(Patron patron, bool overdue) =>
        _random.Next(overdue ? LateRoll : OnTimeRoll)
            < Math.Min(ForgiveCap, patron.Fame / 100 + _player.Fame / 100 + 1);

    /// <summary>기한 안에 깰 때 굴리는 주사위 폭(<c>add $0x64,%eax</c>).</summary>
    private const int OnTimeRoll = 100;

    /// <summary>기한을 넘겨 깰 때의 폭 — 반쯤 넓어져 통과하기 어렵다(<c>and $0x32</c>).</summary>
    private const int LateRoll = 150;

    /// <summary>문턱을 자르는 값(<c>cmp $0x61,%ecx</c>).</summary>
    private const int ForgiveCap = 97;

    /// <summary>
    /// 보고 사례. 미불에 비율을 먹이고 100닢 단위로 내린다.
    /// </summary>
    /// <remarks>게임의 <c>0x00411D10</c> · <c>0x004117D0</c> 그대로다.</remarks>
    private int RewardFor(Contract contract, bool inTime)
    {
        int rate = inTime ? 120 + _random.Next(30) : 90 - _random.Next(20);
        int paid = (int)((long)contract.Unpaid * rate / 100);
        return paid > 100 ? paid / 100 * 100 : paid;
    }

    /// <summary>
    /// 계약 정보 창을 낸다. 계약이 없으면 창 대신 "계약을 맺지 않았습니다" 한 줄이다.
    /// </summary>
    /// <remarks>
    /// 증거품은 계약 중 발견한 것이 준 물건 가운데 <b>아직 지니고 있는</b> 것만 센다 —
    /// 팔아 버렸으면 내밀 증거가 없다.
    /// </remarks>
    private void ShowContract()
    {
        CloseCityMenu();

        var contract = _player.Contract;
        if (contract == null)
        {
            ContractDialog.Show(this, null, _player.Date, "", [], []);
            return;
        }

        var table = _game.Discoveries?.Table;
        var items = _game.Items;

        var found = new List<string>();
        var evidence = new List<string>();
        foreach (int id in contract.Found)
        {
            var row = table?.Find(id);
            found.Add(row?.Name ?? $"발견물 {id}");

            if (row is not { GivesItem: true } got || !_player.HasItem(got.ItemId)) continue;
            evidence.Add(items?.Find(got.ItemId)?.Name ?? $"아이템 {got.ItemId}");
        }

        ContractDialog.Show(this, contract, _player.Date,
                            HintNameOf(contract.Hint), found, evidence);
    }

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

    /// <summary>힌트 이름. 판이 게임 표 · DB · 번호 차례로 물러서며 찾아 준다.</summary>
    private string HintNameOf(int id) => _game.HintName(id);

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

    /// <summary>얻은 힌트를 늘어놓는다. 이름은 판이 찾아 준다.</summary>
    private void ShowHints() =>
        HintListDialog.Show(_cityMenu.Window ?? this,
                            [.. _player.Hints.Order().Select(HintNameOf)]);

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
    private void ShowPatrons()
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
        var owner = _cityMenu.Window ?? this;
        while (true)
        {
            int row = HintListDialog.Pick(owner, names, "스폰서 일람",
                                          "이 마을에는 아는 스폰서가 없습니다");
            if (row < 0 || row >= mine.Count) return;

            var (patron, sponsor) = mine[row];
            PatronInfoDialog.Show(owner, patron, sponsor?.Name, sponsor?.Job);
        }
    }

    /// <summary>
    /// 시설의 명령 창을 짓는다. 줄은 <see cref="Facility"/> 표에서 오고, 그중 흉내낼 수 있는
    /// 것만 <see cref="ActionFor"/> 가 손을 달아 준다. 나머지는 흐린 채로 둔다.
    /// 제목은 건물 이름이다(게임도 그렇다).
    /// </summary>
    private GameMenu BuildMenu(Facility facility, string title, uint teachMask, string kind)
    {
        var items = facility.Menu.ToList();
        // 가르치는 건물인데 줄에 수련이 없으면(학자 저택 따위) 맨 앞에 붙여 준다.
        if (teachMask != 0 && !items.Contains("수련")) items.Insert(0, "수련");

        // 여관 허드렛일은 주머니가 가벼울 때만 나온다. 게임은 조건이 어긋난 줄을 흐리게
        // 두지 않고 아예 감춘다 — 설득·감찰관 매수와 같은 규칙이다.
        if (facility.Kind == FacilityKind.Inn && _player.Gold >= OddJobMaxGold)
            items.Remove(OddJob);

        // 항구의 "발표" 는 알릴 발견물이 있을 때만 뜬다. 게임도 그 줄의 보임 쪽을 조건으로
        // 켠다(0x00477974 가 0x00476DE0 의 값을 넣는다).
        if (facility.Kind == FacilityKind.Harbor && Announceable().Count == 0)
            items.Remove(Facility.Announce);

        // 후원자가 앉은 건물이면 "설득" 이 맨 앞에 붙는다 — 왕궁만이 아니라 총독부·상관·
        // 학자 저택 어디든 그렇다. 게임도 물린 후원자가 없으면 그 줄을 아예 감춘다.
        //
        // 그 자리가 계약을 맺은 자리이고 맡은 것을 찾아 왔으면 그 줄이 "보고" 로 바뀐다 —
        // 게임도 같은 자리를 계약 상태로 갈아 끼운다(0x0044EAE0).
        var patron = PatronAt(kind);
        if (patron != null) items.Insert(0, PatronRow(patron));

        return new GameMenu(title, null,
            [.. items.Select(item => (item, ActionFor(facility, item, teachMask, patron, title, kind)))]);
    }

    /// <summary>
    /// 시장에서 사고파는 규칙. 처음 쓸 때 한 번만 짓는다 — 아이템 표를 못 읽으면 null 이고,
    /// 그러면 "구입" 줄이 흐린 채로 남는다.
    /// </summary>
    private Market? Market
    {
        get
        {
            if (_market != null || _marketTried) return _market;
            _marketTried = true;

            var items = _game.Items;
            if (items == null)
            {
                System.Diagnostics.Debug.WriteLine($"[City] 아이템 표 없음: {ItemTable.LastError}");
                return null;
            }
            // 시세는 아직 다 100 이다. 나중에 채우면 값이 저절로 따라 움직인다.
            _market = new Market(items, MarketRates.Open(), _game.CityRows);
            return _market;
        }
    }

    private Market? _market;
    private bool _marketTried;

    /// <summary>이 건물에 앉아 있는 후원자. 없으면 null.</summary>
    private Patron? PatronAt(string kind) =>
        new PatronService().SeatedAt(LoadPatrons(), _cityName, _player.Date.Year, kind, KindsHere);

    /// <summary>이 도시에 있는 건물 종류들. 후원자를 앉힐 자리를 고를 때 쓴다.</summary>
    private HashSet<string> KindsHere =>
        _kindsHere ??= [.. _table.InCity(_cityId).Select(b => b.Kind)];

    private HashSet<string>? _kindsHere;

    /// <summary>
    /// 그 줄이 하는 일. 지금 되는 것은 나가기와 출항·구입·수련뿐이다 —
    /// 보급·함대편성 따위는 이 창이 흉내내는 범위 밖이라 손을 달지 않는다(흐리게 나온다).
    /// </summary>
    private Action? ActionFor(Facility facility, string item, uint teachMask, Patron? patron,
                              string title, string kind)
    {
        if (item == facility.ExitItem) return CloseMenu;
        if (item == "수련" && teachMask != 0) return () => Teach(teachMask, facility.Kind);
        if (item == "기능") return () => Menu.Push(SystemMenu);
        if (item == Facility.Persuade && patron != null) return () => Persuade(patron);
        if (item == Facility.Report && patron != null) return () => Report(patron);
        if (item == Facility.Break && patron != null) return () => BreakContract(patron);

        return (facility.Kind, item) switch
        {
            (FacilityKind.Harbor, "출항") => () => { Sailed = true; Close(); },
            // 함대편성은 제목 없는 창이 한 겹 더 뜬다. 줄은 게임 것 그대로 두되, 아직 손이
            // 안 달린 줄은 흐리게 남긴다 — 보급·선원편성과 같은 규칙이다.
            (FacilityKind.Harbor, "함대편성") => () => Menu.Push(FleetMenu),
            (FacilityKind.Harbor, "선원편성") => () => Menu.Push(CrewMenu),
            (FacilityKind.Harbor, Facility.Announce) => Announce,
            (FacilityKind.Home, "휴양") => () => Menu.Push(RestMenu),
            (FacilityKind.Home, "저금") => () => Menu.Push(SavingsMenu),
            (FacilityKind.Home, "보관") => () =>
                StorageDialog.Show(Menu.Window ?? this, _player, _game.Items),
            (FacilityKind.Harbor, "보급") => () =>
                SupplyDialog.Show(Menu.Window ?? this, _player,
                                  Market?.Rates.Of(_cityId) ?? 100),
            (FacilityKind.Shipyard, "구입") => () => HullSelectDialog.Show(this, _player),
            (FacilityKind.Shipyard, "매각") when _player.Ships.Count > 1 => SellShip,
            // 게임도 고칠 배가 없으면 이 줄을 흐리게 둔다(0x0044BD40).
            (FacilityKind.Shipyard, "수리") when RepairTargets().Count > 0 => RepairShip,
            (FacilityKind.Shipyard, "개조") => RefitShip,
            (FacilityKind.Market, "구입") when Market != null => () =>
                MarketBuyDialog.Show(this, _player, Market, _cityId, _game.ItemText, _game.ItemPictures),
            (FacilityKind.Market, "매각") when Market != null && _game.Items != null => () =>
                MarketSellDialog.Show(this, _player, Market, _game.Items, _cityId),
            (FacilityKind.Inn, "숙박") => Stay,
            // 부하편성은 여관과 술집 둘 다에 있다.
            (FacilityKind.Inn or FacilityKind.Tavern, "부하편성") => () =>
                MateRosterDialog.Show(this, _player),
            (FacilityKind.Library, "열람") when _library != null => () =>
                LibraryDialog.Show(this, _gameDirectory, _cityName, _cityId,
                                   _player, _library, _table, HintNameOf),
            _ => null,
        };
    }

    /// <summary>
    /// 도시 그림 창을 연다. 그림을 못 풀면 null 이다 — 그림이 없다고 입항까지 막을 일은 아니다.
    /// </summary>
    /// <remarks>
    /// <b>모달로 띄우지 않는다.</b> 모달이면 같은 앱의 다른 창이 입력을 못 받아 함대 창
    /// 제목 줄(옮기기·닫기)이 죽어 버린다. 배는 부르는 쪽에서 멈춰 두고, 창이 닫히면
    /// <see cref="Window.Closed"/> 로 풀어 준다.
    /// </remarks>
    /// <param name="mapArea">
    /// 지도가 놓인 자리(화면 좌표, WPF 단위). 그 자리를 통째로 덮는다. 비워 두면 그림 크기에
    /// 맞춰 owner 한가운데에 띄운다.
    /// </param>
    public static CityPicView? Open(Window owner, Engine.Game game, int cityId, string cityName,
                                      Rect mapArea = default,
                                      int cityTrack = BgmPlayer.CityTrack,
                                      string culture = "")
    {
        // 그림도 건물 표도 없으면 열지 않는다 — 건물 자리를 모르면 도시 안에서 할 일이 없다.
        if (game.CityPics is not { } pictures || game.Buildings == null) return null;

        var bgra = pictures.TryGetBgra(cityId);
        if (bgra == null) return null;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, CityPictures.Width * 4);
        picture.Freeze();

        double areaW = mapArea.Width > 0 ? mapArea.Width : owner.ActualWidth;
        double areaH = mapArea.Height > 0 ? mapArea.Height : owner.ActualHeight;

        var dlg = new CityPicView(game, cityName, picture, PickScale(areaW, areaH), cityId,
                                    mapArea, cityTrack, culture)
        {
            Owner = owner,
        };
        dlg.Show();
        // 닫을 때 초점이 앱 밖으로 새지 않게 붙든다.
        FocusWatch.KeepInApp(dlg);
        // 초점이 어디로 가는지 보려고 둔 진단(FocusWatch). 다 잡고 나면 지운다.
        dlg.Closed += (_, _) => FocusWatch.After("도시그림창 닫힘");
        dlg.Deactivated += (_, _) => FocusWatch.After("도시그림창 초점 잃음");
        return dlg;
    }

    /// <summary>
    /// 그림 배율. 게임처럼 지도의 반쯤을 덮는 크기로 잡는다(자리가 좁아도 1배는 쓴다).
    /// </summary>
    private static int PickScale(double areaWidth, double areaHeight)
    {
        int scale = (int)Math.Min(areaWidth * 0.6 / CityPictures.Width,
                                  areaHeight * 0.7 / CityPictures.Height);
        return Math.Max(1, Math.Min(scale, 4));
    }
}
