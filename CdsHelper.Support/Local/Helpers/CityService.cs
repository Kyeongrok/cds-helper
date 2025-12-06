using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class CityService
{
    public List<City> LoadCities(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"cities.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var cities = JsonSerializer.Deserialize<List<City>>(json);

        return cities ?? new List<City>();
    }

    public List<City> Filter(
        IEnumerable<City> cities,
        string? nameSearch = null,
        string? culturalSphere = null,
        bool libraryOnly = false,
        bool shipyardOnly = false)
    {
        var filtered = cities.AsEnumerable();

        // 도시명 검색
        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(c => c.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 문화권 필터
        if (!string.IsNullOrWhiteSpace(culturalSphere))
        {
            filtered = filtered.Where(c => c.CulturalSphere != null &&
                c.CulturalSphere.Equals(culturalSphere, StringComparison.OrdinalIgnoreCase));
        }

        // 도서관 있는 도시만
        if (libraryOnly)
        {
            filtered = filtered.Where(c => c.HasLibrary);
        }

        // 조선소 있는 도시만
        if (shipyardOnly)
        {
            filtered = filtered.Where(c => c.HasShipyard);
        }

        return filtered.ToList();
    }

    public List<string> GetDistinctCulturalSpheres(IEnumerable<City> cities)
    {
        return cities
            .Select(c => c.CulturalSphere)
            .Where(cs => !string.IsNullOrWhiteSpace(cs))
            .Distinct()
            .OrderBy(cs => cs)
            .ToList()!;
    }

    public string GetCityName(byte index, IEnumerable<City> cities)
    {
        if (index == 255)
            return "함대소속";

        var city = cities.FirstOrDefault(c => c.Id == index);
        return city?.Name ?? $"미확인({index})";
    }
}
