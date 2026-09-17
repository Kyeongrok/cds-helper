using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Discovery;

/// <summary>
/// 초심자(EASY) 캐릭터의 개인 퀘스트라인(이야기0·이야기1)을 맡는다 — 지금 자리·상태에서
/// 열리는 장면을 찾아 주고, 튼 장면의 진행을 적는다.
/// </summary>
/// <remarks>
/// 이야기0(STORY0.CDS)·이야기1(STORY1.CDS)은 발견 이벤트(DISEV.CDS)와 그릇·명령이 완전히
/// 같은 열일곱 파트짜리 책이다(<see cref="DisevBook.Books"/>). 다만 파트 번호가 발견물
/// 번호가 아니라 <b>마당 안의 장면 번호</b>이고, 발견물 표처럼 "이 자리에서 이 번호"를
/// 미리 짝지어 둔 표가 없다 — 그 대신 각 파트 머리말의 <c>Step</c>과 슬롯 조건(건물·도시·
/// 연도·명성·계약 상태)이 스스로 "언제 트는지"를 말한다.
///
/// <b>Step 은 원본이 안에서 어떻게 썼는지 그대로 복원하지 않는다.</b> 여기서는 그 값을
/// 그저 "이 파트는 그 장(章)의 진행도가 이 값일 때만 후보"라는 조회 열쇠로만 쓰고, 진행도
/// 자체는 <see cref="Player.StoryProgress"/> 에 우리가 직접 적어 올린다(<see cref="Advance"/>).
/// 장의 경계는 <c>Step == 0</c> 인 파트를 찾아 그때그때 센다 — 하드코딩하지 않는다.
/// </remarks>
public static class StoryLog
{
    /// <summary>그 장(章)의 진행 열쇠 — <see cref="Player.StoryProgress"/>·<see cref="Player.ClosedStoryArcs"/> 가 쓴다.</summary>
    private static string ArcKey(string cache, int arcStart) => $"{cache}:{arcStart}";

    /// <summary>
    /// 지금 자리(건물)에서 열리는 이야기 파트. 없으면 null.
    /// </summary>
    /// <param name="player">주인공 — <see cref="Player.ActiveStoryBook"/> 이 없으면 곧장 null.</param>
    /// <param name="game">이 판.</param>
    /// <param name="building">지금 들어와 있는 건물 코드. 건물 밖(도시만 들어왔을 때)이면 -1.</param>
    public static int? NextPart(Player player, Game game, int building)
    {
        if (player.ActiveStoryBook is not { } cache) return null;
        if (DisevRunner.Open(game.Directory, cache) is not { } book) return null;

        int arcStart = -1;
        for (int i = 0; i < book.Count; i++)
        {
            if (DisevPart.Parse(book.Part(i), out _) is not { } part) continue;
            if (part.Step == 0) arcStart = i;
            if (arcStart < 0) continue;   // 첫 장 시작(Step 0) 전의 부스러기는 없을 테지만 혹시나

            string key = ArcKey(cache, arcStart);
            if (player.IsStoryArcClosed(key)) continue;
            if (part.Step != player.StoryStepOf(key)) continue;
            if (DisevRunner.IsEligible(game, cache, i, building)) return i;
        }
        return null;
    }

    /// <summary>
    /// 튼 파트의 뒤처리 — 대본이 겪은 것(<see cref="DisevRunner.LastAdvancedStep"/>·
    /// <see cref="DisevRunner.LastStoryArcCompleted"/>)을 보고 그 장의 진행도를 적는다.
    /// </summary>
    /// <remarks>
    /// <see cref="DisevRunner.Run(System.Windows.Window, Game, string, int, int)"/> 을 부른
    /// 바로 뒤에, 같은 <paramref name="cache"/>·<paramref name="partIndex"/> 로 불러야 한다 —
    /// 두 정적 플래그가 그 사이에 다른 대본이 끼면 덮어써진다.
    /// </remarks>
    public static void Advance(Player player, Game game, string cache, int partIndex)
    {
        if (!DisevRunner.LastAdvancedStep && !DisevRunner.LastStoryArcCompleted) return;
        if (DisevRunner.Open(game.Directory, cache) is not { } book) return;
        if (DisevPart.Parse(book.Part(partIndex), out _) is not { } part) return;

        int arcStart = partIndex;
        for (int i = 0; i <= partIndex; i++)
            if (DisevPart.Parse(book.Part(i), out _) is { Step: 0 }) arcStart = i;

        string key = ArcKey(cache, arcStart);
        if (DisevRunner.LastStoryArcCompleted) player.CloseStoryArc(key);
        else player.SetStoryStep(key, part.Step + 1);
    }
}
