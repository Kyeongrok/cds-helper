using System.Collections.Generic;

namespace cds_helper.Models;

public class SaveGameInfo
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public List<CharacterData> Characters { get; set; } = new();

    public string DateString => $"{Year}년 {Month}월 {Day}일";
}
