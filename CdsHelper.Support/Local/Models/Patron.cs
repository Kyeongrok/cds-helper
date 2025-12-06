using System.Text.Json.Serialization;

namespace CdsHelper.Support.Local.Models;

public class PatronPreferences
{
    [JsonPropertyName("geography")]
    public bool Geography { get; set; }

    [JsonPropertyName("treasure")]
    public bool Treasure { get; set; }

    [JsonPropertyName("tradeGoods")]
    public bool TradeGoods { get; set; }

    [JsonPropertyName("creature")]
    public bool Creature { get; set; }

    [JsonPropertyName("history")]
    public bool History { get; set; }

    [JsonPropertyName("religion")]
    public bool Religion { get; set; }

    [JsonPropertyName("superstition")]
    public bool Superstition { get; set; }

    [JsonPropertyName("ethnicity")]
    public bool Ethnicity { get; set; }
}

public class Patron
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nationality")]
    public string Nationality { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("supportRate")]
    public string SupportRate { get; set; } = string.Empty;

    [JsonPropertyName("discernment")]
    public int Discernment { get; set; }

    [JsonPropertyName("occupation")]
    public string Occupation { get; set; } = string.Empty;

    [JsonPropertyName("appearYear")]
    public int? AppearYear { get; set; }

    [JsonPropertyName("retireYear")]
    public int? RetireYear { get; set; }

    [JsonPropertyName("preferences")]
    public PatronPreferences Preferences { get; set; } = new();

    [JsonPropertyName("fame")]
    public int Fame { get; set; }

    [JsonPropertyName("wealth")]
    public int Wealth { get; set; }

    [JsonPropertyName("power")]
    public string Power { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    public bool IsActive(int currentYear)
    {
        if (AppearYear.HasValue && currentYear < AppearYear.Value)
            return false;

        if (RetireYear.HasValue && currentYear >= RetireYear.Value)
            return false;

        return true;
    }

    public string StatusDisplay(int currentYear)
    {
        if (AppearYear.HasValue && currentYear < AppearYear.Value)
            return "미등장";

        if (RetireYear.HasValue && currentYear >= RetireYear.Value)
            return "은퇴";

        return "활동중";
    }
}
