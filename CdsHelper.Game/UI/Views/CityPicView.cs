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
using CdsHelper.Game.Engine.Town;
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
    /// 그 부하의 얼굴. 신상은 판이 찾아 준다(<see cref="Engine.Game.MateInfo"/>) —
    /// 적어 둔 것이 없으면 게임 세이브의 인물표를 한 번 뒤져 채운다.
    /// </summary>
    private uint[]? MateFace(string name) =>
        _game.MateInfo(name) is { } mate && mate.Face is >= 0 and < 0xFFFF
            ? _game.Faces?.TryGetBgra(mate.Face, female: false)
            : null;

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

        _photoWindow = BuildingPhotoWindow.Show(this, photos.TryGetBgra(k), Guests.GuestArt(kind), _scale,
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
        var people = _game.Roster?.At(_cityId, building) ?? [];
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
        _player.RememberMate(Tavern.MateInfoOf(who));
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

    /// <summary>한잔 산다. 정말 샀으면 true — 낯을 트는 것은 부르는 쪽이 판단한다.</summary>
    private bool BuyDrink()
    {
        if (_player.Gold < Tavern.DrinkPrice)
        {
            NoticeDialog.Show(this, "돈 먼저 지불하게.");
            return false;
        }
        _player.SetGold(_player.Gold - Tavern.DrinkPrice);
        NoticeDialog.Show(this, $"금화 {Tavern.DrinkPrice}닢으로 한잔 샀다.");
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
    /// 자택·여관의 "기능" 줄 — 저장·로드·게임 종료다. 도시 일이 아니라 판 일이라
    /// <see cref="GameSystemMenu"/> 가 든다.
    /// </summary>
    private GameMenu SystemMenu() => GameSystemMenu.Build(this, _game, Menu);

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
        ("후원자 정보", () => { CloseCityMenu(); Patrons.ShowPatrons(); }),
        ("지도를 본다", () => _cityMenu.Push(MapMenu)),
        ("게임 종료", () => GameSystemMenu.Quit(this, Menu)),
        ("취소", CloseCityMenu));

    /// <summary>
    /// 인물 정보. <b>부하가 하나라도 있으면</b> 게임처럼 누구를 볼지 먼저 묻고,
    /// 아무도 없으면 곧바로 제독의 판을 낸다.
    /// </summary>
    /// <remarks>도시 안이라 함대좌표는 게임처럼 <c>---</c> 다.</remarks>
    private void ShowPerson() => PersonInfoMenu.Show(this, _game, _cityMenu);

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

        BelongingsDialog.Show(this, _player, _game.Items, _game.ItemText, _game.ItemPictures,
                              GameInfo.DiscoveryNames(_game));
    }

    /// <summary>
    /// 계약 정보 창을 낸다. 계약이 없으면 빈 판이 뜬다.
    /// </summary>
    /// <remarks>
    /// 증거품은 계약 중 발견한 것이 준 물건 가운데 <b>아직 지니고 있는</b> 것만 센다 —
    /// 팔아 버렸으면 내밀 증거가 없다. 판에 채울 것은 <see cref="GameInfo.ContractSheetOf"/>
    /// 가 짓는다(지도 창과 한 벌이다).
    /// </remarks>
    private void ShowContract()
    {
        CloseCityMenu();

        // 계약이 없어도 빈 판을 낸다 — 도시 커맨드는 그 자리에서 물리지 않는다.
        var sheet = GameInfo.ContractSheetOf(_game);
        ContractDialog.Show(this, sheet.Contract, _player.Date,
                            sheet.HintName, sheet.Found, sheet.Evidence);
    }

    /// <summary>힌트 이름. 판이 게임 표 · DB · 번호 차례로 물러서며 찾아 준다.</summary>
    private string HintNameOf(int id) => _game.HintName(id);

    /// <summary>얻은 힌트를 늘어놓는다. 이름은 판이 찾아 준다.</summary>
    private void ShowHints() =>
        HintListDialog.Show(_cityMenu.Window ?? this, GameInfo.HintNames(_game));

    /// <summary>
    /// 시설의 명령 창을 짓는다. 줄은 <see cref="Facility"/> 표에서 오고, 어느 줄이 무슨
    /// 일인지는 <see cref="TownWorks"/> 가 안다. 흉내낼 수 있는 것만 손이 달리고 나머지는
    /// 흐린 채로 둔다. 제목은 건물 이름이다(게임도 그렇다).
    /// </summary>
    private GameMenu BuildMenu(Facility facility, string title, uint teachMask, string kind)
    {
        // 어느 줄이 언제 붙고 떨어지는지는 일 표가 안다.
        var patron = PatronAt(kind);
        var items = TownWorks.LinesOf(facility, new TownWorks.TownState(
            Teaches: TownWorks.Teaches(teachMask),
            Poor: _player.Gold < Lodging.OddJobMaxGold,
            CanAnnounce: Port.Announceable().Count > 0,
            PatronRow: patron == null ? null : Patrons.PatronRow(patron)));

        return new GameMenu(title, null,
            [.. items.Select(item => (item, ActionFor(facility, item, teachMask, patron, title, kind)))]);
    }

    /// <summary>이 마을 후원자 — 설득·보고·계약은 자리가 아니라 사람이 든다.</summary>
    private PatronMenu Patrons => _patronMenu ??=
        new PatronMenu(this, _game, _cityName, Menu, _cityMenu, _cityTrack);

    private PatronMenu? _patronMenu;

    /// <summary>이 마을 술집·여관에 앉은 사람들 — 말을 거는 일은 사람 쪽이 든다.</summary>
    private TavernMenu Guests => _guests ??= new TavernMenu(this, _game, _cityId, _culture);

    private TavernMenu? _guests;

    /// <summary>이 마을 항구 — 함대·선원·발표는 도시가 아니라 항구가 한다.</summary>
    private HarborMenu Port => _port ??= new HarborMenu(this, _game, Menu, _cityId);

    private HarborMenu? _port;

    /// <summary>이 마을 자택 — 휴양·저금은 도시가 아니라 집이 한다.</summary>
    private HomeMenu HomeRooms => _home ??= new HomeMenu(this, _game, Menu);

    private HomeMenu? _home;

    /// <summary>
    /// 이 마을 조선소. 배를 손보는 일은 도시가 아니라 조선소가 한다
    /// (<see cref="ShipyardMenu"/>) — 이 창은 명령 창과 시세만 대 준다.
    /// </summary>
    private ShipyardMenu Yard => _yard ??=
        new ShipyardMenu(this, _game, Menu, _cityId, Market?.Rates.Of(_cityId) ?? 100);

    private ShipyardMenu? _yard;

    /// <summary>도시 커맨드 창. 그림 안이 아니라 제 창으로 띄운다.</summary>
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
    private Patron? PatronAt(string kind) => Patrons.At(kind, KindsHere);

    /// <summary>이 도시에 있는 건물 종류들. 후원자를 앉힐 자리를 고를 때 쓴다.</summary>
    private HashSet<string> KindsHere =>
        _kindsHere ??= [.. _table.InCity(_cityId).Select(b => b.Kind)];

    private HashSet<string>? _kindsHere;

    /// <summary>
    /// 그 줄이 하는 일에 손을 달아 준다. 어느 자리의 어느 줄이 무슨 일인지는
    /// <see cref="TownWorks"/> 가 안다 — 여기서는 <b>일마다 무엇을 하는지</b>만 적는다.
    /// 손이 안 달린 일은 null 이라 줄이 흐리게 나온다(보급·회화 따위).
    /// </summary>
    private Action? ActionFor(Facility facility, string item, uint teachMask, Patron? patron,
                              string title, string kind) =>
        TownWorks.WorkOf(facility, item, TownWorks.Teaches(teachMask), patron != null) switch
        {
            TownWork.Exit => CloseMenu,
            TownWork.Train => () => Teach(teachMask, facility.Kind),
            TownWork.System => () => Menu.Push(SystemMenu),
            TownWork.Persuade => () => Patrons.Persuade(patron!),
            TownWork.Report => () => Patrons.Report(patron!),
            TownWork.BreakContract => () => Patrons.BreakContract(patron!),

            TownWork.Sail => () => { Sailed = true; Close(); },
            // 함대편성·선원편성은 제목 없는 창이 한 겹 더 뜬다.
            TownWork.FleetForm => () => Menu.Push(Port.FleetMenu),
            TownWork.CrewForm => () => Menu.Push(Port.CrewMenu),
            TownWork.Announce => Port.Announce,
            TownWork.Supply => () =>
                SupplyDialog.Show(Menu.Window ?? this, _player,
                                  Market?.Rates.Of(_cityId) ?? 100),

            TownWork.BuyShip => () => HullSelectDialog.Show(this, _player),
            TownWork.SellShip when _player.Ships.Count > 1 => Yard.SellShip,
            // 게임도 고칠 배가 없으면 이 줄을 흐리게 둔다(0x0044BD40).
            TownWork.RepairShip when Yard.CanRepair => Yard.RepairShip,
            TownWork.RefitShip => Yard.RefitShip,

            TownWork.BuyGoods when Market != null => () =>
                MarketBuyDialog.Show(this, _player, Market, _cityId, _game.ItemText, _game.ItemPictures),
            TownWork.SellGoods when Market != null && _game.Items != null => () =>
                MarketSellDialog.Show(this, _player, Market, _game.Items, _cityId),

            TownWork.Stay => Stay,
            TownWork.MateForm => () => MateRosterDialog.Show(this, _player),

            TownWork.Rest => () => Menu.Push(HomeRooms.RestMenu),
            TownWork.Savings => () => Menu.Push(HomeRooms.SavingsMenu),
            TownWork.Store => () =>
                StorageDialog.Show(Menu.Window ?? this, _player, _game.Items),

            TownWork.Read when _library != null => () =>
                LibraryDialog.Show(this, _gameDirectory, _cityName, _cityId,
                                   _player, _library, _table, HintNameOf),
            _ => null,
        };

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
