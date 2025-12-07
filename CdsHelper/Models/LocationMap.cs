using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace cds_helper.Models;

/// <summary>
/// 대항해시대 3 도시 코드 매핑
/// </summary>
public static class LocationMap
{
    private static Dictionary<byte, string>? _cities;

    public static Dictionary<byte, string> Cities
    {
        get
        {
            if (_cities == null)
            {
                LoadCities();
            }
            return _cities!;
        }
    }

    private static void LoadCities()
    {
        try
        {
            // 실행 파일 위치에서 cities.json 찾기
            var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executableDir = Path.GetDirectoryName(executablePath);
            var citiesFilePath = Path.Combine(executableDir!, "cities.json");

            if (!File.Exists(citiesFilePath))
            {
                // 프로젝트 루트에서 찾기
                var projectRoot = Path.Combine(executableDir!, "..", "..", "..", "..");
                citiesFilePath = Path.Combine(projectRoot, "cds-helper", "cities.json");
            }

            if (File.Exists(citiesFilePath))
            {
                var json = File.ReadAllText(citiesFilePath);
                var cityList = JsonSerializer.Deserialize<List<City>>(json);
                _cities = cityList?.ToDictionary(c => c.Id, c => c.Name) ?? new Dictionary<byte, string>();
            }
            else
            {
                _cities = new Dictionary<byte, string>();
            }
        }
        catch
        {
            _cities = new Dictionary<byte, string>();
        }
    }

    public static string GetCityName(byte index)
    {
        if (index == 255)
            return "함대소속";

        return Cities.TryGetValue(index, out var city) ? city : $"미확인({index})";
    }
}
