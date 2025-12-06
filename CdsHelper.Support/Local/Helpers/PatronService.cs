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

    public List<PatronDisplay> ToDisplayList(IEnumerable<Patron> patrons, int currentYear)
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
            Wealth = p.Wealth,
            Power = p.Power,
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
