using System.Windows;
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
internal sealed class TavernMenu(Window view, Engine.Game game, int cityId, string culture,
                                 int cultureNo)
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

    private Player _player => _game.Player;

    /// <summary>들어설 때 건네는 한마디. 다섯 줄 가운데 하나라 올 때마다 다르다.</summary>
    public void Greet() =>
        ConfirmDialog.Tell(_view, Greetings[_game.Random.Next(Greetings.Length)],
                           face: _game.SpeakerFace(BuildingCode, _cultureNo));

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
        if (TalkDialog.Ask(_view, null, "", seen, "한잔 산다", "무시한다") == 0) BuyDrink();
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
            ? ["정보를 듣는다", "부하로 고용한다", "떠난다"]
            : ["정보를 듣는다", "떠난다"];

        switch (TalkDialog.Ask(_view, face, "", "무슨 용건인가?", choices))
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
    public bool BuyDrink()
    {
        if (_player.Gold < Tavern.DrinkPrice)
        {
            NoticeDialog.Show(_view, "돈 먼저 지불하게.");
            return false;
        }
        _player.SetGold(_player.Gold - Tavern.DrinkPrice);
        NoticeDialog.Show(_view, $"금화 {Tavern.DrinkPrice}닢으로 한잔 샀다.");
        return true;
    }

}
