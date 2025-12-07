using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using cds_helper.Models;

namespace cds_helper.Services;

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
}
