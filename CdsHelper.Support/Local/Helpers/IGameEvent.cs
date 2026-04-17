namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 화면에 뜬 이벤트/다이얼로그 하나를 나타낸다.
/// OCR 텍스트로 식별(<see cref="Matches"/>)하고, 자동 처리(<see cref="HandleAsync"/>)한다.
/// </summary>
public interface IGameEvent
{
    /// <summary>상태 표시에 쓰는 사람용 이름 (예: "도망간다 선택")</summary>
    string Name { get; }

    /// <summary>상태 표시에 쓰는 이모지 프리픽스 (예: "🏃")</summary>
    string Icon { get; }

    /// <summary>OCR 결과 텍스트가 이 이벤트에 해당하는지 판정한다.</summary>
    bool Matches(string ocrText);

    /// <summary>이벤트를 자동으로 처리한다 (버튼/키 입력 등).</summary>
    Task HandleAsync(IntPtr hWnd, CancellationToken token = default);
}
