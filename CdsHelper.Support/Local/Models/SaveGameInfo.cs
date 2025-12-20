namespace CdsHelper.Support.Local.Models;

public class SaveGameInfo
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public List<CharacterData> Characters { get; set; } = new();
    public List<HintData> Hints { get; set; } = new();

    public string DateString => $"{Year}년 {Month}월 {Day}일";

    public int AcquiredHintCount => Hints.Count(h => h.IsAcquired);
    public int TotalHintCount => Hints.Count;
}
