using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class PatronService
{
    public List<Patron> LoadPatrons(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"patrons.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var patrons = JsonSerializer.Deserialize<List<Patron>>(json);

        return patrons ?? new List<Patron>();
    }

    /// <summary>
    /// 그 도시에 지금 있는 후원자들. 아직 안 나타났거나 이미 은퇴한 사람은 뺀다.
    /// </summary>
    /// <remarks>
    /// 왕궁에서 설득할 상대를 찾을 때 쓴다. 게임은 건물마다 후원자 하나를 물려 두지만
    /// (스폰서 객체 <c>+0xB4</c>) 그 짝이 어디서 오는지는 아직 못 풀었다 — 그래서 도시로 찾고,
    /// 여럿이면 고르게 한다.
    /// </remarks>
    public List<Patron> ActiveInCity(IEnumerable<Patron> patrons, string city, int year) =>
        patrons
            .Where(p => p.City.Equals(city, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.IsActive(year))
            .ToList();

    public List<Patron> Filter(
        IEnumerable<Patron> patrons,
        string? nameSearch = null,
        string? citySearch = null,
        string? nationality = null,
        bool activeOnly = false,
        int currentYear = 1480)
    {
        var filtered = patrons.AsEnumerable();

        // 후원자명 검색
        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(p => p.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 도시 검색
        if (!string.IsNullOrWhiteSpace(citySearch))
        {
            filtered = filtered.Where(p => p.City.Contains(citySearch, StringComparison.OrdinalIgnoreCase));
        }

        // 국적 필터
        if (!string.IsNullOrWhiteSpace(nationality))
        {
            filtered = filtered.Where(p => p.Nationality.Equals(nationality, StringComparison.OrdinalIgnoreCase));
        }

        // 활동중인 후원자만
        if (activeOnly)
        {
            filtered = filtered.Where(p => p.IsActive(currentYear));
        }

        return filtered.ToList();
    }

    public List<PatronDisplay> ToDisplayList(IEnumerable<Patron> patrons, int currentYear, int playerFame = 0)
    {
        return patrons.Select(p => new PatronDisplay
        {
            Id = p.Id,
            Name = p.Name,
            Nationality = p.Nationality,
            City = p.City,
            Occupation = p.Occupation,
            SupportRate = p.SupportRate,
            Discernment = p.Discernment,
            AppearYear = p.AppearYear,
            RetireYear = p.RetireYear,
            StatusDisplay = p.StatusDisplay(currentYear),
            Fame = p.Fame,
            IsFameMet = playerFame > 0 && p.Fame <= playerFame,
            Wealth = p.Wealth,
            Power = p.Power,
            Preferences = p.PreferencesDisplay(),
            Note = p.Note
        }).ToList();
    }

    public List<string> GetDistinctNationalities(IEnumerable<Patron> patrons)
    {
        return patrons
            .Select(p => p.Nationality)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }
}
