using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 술집·여관에 앉은 사람들 — 사진 앞에 세우고, 말을 걸고, 한잔 사고, 부하로 삼는다.
/// </summary>
/// <remarks>
/// 값은 <see cref="Tavern"/> 가 알고, 여기서는 묻고 알리는 차례만 맡는다. 사람은
/// 게임 세이브의 인물표에서 오고(<see cref="Engine.Game.Roster"/>), 그림은
/// 손님 그림(<see cref="Engine.Game.Guests"/>)에서 온다.
///
/// 명령 창이 아니라 <b>건물 사진 창</b>에 붙는다 — 게임도 술집에 들어서면 사진 앞에
/// 손님이 서고, 그 사람을 눌러 말을 건다.
/// </remarks>
/// <param name="view">이 술집을 낸 도시 창. 대사 창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 인물표가 여기서 온다.</param>
/// <param name="cityId">이 마을 번호. 그 마을 그 건물에 앉은 사람을 찾는다.</param>
/// <param name="culture">이 마을 문화권 이름. 손님 그림을 고르는 데 쓴다.</param>
/// <param name="cultureNo">이 마을 문화권 번호. 주인 얼굴이 여기 따라 갈린다.</param>
/// <param name="hideMenu">
/// 손님과 이야기하는 동안 시설 명령 창을 접어 두라는 부탁. 게임은 손님을 누르면
/// <b>명령 창을 지우고 그 자리에</b> 고르는 줄을 낸다.
/// </param>
/// <param name="leave">술집을 나가라는 부탁 — 설득을 한 번 하고 나면 게임이 그렇게 한다.</param>
internal sealed class TavernMenu(Window view, Engine.Game game, int cityId, string culture,
                                 int cultureNo, Action<bool>? hideMenu = null,
                                 Action? leave = null)
{
    /// <summary>술집의 건물 코드. 화자표에서 주인을 찾을 때 쓴다.</summary>
    private const int BuildingCode = 4;

    /// <summary>
    /// 들어설 때 손님이 건네는 말. 게임 표(<c>0x005473C0</c>) 다섯 줄 그대로다 —
    /// 하나를 집어 내므로 <b>들어갈 때마다 갈린다</b>.
    /// </summary>
    private static readonly string[] Greetings =
    [
        "여어! 당신, 음...누구였더라? 자, 이쪽으로 오게나.",
        "헤헤, 오늘, 좋은 일이 있었는데 기분좋으니 함께 마시자구.",
        "여어, 함께 마시자구. 오늘 밤은 실컷 마시고 싶은 기분이라네.",
        "으음, 기분좋군. 여, 거기, 자네 말일세, 자네. 이쪽으로 오게 해 둘 말이 있네.",
        "헤에, 너무 마셨나. 거기 자네, 좀더 마시고 싶으니 같이 마십시다.",
    ];

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly int _cityId = cityId;
    private readonly string _culture = culture;
    private readonly int _cultureNo = cultureNo;

    private readonly Action<bool>? _hideMenu = hideMenu;
    private readonly Action? _leave = leave;

    /// <summary>명령 창을 접어 두고 한 가지를 치른 뒤 도로 편다.</summary>
    private void Alone(Action run)
    {
        _hideMenu?.Invoke(true);
        try { run(); }
        finally { _hideMenu?.Invoke(false); }
    }

    private Player _player => _game.Player;

    /// <summary>
    /// 들어설 때 건네는 한마디. 다섯 줄 가운데 하나라 올 때마다 다르다.
    /// </summary>
    /// <remarks>
    /// <b>말하는 이는 술집 주인이 아니라 앉아 있는 손님</b>이다. 말이 "여어, 함께
    /// 마시자구" 인 것부터가 취객의 말이고, 게임도 그렇게 짓는다 — <c>0x0042E976</c> 이
    /// <c>0x004A1D10</c> 으로 <b>손님 자리를 하나 집어</b> 그 자리의 화자로 창을 낸다
    /// (자리 배열은 화면 객체 <c>+0x18</c>, 한 칸 <c>0x18</c> 바이트다).
    ///
    /// 그래서 얼굴도 그 손님 것이다. 우리는 이 술집에 앉은 사람 가운데 첫 사람의 얼굴을
    /// 쓰고, 아무도 안 앉았으면 얼굴 없이 낸다 — 지나가는 손님은 서 있는 그림만 있고
    /// 초상화가 따로 없다.
    ///
    /// 예전에는 화자표의 <b>술집 주인</b> 얼굴을 썼는데, 주인이 할 말이 아니다.
    /// </remarks>
    /// <remarks>
    /// <b>들어설 때마다 말을 걸지는 않는다.</b> 게임은 <c>0x0042E985</c> 에서
    /// <c>rand(3) != 0</c> 이면 그냥 나간다 — <b>세 번에 한 번</b>만 누가 말을 건다.
    /// 그래서 들락거려도 말이 잇달아 나오지 않는다.
    /// </remarks>
    public void Greet()
    {
        if (_game.Random.Next(GreetDice) != 0) return;

        // 자리에서 얼굴을 못 구하면 그 마을 술집 화자로 물러선다 — 게임은 늘
        // 얼굴을 걸고 말하므로 얼굴 없는 창이 뜨는 것이 더 어긋난다.
        var face = DrinkerFace() ?? _game.SpeakerFace(BuildingCode, _cultureNo);
        ConfirmDialog.Tell(_view, Greetings[_game.Random.Next(Greetings.Length)], face: face);
    }

    // ── 술 ──────────────────────────────────────────────────────────────────

    /// <summary>마신 술의 도수를 쌓아 둔다. 이 값이 주량을 넘으면 취한다.</summary>
    private int _tipsy;

    /// <summary>주량 한 칸의 크기(<c>0x0042F027</c> 의 <c>(주량 + 1) x 50</c>).</summary>
    private const int TipsyStep = 50;

    /// <summary>
    /// 술 한 잔. 값을 이르고 좋다면 받아 마신다(<c>0x0042F580</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0042F5CC  값 = 시세 x 표값 / 100, 적어도 1
    ///   0042F5FC  "%s%s 금화 %1d닢이네."  (조사 1 = 은/는) · YES/NO
    ///   0042F611  소지금이 모자라면 "돈 먼저 지불하게."
    ///   0042EFF8  값을 물고
    ///   0042F015  취기 += 도수
    ///   0042F030  취기가 (주량 + 1) x 50 을 넘으면 취한다
    ///   0042F0ED  안 취했으면 다섯 마디 가운데 하나
    /// </code>
    /// <b>피로도는 안 건드린다.</b> "피로가 풀렸다!" 는 그 다섯 마디 중 하나일 뿐이고,
    /// 게임에서도 마신다고 피로가 풀리지는 않는다 — 피로는 자택 휴양이 푼다.
    /// </remarks>
    /// <param name="drink">표에서 고른 술.</param>
    /// <param name="shown">줄에 적힌 이름. 그 고장 말을 모르면 별칭이다.</param>
    public void Drink(DrinkTable.Drink drink, string shown) => Alone(() =>
    {
        int price = Math.Max(_game.Rates.Of(_cityId) * drink.Price / 100, 1);
        var face = DrinkerFace() ?? _game.SpeakerFace(BuildingCode, _cultureNo);

        if (!ConfirmDialog.Ask(_view, $"{shown}{GameUi.Josa(shown, "은", "는")} 금화 {price}닢이네.",
                               face: face))
            return;

        if (_player.Gold < price)
        {
            ConfirmDialog.Tell(_view, "돈 먼저 지불하게.", face: face);
            return;
        }

        _player.Pay(price);
        if (drink.Proof <= 0) return;

        // 마신 뒤에 뜨는 말은 <b>얼굴이 없다</b> — 값을 이르는 창은 얼굴을 걸고 부르지만
        // (0x4692E0), 이쪽은 얼굴 없는 알림이다(0x469060, 인자가 둘뿐이다).
        _tipsy += drink.Proof;
        if (_tipsy > TipsyStep)
        {
            ConfirmDialog.Tell(_view, "기분이 좋아졌다.........");
            Drunk();
            return;
        }

        ConfirmDialog.Tell(_view, Sips[_game.Random.Next(Sips.Length)]);
    });

    /// <summary>
    /// 취하고 나서 벌어지는 일(<c>0x0042F046</c>).
    /// </summary>
    /// <remarks>
    /// 차례가 이렇다.
    /// <list type="number">
    ///   <item><b>부관이 깨운다</b>(<c>0x0042EA40</c>) — 부관이 있고 굴림에 걸릴 때.</item>
    ///   <item><b>부인이 깨운다</b>(<c>0x0042EAA0</c>) — 여기가 자택이 있는 도시일 때.
    ///         우리는 아직 혼인을 안 다루므로 이 갈래는 건너뛴다.</item>
    ///   <item>아니면 <b>다섯 가지 가운데 하나</b>(<c>0x0042F07F</c>). 소지금이 100닢
    ///         이하면 넷 중에서 뽑는다 — 한턱은 낼 돈이 있어야 낸다.</item>
    /// </list>
    /// 게임은 취한 뒤 화면을 어둡게 했다 밝히는데(<c>0x004A59F0</c>), 우리는 말 창만 낸다.
    /// </remarks>
    private void Drunk()
    {
        _tipsy = 0;                                   // 한 번 뻗으면 취기가 가신다

        string first = _player.MateAt(0);
        bool hasMate = first.Length > 0;
        var mate = hasMate && _player.MateInfoOf(first) is { } who ? MateFace(who) : null;

        // 부관이 깨운다 — 게임은 능력치로 굴리는데(0x0042F1A7) 우리는 부관 유무만 본다.
        if (hasMate && _game.Random.Next(2) == 0)
        {
            ConfirmDialog.Tell(_view, "제독! 이봐요, 제독! 괜찮습니까?", face: mate);
            ConfirmDialog.Tell(_view, "부관의 목소리에 정신이 들었다");
            _player.Tire(_game.Random.Next(5) + 5);
            _player.Infamy += _game.Random.Next(5);
            return;
        }

        int pick = _game.Random.Next(_player.Gold > TreatFloor ? DrunkKinds : DrunkKinds - 1);
        switch (pick)
        {
            case 0: PassOut(hasMate, mate); break;
            case 1: PickFight(mate); break;
            case 2: ThrowUp(hasMate, mate); break;
            case 3: FoundMoney(hasMate, mate); break;
            default: BuyRound(mate); break;
        }
    }

    /// <summary>취해서 벌어지는 가짓수와, 한턱이 나오려면 있어야 할 소지금.</summary>
    private const int DrunkKinds = 5, TreatFloor = 100;

    /// <summary>뻗는다 — 깨어 보니 돈이 없다(<c>0x0042EB50</c>).</summary>
    /// <remarks>부관이 지켜 주면 소지금의 <b>1/5</b>, 혼자면 <b>절반</b>을 잃는다.</remarks>
    private void PassOut(bool hasMate, uint[]? mate)
    {
        int lost = hasMate ? _player.Gold / 5 : _player.Gold / 2;
        _player.Pay(lost);

        if (hasMate)
        {
            ConfirmDialog.Tell(_view, "제독! 이봐요. 제독! 괜찮습니까?", face: mate);
            ConfirmDialog.Tell(_view, "부관 목소리에 정신이 들었다...");
        }
        else
        {
            ConfirmDialog.Tell(_view, "손님, 손님! 일어나세요. 벌써 아침이에요.", face: HostFace());
            ConfirmDialog.Tell(_view, "가게 주인이 깨웠다...");
        }
        if (lost > 0) ConfirmDialog.Tell(_view, "돈을 도둑 맞았다!!");
    }

    /// <summary>시비가 붙어 일기토가 벌어진다(<c>0x0042EC10</c>).</summary>
    /// <remarks>
    /// 게임은 걸 사람을 그 자리에서 고르고 <b>싸움 창</b>을 띄운다. 우리는 아직 취중
    /// 일기토를 붙이지 않고 말만 낸다 — 거는 말은 게임 것 그대로 둘 중 하나다.
    /// </remarks>
    private void PickFight(uint[]? mate)
    {
        int k = _game.Random.Next(Taunts.Length);
        ConfirmDialog.Tell(_view, Taunts[k]);
        if (_player.MateAt(0).Length > 0) ConfirmDialog.Tell(_view, MateStops[k], face: mate);
    }

    /// <summary>토하고 뻗는다 — 여관에서 깨고 돈과 이름을 잃는다(<c>0x0042ED30</c>).</summary>
    private void ThrowUp(bool hasMate, uint[]? mate)
    {
        ConfirmDialog.Tell(_view, "기분이 나쁘다......눈이 도는군~ ~우웩~");

        int tire, lost;
        if (hasMate)
        {
            ConfirmDialog.Tell(_view, "제독, 괜찮습니까! 얼굴이 새파랗습니다. 제독, 제독!", face: mate);
            tire = _game.Random.Next(10) + 10;
            lost = 0;                                  // 부관이 있으면 돈은 안 털린다
        }
        else
        {
            ConfirmDialog.Tell(_view, "손님, 괜찮습니까! 얼굴이 새파랍니다, 손님, 손님!", face: HostFace());
            tire = _game.Random.Next(10) + 20;
            lost = Math.Min(_player.Gold, _game.Random.Next(10) + 20);
        }

        _player.Tire(tire);
        _player.Pay(lost);
        _player.Infamy += _game.Random.Next(30) + 10;

        ConfirmDialog.Tell(_view, hasMate
            ? "부관 목소리에 정신이 들었다......어쩐지 여관같군."
            : "정신이 드는군.....아무래도 여관같군. 어떻게 여기까지 왔는지 전혀 생각이 나지 않는다.");
    }

    /// <summary>깨어 보니 모르는 돈을 쥐고 있다(<c>0x0042EF00</c>). 악명이 오른다.</summary>
    private void FoundMoney(bool hasMate, uint[]? mate)
    {
        int got = _game.Random.Next(100) + 100;

        if (hasMate)
        {
            ConfirmDialog.Tell(_view, "제독! 이봐요, 제독! 괜찮습니까?", face: mate);
            ConfirmDialog.Tell(_view, "부관 목소리에 정신이 들었다.....");
            ConfirmDialog.Tell(_view, "기억에 없는 돈을 쥐고 있었군!");
            ConfirmDialog.Tell(_view, "제독! 어떻게 된 것입니까? 그 돈...", face: mate);
        }
        else
        {
            ConfirmDialog.Tell(_view, "손님, 손님! 일어나세요. 벌써 아침이에요!", face: HostFace());
            ConfirmDialog.Tell(_view, "가게 주인이 깨웠다.");
            ConfirmDialog.Tell(_view, "기억에 없는 돈을 쥐고 있었군!");
        }

        ConfirmDialog.Tell(_view, $"금화 {got}닢을 손에 넣었다!");
        _player.Earn(got);
        _player.Infamy += hasMate ? FoundInfamyMate : FoundInfamyAlone;
    }

    /// <summary>모르는 돈을 쥐었을 때 오르는 악명 — 부관이 있으면 더 크다.</summary>
    private const int FoundInfamyMate = 100, FoundInfamyAlone = 50;

    /// <summary>한턱 낸다(<c>0x0042EE60</c>) — 돈을 쓰고 이름이 크게 오른다.</summary>
    private void BuyRound(uint[]? mate)
    {
        ConfirmDialog.Tell(_view, "여~어, 주인! 여기에 있는 자들에게 한잔씩 돌리게.", face: HostFace());

        if (_player.MateAt(0).Length > 0)
            ConfirmDialog.Tell(_view, "역시 제독! 그럼 사양하지 않겠습니다.", face: mate);
        else
            ConfirmDialog.Tell(_view, "여어, 자네 마음에 들었어! 주인! 술 더 가지고 오게!",
                               face: DrinkerFace());

        _player.Pay(Math.Min(_player.Gold, _game.Random.Next(50) + 50));
        _player.Fame += _game.Random.Next(100) + 100;
        _player.Infamy += _game.Random.Next(6);
    }

    /// <summary>술집 주인 얼굴. 화자표에서 온다.</summary>
    private uint[]? HostFace() => _game.SpeakerFace(BuildingCode, _cultureNo);

    /// <summary>시비 거는 말과, 부관이 말리는 말(<c>0x0042EC39</c>). 짝이 맞는 둘 중 하나다.</summary>
    private static readonly string[] Taunts =
    [
        "어이, 거기 너! 마음에 안드는군. 나와 결투하자.",
        "거기 겁장이! 남자라면 검을 뽑아라.",
    ];

    private static readonly string[] MateStops =
    [
        "제, 제독, 갑자기 무슨 말씀을 하시는 겁니까?",
        "잠깐, 잠깐만 제독! 농담이 지나치십니다.",
    ];

    /// <summary>
    /// 안 취했을 때 나오는 다섯 마디(<c>0x0042F0ED</c>). 하나를 집어 낸다.
    /// </summary>
    private static readonly string[] Sips =
    [
        "기분 좋군!",
        "꽤 맛있는 술이다!",
        "피로가 풀렸다!",
        "맛있다! 살 것 같다.",
        "몸이 따뜻해졌다!",
    ];

    /// <summary>말을 걸 낯을 가리는 주사위 — 셋에 하나다.</summary>
    private const int GreetDice = 3;

    /// <summary>
    /// 말을 거는 취객의 얼굴 — <b>이름 없는 손님</b> 가운데 맨 앞자리 사람이다.
    /// </summary>
    /// <remarks>
    /// 게임은 자리 배열을 앞에서부터 훑어 <b>인물이 안 앉은 첫 자리</b>를 집는다
    /// (<c>0x004A1D10</c>).
    /// <code>
    ///   자리 한 칸 24바이트, 배열은 화면 객체 +8
    ///   +0x00  얼굴 번호          +0x04  쓰는 말(언어)
    ///   +0x0C  인물 객체          0 이면 지나가는 손님
    ///   +0x10  서 있는 그림 번호  -1 이면 빈 자리
    ///
    ///   0x4A1D20  [자리+0x10] == -1 이면 건너뛴다
    ///   0x4A1D25  [자리+0x0C] == 0 인 첫 자리를 집는다
    /// </code>
    /// 그 자리를 <c>0x004690A0</c> 에 그대로 넘기면 <c>+0x00</c> 을 얼굴로 쓴다.
    ///
    /// <b>이름난 사람도 여급도 아니다.</b> 여급은 인물표에 있는 사람이라 <c>+0x0C</c> 가
    /// 차 있고, 앉아 있는 항해자들도 마찬가지다 — 그래서 말을 거는 것은 늘 지나가는
    /// 술꾼이다. 예전에는 여기서 <b>앉아 있는 첫 인물</b>의 얼굴을 써서 엉뚱한 사람이
    /// 말을 걸었다.
    ///
    /// <b>얼굴 번호는 우리가 정한다.</b> 게임은 자리를 지을 때 <c>+0x00</c> 에 얼굴을
    /// 박아 두는데 그 자리를 아직 못 짚었다. 대신 서 있는 그림 번호에서 뽑아 쓴다 —
    /// 같은 마을이면 늘 같은 얼굴이 나온다.
    /// </remarks>
    private uint[]? DrinkerFace()
    {
        if (_game.Guests is not { } book || _game.Faces is not { } faces) return null;

        var people = _game.Roster?.At(_cityId, TavernRoster.Tavern) ?? [];
        var keys = new List<int>(people.Count);
        foreach (var p in people) keys.Add(p.Index);

        bool first = true;
        foreach (var seat in book.Seat(_culture, _cityId, keys))
        {
            if (seat.Person >= 0) { first = false; continue; }

            // 맨 앞 여자 자리는 이 마을 여급이다 — 게임에서는 그쪽도 인물이라 건너뛴다.
            if (first && seat.Art.Female && Standing() != null) { first = false; continue; }
            first = false;

            int count = faces.MaleCount;
            if (count <= 0) return null;
            return faces.TryGetBgra(seat.Art.Index % count, female: false);
        }
        return null;
    }

    /// <summary>
    /// 사진 앞에 세울 손님들. 술집·여관이 아니거나 그림을 못 읽었으면 빈 목록이다.
    /// </summary>
    /// <remarks>
    /// 세이브에 그 도시 그 건물로 적힌 인물을 자리에 앉히고(<see cref="TavernRoster"/>),
    /// 남는 자리는 지나가는 손님으로 채운다. 이름표는 인물이면 그 이름, 아니면 성별이다.
    /// </remarks>
    public IReadOnlyList<BuildingPhotoWindow.GuestArt> GuestArt(FacilityKind kind)
    {
        if (kind is not (FacilityKind.Tavern or FacilityKind.Inn)) return [];

        var book = _game.Guests;
        if (book == null) return [];

        byte building = kind == FacilityKind.Tavern ? TavernRoster.Tavern : TavernRoster.Inn;
        var people = _game.Roster?.At(_cityId, building) ?? [];
        var keys = new List<int>(people.Count);
        foreach (var p in people) keys.Add(p.Index);

        var art = new List<BuildingPhotoWindow.GuestArt>(TavernGuests.MaxOnScreen);
        var maid = kind == FacilityKind.Tavern ? Standing() : null;
        bool maidSeated = false;

        foreach (var seat in book.Seat(_culture, _cityId, keys, withMaid: maid != null))
        {
            var bgra = book.TryGetBgra(seat.Art);
            if (bgra == null) continue;

            // 자리 짓는 쪽이 맨 앞에 여자를 세운다 — 술집이면 그 자리가 이 마을 여급이다.
            if (seat.Person < 0 && seat.Art.Female && !maidSeated && maid is { } her)
            {
                maidSeated = true;
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height,
                            _player.LikingOf(her.Id) > 0 ? her.Name : "여",
                            () => Alone(() => MeetBarmaid(her))));
                continue;
            }

            if (seat.Person < 0)
            {
                string label = seat.Art.Female ? "여" : "남";
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height, label,
                            () => Alone(() => MeetStranger(seat.Art.Female))));
            }
            else
            {
                // 낯을 트기 전에는 이름이 안 보인다 — 이름표도 "남"·"여" 다.
                var who = people[seat.Person];
                bool known = Known(who);
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height,
                            known ? who.Name : seat.Art.Female ? "여" : "남",
                            () => Alone(() => MeetPerson(who, seat.Art.Female))));
            }
        }
        return art;
    }

    // ── 여급 ────────────────────────────────────────────────────────────────

    /// <summary>이 마을 술집에 지금 서 있는 여급. 표를 못 읽었거나 없으면 null.</summary>
    private BarmaidTable.Barmaid? Standing() =>
        _game.Barmaids?.Standing(_cityId, _player.Date.Year);

    /// <summary>여급 얼굴. FEMALE.CDS 에서 낸다.</summary>
    private uint[]? FaceOfMaid(in BarmaidTable.Barmaid her) =>
        _game.Faces?.TryGetBgra(her.Face, female: true);

    /// <summary>
    /// 여급을 눌렀을 때.
    /// </summary>
    /// <remarks>
    /// 게임 차례 그대로다.
    /// <code>
    ///   낯 트기 전   "아름다운 여성이 있다" → 한잔 산다 · 무시한다
    ///   한잔 사면    그제야 얼굴을 내고 이름을 밝힌다 — 궁합대로 말투가 갈린다
    ///   그 뒤로      이야기한다 · 설득한다 · 떠난다
    /// </code>
    /// 낯 트기 전은 지나가는 여성과 <b>똑같이</b> 나온다 — 얼굴도 이름도 안 보인다.
    /// 게임의 선물 창(<c>0x00466AC9</c> "무엇을 보내시겠습니까?")은 이 세 줄에 없어
    /// 아직 안 붙였다. 어디서 뻗는지 못 찾았다.
    /// </remarks>
    private void MeetBarmaid(BarmaidTable.Barmaid her)
    {
        bool destined = Barmaids.Destined(_player, her);

        // 낯 트기 전에는 얼굴도 이름도 없다.
        if (_player.LikingOf(her.Id) == 0)
        {
            if (TalkDialog.Ask(_view, null, "", "아름다운 여성이 있다",
                               "한잔 산다", "무시한다") != 0) return;
            if (!BuyDrink()) return;

            _player.AddLiking(her.Id, Barmaids.FirstMeet(_player, her));
        }

        var face = FaceOfMaid(her);
        string words = destined
            ? $"고마워요! 저는 {her.Name}. 물어보고 싶은 것이 있으면 뭐든지 물어보세요."
            : $"아, 고마워요. 저는 {her.Name}. 무슨 일이시죠?";

        while (true)
        {
            switch (TalkDialog.Ask(_view, face, "", words,
                                   "이야기한다", "설득한다", "떠난다"))
            {
                case 0: Chat(her, destined); break;

                // 설득은 한 번뿐이다 — 되든 안 되든 그 자리에서 술집을 나온다.
                case 1: Woo(her, face); _leave?.Invoke(); return;

                default: return;
            }
            // 한 번 인사를 나눈 뒤로는 <b>줄만 다시 뜬다</b> — 게임은 "무슨 일이시죠?" 를
            // 되풀이하지 않는다. 빈 글이면 대사 창을 건너뛴다(TalkDialog.Ask).
            words = "";
        }
    }

    /// <summary>잡담. 게임 표(<c>0x0055B0C0</c> 벌)에서 한 줄을 집는다.</summary>
    private void Chat(in BarmaidTable.Barmaid her, bool destined)
    {
        TalkDialog.Say(_view, FaceOfMaid(her), "", Chats[_game.Random.Next(Chats.Length)]);
        _player.AddLiking(her.Id, Barmaids.ChatLike(destined));
    }

    /// <summary>여급이 건네는 잡담. 게임 것을 그대로 옮겼다.</summary>
    private static readonly string[] Chats =
    [
        "취해서 괴롭히는 손님들이 있어요, 정말 싫어!",
        "옛, 애인? 비-밀.",
        "어디 멋있는 사람 없나~.",
        "모험이 그렇게 재미있어요? 그럼 이야기 들려줘요.",
        "배는 가지고 있어요? 배가 없다면 멋이 없죠.",
        "당신 좋아하는 사람있죠? 얼굴에 씌어 있어요.",
        "이 가게 술은 맛있어요. 많이 드세요.",
        "바다의 사나이란, 전부 「여자보다 바다다!」 라고 말해요. 실례되는 말이야.",
        "이 마을의 하늘은 별이 총총한게 매우 아름다워요!",
        "나도 배 타보고 싶어.",
        "역시 좋아하는 사람의 배에 타고 싶어.",
        "매일 같은 일 반복이야···나도 다른 마을에 가고 싶어~.",
        "결혼? 그런 것 생각해 본 일도 없어요.",
        "어떤 여자를 좋아해요? 가르쳐 줘요.",
    ];

    /// <summary>
    /// 설득한다. 친밀도가 차 있으면 맺어지고, 아니면 물린다.
    /// </summary>
    /// <returns>맺어졌으면 true — 그러면 창을 접는다.</returns>
    /// <remarks>
    /// 설득의 말은 문화권마다 딴 벌이다(<c>0x0055B9B8</c> 벌 — 게임 문자열에 "지중해의
    /// 유혹어" 라는 이름이 그대로 박혀 있다). 물릴 때 하는 말 셋도 게임 것이다
    /// (<c>0x0055BFB0</c>).
    ///
    /// <b>문턱은 우리가 정했다</b> — 게임에서 그 자리를 아직 못 짚었다.
    /// </remarks>
    private void Woo(in BarmaidTable.Barmaid her, uint[]? face)
    {
        // 유혹의 말은 <b>제독이 하는 것</b>이라 대사 창에 안 뜬다 — 여급 얼굴을 걸고
        // 제독이 말하는 창은 게임에 없다. 바로 대답부터 나온다.
        if (_player.LikingOf(her.Id) < Barmaids.WooNeeded || _player.Spouse.Length > 0)
        {
            TalkDialog.Say(_view, face, "",
                           Barmaids.Refusals[_game.Random.Next(Barmaids.Refusals.Length)]);
            return;
        }

        _player.Marry(her.Name, her.Id);
        TalkDialog.Say(_view, face, "", Barmaids.Yes);
        ConfirmDialog.Tell(_view, string.Format(Barmaids.Married, _player.Name, her.Name));
    }

    /// <summary>
    /// 이름 없는 손님을 눌렀을 때. 게임 문구를 그대로 옮겼다(<c>0x0054AC40</c>·<c>0x0054AB98</c>).
    /// </summary>
    private void MeetStranger(bool female)
    {
        // 여자 손님은 예전 그대로 — 한잔 사서 낯을 트는 자리다.
        if (female)
        {
            if (TalkDialog.Ask(_view, null, "", "아름다운 여성이 있다",
                               "한잔 산다", "무시한다") == 0) BuyDrink();
            return;
        }

        // 무명 손님은 <b>소문만</b> 건넨다 — 고용도 결투도 없다.
        var face = DrinkerFace() ?? _game.SpeakerFace(BuildingCode, _cultureNo);
        ConfirmDialog.Tell(_view, TavernRumors.Of(_cultureNo, _game.Random), face: face);
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
            if (TalkDialog.Ask(_view, null, "", seen, "한잔 산다", "무시한다") != 0) return;
            if (BuyDrink() && _player.Meet(who.Name))
                TalkDialog.Say(_view, face, "", $"고맙네. 나는 {who.Name}. 잘 부탁하네.");
            return;
        }

        if (TalkDialog.Ask(_view, face, "", $"[{who.Name}]{Subject(who.Name)} 있다",
                           "말을 건다", "무시한다") != 0) return;

        bool hireable = who.Hire == TavernRoster.Hireable;
        string[] choices = hireable
            ? ["정보를 듣는다", "부하로 고용한다", "일기토를 신청한다", "떠난다"]
            : ["정보를 듣는다", "일기토를 신청한다", "떠난다"];
        int duelAt = hireable ? 2 : 1;

        int at = TalkDialog.Ask(_view, face, "", "무슨 용건인가?", choices);
        if (at == duelAt) { Duel(who, face); return; }

        switch (at)
        {
            case 0:
                // 게임은 여기서 발견물 실마리를 주는데 우리는 아직 그 자리를 못 흉내낸다.
                // 대신 세이브에 적힌 그 사람 됨됨이를 이른다. 나이는 값이 이상한 칸이
                // 더러 있어(등장 전 인물) 말이 될 때만 말한다.
                TalkDialog.Say(_view, face, "", who.Age is > 0 and < 120
                                   ? $"나 말인가. {who.Name}. 올해 {who.Age}이네. 이름값은 {who.Fame} 쯤 하지."
                                   : $"나 말인가. {who.Name}. 이름값은 {who.Fame} 쯤 하지.");
                break;
            case 1 when hireable:
                Hire(who, face);
                break;
        }
    }

    /// <summary>
    /// 일기토를 신청한다(<c>0x004A4AA0</c> 의 둘째 줄).
    /// </summary>
    /// <remarks>
    /// 게임 차례 그대로다.
    /// <list type="number">
    ///   <item>부관이 있으면 "제독! 진심이십니까?", 없으면 "일기토를 신청합니다.
    ///         좋습니까?" 로 한 번 되묻는다(<c>0x004A4B6A</c>).</item>
    ///   <item>상대가 달아나려 든다 — <b>체력에 주사위 오십씩</b>을 얹어 견주고 못
    ///         미치면 놓친다(<c>0x004A494B</c>).</item>
    ///   <item>판이 열린다(<see cref="DuelDialog"/>).</item>
    ///   <item>이기면 그것으로 끝이다. <b>술집 일기토는 처형·놓아 준다·모두 뺏는다가
    ///         안 뜬다</b> — 그 줄은 판 종류가 7 아래일 때만 나오는데 술집에서 신청한
    ///         판은 8 이다(<c>0x004AA2B1</c>).</item>
    ///   <item>지면 도망·용서·죽음으로 갈린다(<see cref="Engine.Town.Duel.FateOf"/>).</item>
    ///   <item>이기든 지든 <b>남은 부위의 평균만큼 체력이 준다</b>.</item>
    /// </list>
    /// </remarks>
    private void Duel(TavernRoster.Person who, uint[]? face)
    {
        string ask = _player.MateCount > 0
            ? "제독! 진심이십니까?"
            : "일기토를 신청합니다. 좋습니까?";
        if (!ConfirmDialog.Ask(_view, ask, "일기토", face)) return;

        var dice = new GameRandom(Environment.TickCount);
        if (!Engine.Town.Duel.Caught(_player.AbilityOf(Ability.Body), who.Body, dice))
        {
            TalkDialog.Say(_view, face, "",
                           $"{who.Name}{Subject(who.Name)} 도망쳤다!");
            return;
        }

        var mate = SendMate(dice);
        var duel = new Engine.Town.Duel(mate is { } m ? MateSide(m) : Mine(),
                                        Theirs(who), Shielded(), Environment.TickCount);
        DuelDialog.Show(_view, duel, dice, face, _game.Fighters,
                        FighterSprites.SetForCulture(_cultureNo),
                        myFace: _game.Faces?.TryGetBgra(_player.Face, female: false),
                        arena: "duel-tavern");

        int lost = duel.BodyLost;

        if (duel.Won == true)
        {
            TalkDialog.Say(_view, face, "", Beaten[dice.Next(Beaten.Length)]);
        }
        else
        {
            switch (duel.FateOf(_player.Fame))
            {
                case Engine.Town.Duel.Fate.Fled:
                    TalkDialog.Say(_view, face, "",
                                   "안되겠다. 이길 수가 없군! 틈을 봐서 도망쳐야겠다!");
                    break;
                case Engine.Town.Duel.Fate.Spared:
                    TalkDialog.Say(_view, face, "", Spared[dice.Next(Spared.Length)]);
                    break;
                default:
                    // 게임은 여기서 놀이가 끝난다(0x004AA17B 의 [+0x1C0]=3). 우리는
                    // 아직 그 끝을 안 지어서, 몸만 겨우 건진 것으로 둔다.
                    TalkDialog.Say(_view, face, "", "자네, 제독감이 아니로군. 물고기의 먹이가 더 어울리는군.");
                    lost = Math.Max(lost, _player.AbilityOf(Ability.Body) - 1);
                    break;
            }
        }

        // 대신 나간 사람이 다친다.
        if (mate is { } hurt) _player.HurtMate(hurt.Name, lost);
        else _player.Hurt(lost);
    }

    /// <summary>
    /// 부관을 대신 내보낼지 묻는다(<c>0x004A8611</c>).
    /// </summary>
    /// <remarks>
    /// 제독이 부관보다 세면 <b>부관이 꺼린다</b> — 그 말을 하고 한 번 더 묻는다. 세기는
    /// <c>(무력+1)/20 + 검술*10</c> 으로 잰다(<c>0x004A86CD</c>). 부관이 더 세면 흔쾌히
    /// 나서고 다시 묻지 않는다.
    /// </remarks>
    private Player.MateInfo? SendMate(GameRandom dice)
    {
        string first = _player.Mates.FirstOrDefault(m => m.Length > 0) ?? "";
        if (first.Length == 0 || _player.MateInfoOf(first) is not { } mate) return null;
        if (!ConfirmDialog.Ask(_view, "　부관을 싸우게 하겠습니까?", "일기토")) return null;

        int mine = (_player.AbilityOf(Ability.Might) + 1) / MateEdge
                 + _player.LevelOf(Skill.Names[Skill.Sword]) * MateSwordWeight;
        int theirs = (mate.Might + 1) / MateEdge + mate.Sword * MateSwordWeight;

        var face = _player.MateInfoOf(first) is { } who ? MateFace(who) : null;
        if (mine <= theirs)
        {
            TalkDialog.Say(_view, face, "", MateEager[dice.Next(MateEager.Length)]);
            return mate;
        }

        TalkDialog.Say(_view, face, "", MateShy[dice.Next(MateShy.Length)]);
        return ConfirmDialog.Ask(_view, "　부관을 싸우게 하겠습니까?", "일기토") ? mate : null;
    }

    /// <summary>부관 세기를 재는 잣대 — 무력을 스물로 나누고 검술에 열을 곱한다.</summary>
    private const int MateEdge = 20, MateSwordWeight = 10;

    /// <summary>부관이 꺼릴 때 하는 말(<c>0x005341F8</c> 다섯).</summary>
    private static readonly string[] MateShy =
    [
        "옛, 저 말입니까? 제독이 더 강하지 않습니까?",
        "그다지 자신은 없지만, 해 보겠습니다.",
        "제가 일기토를? 제독보다 약한 제가 말입니까?",
        "저보고 싸우라고요? 제독이 나가는 편이 이길 확률이 높을 텐데요.",
        "일기토 말입니까... 제독이 나가는 편이 나을 거라 생각합니다만...",
    ];

    /// <summary>부관이 나설 때 하는 말(<c>0x00534330</c> 다섯).</summary>
    private static readonly string[] MateEager =
    [
        "저에게 맡겨 주십시오! 기필코 이기겠습니다.",
        "저를 지명하리라고는, 역시 제독이십니다.",
        "봐 주십시오. 저런 녀석은 한번에 쓰러뜨리겠습니다.",
        "제가 활약할 때가 온 것 같군요, 맡겨 주십시오.",
        "저런 녀석, 혼 줄을 내버리겠습니다.",
    ];

    /// <summary>부관 몫. 무기·방어구는 제독의 것을 그대로 쓴다(게임도 그렇다).</summary>
    private Engine.Town.Duel.Fighter MateSide(in Player.MateInfo mate) =>
        new(mate.Name, mate.Body, mate.Might, mate.Sword, mate.Luck,
            Best(Engine.Town.Duel.WeaponCategory), Best(Engine.Town.Duel.ArmorCategory));

    /// <summary>부관 얼굴. 못 구하면 null.</summary>
    private uint[]? MateFace(in Player.MateInfo who) =>
        _game.Faces?.TryGetBgra(who.Face, female: false);

    /// <summary>이겼을 때 상대가 남기는 말(<c>0x005348A8</c> 다섯).</summary>
    private static readonly string[] Beaten =
    [
        "제길, 기억해 두어라.",
        "오늘은 여기까지 해 두지. 그럼.",
        "다음 번엔 어림도 없다, 각오해라.",
        "이 굴욕은 잊지 않겠다.",
        "나를 죽이지 않은 것을 언젠가 후회하게 해 주마.",
    ];

    /// <summary>졌는데 봐 줄 때 하는 말(<c>0x005346E8</c> 다섯).</summary>
    private static readonly string[] Spared =
    [
        "칫, 병아린가. 용서해 주지.",
        "너 같은 녀석 죽여도 자랑할게 못된다.",
        "여자와 아이, 약한 자들은 죽이지 않는 주의라서...",
        "너 같은 녀석 죽일 가치도 없다. 빨리 사라져라.",
        "이번만은 용서해 주지. 좀더 힘을 길러라.",
    ];

    /// <summary>내 몫 — 능력치와 검술, 그리고 지닌 무기·방어구 가운데 가장 센 것.</summary>
    private Engine.Town.Duel.Fighter Mine() =>
        new(_player.Name.Length > 0 ? _player.Name : "제독",
            _player.AbilityOf(Ability.Body),
            _player.AbilityOf(Ability.Might),
            _player.LevelOf(Skill.Names[Skill.Sword]),
            _player.AbilityOf(Ability.Luck),
            Best(Engine.Town.Duel.WeaponCategory),
            Best(Engine.Town.Duel.ArmorCategory));

    /// <summary>상대 몫. 세이브에 적힌 능력치와 검술을 그대로 쓴다.</summary>
    private static Engine.Town.Duel.Fighter Theirs(in TavernRoster.Person who) =>
        new(who.Name, who.Body, who.Might, who.Sword, who.Luck, 0, 0);

    /// <summary>이디스의 방패를 지녔는가 — 스친 것이 막은 것이 된다.</summary>
    private bool Shielded() => _player.Items.Contains(Engine.Town.Duel.EdithShieldId);

    /// <summary>지닌 것 가운데 그 갈래에서 가장 센 효과. 표를 못 읽었으면 0.</summary>
    private int Best(int category)
    {
        if (_game.Items is not { } table) return 0;
        int best = 0;
        foreach (int id in _player.Items)
            if (table.Find(id) is { } item && item.Category == category && item.Effect > best)
                best = item.Effect;
        return best;
    }

    /// <summary>
    /// 부하로 삼는다. 게임은 명성이 그 사람에 못 미치면 물린다 —
    /// <see cref="Support.Local.Models.CharacterData.CanRecruit"/> 와 같은 잣대다.
    /// </summary>
    private void Hire(TavernRoster.Person who, uint[]? face)
    {
        if (_player.HasMate(who.Name))
        {
            TalkDialog.Say(_view, face, "", $"{who.Name}{Subject(who.Name)} 이미 자네 사람이 아닌가.");
            return;
        }

        if (_player.MateCount >= Player.MaxMates)
        {
            TalkDialog.Say(_view, face, "", "자네 배에는 이미 사람이 넘치지 않는가.");
            return;
        }

        if (_player.Fame < who.Fame)
        {
            TalkDialog.Say(_view, face, "", "자네 이름은 들어 본 적이 없군. 더 이름을 알리고 오게.");
            return;
        }

        int slot = AskMateSlot(face);
        if (slot < 0) return;                       // 물렀다

        _player.SetMate(slot, who.Name);
        // 됨됨이를 지금 베껴 둔다 — 나중에 인물정보를 낼 때 게임 세이브를 다시 안 뒤지게.
        _player.RememberMate(Tavern.MateInfoOf(who));
        TalkDialog.Say(_view, face, "",
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

        int picked = TalkDialog.Ask(_view, face, "", "어느 자리에 앉히겠나?", choices);
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
    /// <remarks>
    /// <b>샀다고 알리지 않는다.</b> 게임에는 그런 문구가 없다 — 값만 빠지고 곧바로
    /// 상대가 말을 잇는다. 돈이 모자랄 때 물리는 "돈 먼저 지불하게."(<c>0x0054AC98</c>)만
    /// 게임 것이다.
    /// </remarks>
    public bool BuyDrink()
    {
        if (_player.Gold < Tavern.DrinkPrice)
        {
            ConfirmDialog.Tell(_view, "돈 먼저 지불하게.");
            return false;
        }
        _player.SetGold(_player.Gold - Tavern.DrinkPrice);
        return true;
    }

}
