namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// OCR 텍스트로부터 현재 화면에 떠있는 이벤트가 어떤 것인지 식별한다.
/// 새로운 이벤트를 추가하려면 <see cref="IGameEvent"/>를 구현하고
/// <see cref="_events"/> 리스트에 등록한다.
/// </summary>
public class GameEventRecognizer
{
    private readonly IReadOnlyList<IGameEvent> _events;

    public GameEventRecognizer()
    {
        // 구체적인 매칭을 먼저 등록 (ConfirmOnly의 "확인"이 가장 포괄적이므로 마지막)
        _events = new IGameEvent[]
        {
            new SupplyMaxEvent(),
            new FleeChoiceEvent(),
            new ConfirmOnlyEvent(),
        };
    }

    /// <summary>
    /// OCR 결과 텍스트로 매칭되는 이벤트를 찾는다. 없으면 null.
    /// </summary>
    public IGameEvent? Recognize(string? ocrText)
    {
        if (string.IsNullOrEmpty(ocrText)) return null;
        foreach (var ev in _events)
            if (ev.Matches(ocrText)) return ev;
        return null;
    }
}
