namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// "확인" 버튼만 있는 이벤트 알림 다이얼로그 (예: 이슬람 함대 조우 알림, 이벤트 결과 알림)
/// </summary>
public class ConfirmOnlyEvent : IGameEvent
{
    public string Name => "이벤트 확인";
    public string Icon => "📢";

    public bool Matches(string ocrText) => ocrText.Contains("확인");

    public async Task HandleAsync(IntPtr hWnd, CancellationToken token = default)
    {
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(500, token);
    }
}

/// <summary>
/// 전투/도망 선택 다이얼로그 → 자동으로 "도망간다" 선택.
/// 2옵션: "싸운다 / 도망간다"
/// 3옵션: "교섭한다 / 도망간다 / 응전한다"
/// 두 경우 모두 "도망간다"는 두 번째 옵션이므로 ↓ 1회 + Enter.
/// </summary>
public class FleeChoiceEvent : IGameEvent
{
    public string Name => "도망간다 선택";
    public string Icon => "🏃";

    public bool Matches(string ocrText) =>
        ocrText.Contains("도망") &&
        (ocrText.Contains("싸운") || ocrText.Contains("응전") || ocrText.Contains("교섭"));

    public async Task HandleAsync(IntPtr hWnd, CancellationToken token = default)
    {
        GameWindowHelper.SendDownKey(hWnd);
        await Task.Delay(150, token);
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(500, token);
    }
}

/// <summary>
/// 보급 화면 → "최대" 버튼 → "결정" 버튼 자동 클릭.
/// 화면 하단의 버튼 좌표는 클라이언트 영역 크기에 대한 비율로 추정.
/// </summary>
public class SupplyMaxEvent : IGameEvent
{
    public string Name => "보급 최대";
    public string Icon => "📦";

    public bool Matches(string ocrText) =>
        ocrText.Contains("용량") && ocrText.Contains("결정") && ocrText.Contains("최대");

    public async Task HandleAsync(IntPtr hWnd, CancellationToken token = default)
    {
        var (w, h) = GameWindowHelper.GetClientSize(hWnd);
        if (w <= 0 || h <= 0) return;

        // 하단 버튼 바: 최대 / 10일분 / 감지점 / 전회분 / 결정 / 돌아간다
        // 스크린샷 기준(600x410 client): 버튼 y ≈ 305 (0.74), 최대 x ≈ 115 (0.19), 결정 x ≈ 420 (0.70)
        int btnY = (int)(h * 0.74);
        int maxX = (int)(w * 0.19);
        int confirmX = (int)(w * 0.70);

        GameWindowHelper.SendClickRelative(hWnd, maxX, btnY);
        await Task.Delay(300, token);
        GameWindowHelper.SendClickRelative(hWnd, confirmX, btnY);
        await Task.Delay(500, token);
    }
}
