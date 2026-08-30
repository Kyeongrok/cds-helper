using System.Windows;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도서관 — 들어설 때 사서가 건네는 인사와 서가 열람.
/// </summary>
/// <remarks>
/// 게임도 서가를 보여 주기 전에 한마디를 낸다. 대답은 받지 않는다 — 화면에도 단추가
/// "확인" 하나뿐이라 물음이 아니라 인사다.
/// </remarks>
/// <param name="view">이 도서관을 낸 도시 창. 창들의 주인이다.</param>
/// <param name="game">이 판 — 책 표와 화자 얼굴이 여기서 온다.</param>
/// <param name="cityId">이 마을 번호.</param>
/// <param name="cityName">이 마을 이름. 서가 제목에 붙는다.</param>
/// <param name="culture">이 마을 문화권. 사서 얼굴이 여기 따라 갈린다.</param>
/// <param name="buildings">건물 표. 책이 가리키는 건물 이름을 푼다.</param>
internal sealed class LibraryMenu(Window view, Engine.Game game, int cityId, string cityName,
                                  int culture, CityBuildingTable buildings)
{
    /// <summary>도서관의 건물 코드. 화자표에서 사서를 찾을 때 쓴다.</summary>
    private const int BuildingCode = 8;

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly int _cityId = cityId;
    private readonly string _cityName = cityName;
    private readonly int _culture = culture;
    private readonly CityBuildingTable _buildings = buildings;

    /// <summary>사서가 건네는 한마디. 얼굴은 이 마을 문화권이 정한다.</summary>
    public void Greet() =>
        ConfirmDialog.Tell(_view, "책을 찾고 계십니까?",
                           face: _game.SpeakerFace(BuildingCode, _culture));

    /// <summary>책 표를 못 읽었으면 열람 줄이 흐리다.</summary>
    public bool CanRead => _game.Books != null;

    /// <summary>
    /// 서가를 펼친다.
    /// </summary>
    /// <param name="owner">
    /// 서가 창의 주인. <b>명령 창을 넘겨야 한다</b> — 도시 그림 창을 주인으로 삼으면
    /// 서가를 닫을 때 활성 창이 도시 그림으로 갔다가, 명령 창을 누를 때 도로 넘어와서
    /// 창이 한 번 깜빡인다. 보급·소지품 창도 같은 까닭으로 명령 창을 주인으로 쓴다.
    /// </param>
    /// <param name="say">못 읽는 책의 까닭을 낼 곳 — 게임 화면 맨 아래 띠다.</param>
    public void Read(Window owner, Action<string>? say = null)
    {
        if (_game.Books is not { } books) return;

        LibraryDialog.Show(owner, _game.Directory, _cityName, _cityId,
                           _game.Player, books, _buildings, _game.HintName,
                           _game.Book, id => _game.Hints?.Find(id)?.Text ?? "", say);
    }
}
