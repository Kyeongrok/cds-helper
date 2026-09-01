using System.Windows;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 수련하는 곳 — 조합 · 교회 · 학자 저택.
/// </summary>
/// <remarks>
/// 자리가 아니라 <b>건물 표의 가르침 비트</b>가 정한다. 비트가 선 건물이면 어디든
/// "수련" 줄이 붙는다(<see cref="Engine.Town.TownWorks"/>).
///
/// <b>건물마다 사람도 말도 다르다.</b> 게임은 문구를 <c>0x00490D90</c> 의 갈래표로 고른다
/// (<c>0x00490DDC</c> 가 건물 종류로 셋 중 하나를 집는다).
/// <code>
///   0x0055A7E8  교회    "주의 배움의 터전에 잘 오셨습니다. 어떤 학문, 기능을 배우고 싶습니까?"
///   0x0055A830  조합    "기술을 습득하고 싶나?"
///   0x0055A848  그 밖   "가르쳐 드릴 것은 한가지 밖에 없습니다만."
/// </code>
/// 얼굴은 눈으로 찾아 박아 두지 않고 화자표에서 읽는다 — 조합장은 유럽에서 44 지만
/// 문화권을 건너가면 319 · 351 · 368 로 갈린다.
/// </remarks>
/// <param name="view">이 건물을 낸 도시 창. 대사 창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 화자 얼굴이 여기서 온다.</param>
/// <param name="buildingCode">건물 코드(교회 3 · 조합 9 · 학자 저택 15).</param>
/// <param name="culture">이 마을 문화권. 가르치는 사람 얼굴이 여기 따라 갈린다.</param>
/// <param name="buildings">건물 표. 가르침 비트를 기술 이름으로 푼다.</param>
internal sealed class TrainingMenu(Window view, Engine.Game game, int buildingCode, int culture,
                                   CityBuildingTable buildings)
{
    /// <summary>교회의 건물 코드. 말투가 이것만 다르다.</summary>
    private const int Church = 3;
    private const int Scholar = 15;

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly int _buildingCode = buildingCode;
    private readonly int _culture = culture;
    private readonly CityBuildingTable _buildings = buildings;

    /// <summary>가르치는 사람의 얼굴. 화자표가 건물과 문화권으로 정한다.</summary>
    private uint[]? Face => _game.SpeakerFace(_buildingCode, _culture);

    /// <summary>
    /// 들어설 때 가르치는 사람이 건네는 물음.
    /// </summary>
    /// <remarks>
    /// <b>학자 저택은 말이 없다.</b> 문 앞에서 명성을 보고 들여보내고 그것으로 끝이다 —
    /// 조합과 교회만 한마디 건넨다.
    /// </remarks>
    public void Greet()
    {
        if (_buildingCode == Scholar) return;

        ConfirmDialog.Tell(_view, Church == _buildingCode
            ? "주의 배움의 터전에 잘 오셨습니다. 어떤 학문, 기능을 배우고 싶습니까?"
            : "기술을 습득하고 싶나?", face: Face);
    }

    /// <summary>
    /// "수련" — 배울 것을 늘어놓고, 아무것도 안 배웠으면 한마디 한다.
    /// </summary>
    /// <remarks>
    /// 교회에서 그냥 "종료" 를 누르면 배웅하는 말이 나온다 — <b>"죄송하지만, 여기서는
    /// 수련이 불가능합니다." 가 아니다.</b> 그 말은 아예 가르칠 것이 없을 때의 것이다.
    /// </remarks>
    public void Teach(uint teachMask)
    {
        if (SkillLearnDialog.Show(_view, _game.Player, _buildings.Teaches(teachMask))) return;

        ConfirmDialog.Tell(_view, Church == _buildingCode
            ? "용건이 있을 경우에는 언제든지 와 주십시오."
            : "용건이 없다면 오지 말게!", face: Face);
    }
}
