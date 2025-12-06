using System.IO;
using CdsHelper.Api.Controllers;
using CdsHelper.Api.Entities;
using CdsHelper.Api.Migrations;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class CityService
{
    private CityController? _controller;
    private List<City> _cachedCities = new();
    private bool _initialized;

    /// <summary>
    /// Controller 초기화 및 데이터 마이그레이션
    /// </summary>
    public async Task InitializeAsync(string dbPath, string? jsonPath = null)
    {
        if (_initialized) return;

        _controller = CityController.Create(dbPath);

        // JSON 파일이 있으면 마이그레이션 시도
        if (!string.IsNullOrEmpty(jsonPath) && System.IO.File.Exists(jsonPath))
        {
            await DataMigrator.MigrateCitiesFromJsonAsync(_controller, jsonPath);
        }

        // 캐시 로드
        await RefreshCacheAsync();
        _initialized = true;
    }

    /// <summary>
    /// 캐시 새로고침
    /// </summary>
    private async Task RefreshCacheAsync()
    {
        if (_controller == null) return;

        var entities = await _controller.GetAllCitiesAsync();
        _cachedCities = entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 모든 도시 로드 (동기 - 기존 호환성)
    /// </summary>
    public List<City> LoadCities(string filePath)
    {
        // 기존 방식 유지 (JSON에서 직접 로드)
        if (!System.IO.File.Exists(filePath))
        {
            throw new FileNotFoundException($"cities.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = System.IO.File.ReadAllText(filePath);
        var cities = System.Text.Json.JsonSerializer.Deserialize<List<City>>(json);

        return cities ?? new List<City>();
    }

    /// <summary>
    /// DB에서 모든 도시 로드 (비동기)
    /// </summary>
    public async Task<List<City>> LoadCitiesFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다. InitializeAsync를 먼저 호출하세요.");

        var entities = await _controller.GetAllCitiesAsync();
        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 캐시된 도시 목록 반환
    /// </summary>
    public List<City> GetCachedCities()
    {
        return _cachedCities;
    }

    /// <summary>
    /// 필터링된 도시 목록 반환 (DB 쿼리)
    /// </summary>
    public async Task<List<City>> FilterFromDbAsync(
        string? nameSearch = null,
        string? culturalSphere = null,
        bool? libraryOnly = null,
        bool? shipyardOnly = null)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var entities = await _controller.GetCitiesByFilterAsync(
            nameSearch,
            culturalSphere,
            libraryOnly == true ? true : null,
            shipyardOnly == true ? true : null);

        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 필터링 (기존 호환성 - 메모리 필터)
    /// </summary>
    public List<City> Filter(
        IEnumerable<City> cities,
        string? nameSearch = null,
        string? culturalSphere = null,
        bool libraryOnly = false,
        bool shipyardOnly = false)
    {
        var filtered = cities.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(c => c.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(culturalSphere))
        {
            filtered = filtered.Where(c => c.CulturalSphere != null &&
                c.CulturalSphere.Equals(culturalSphere, StringComparison.OrdinalIgnoreCase));
        }

        if (libraryOnly)
        {
            filtered = filtered.Where(c => c.HasLibrary);
        }

        if (shipyardOnly)
        {
            filtered = filtered.Where(c => c.HasShipyard);
        }

        return filtered.ToList();
    }

    /// <summary>
    /// 문화권 목록 (DB)
    /// </summary>
    public async Task<List<string>> GetDistinctCulturalSpheresFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        return await _controller.GetCulturalSpheresAsync();
    }

    /// <summary>
    /// 문화권 목록 (기존 호환성 - 메모리)
    /// </summary>
    public List<string> GetDistinctCulturalSpheres(IEnumerable<City> cities)
    {
        return cities
            .Select(c => c.CulturalSphere)
            .Where(cs => !string.IsNullOrWhiteSpace(cs))
            .Distinct()
            .OrderBy(cs => cs)
            .ToList()!;
    }

    /// <summary>
    /// 도시명 조회 (DB)
    /// </summary>
    public async Task<string> GetCityNameFromDbAsync(byte index)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        return await _controller.GetCityNameAsync(index);
    }

    /// <summary>
    /// 도시명 조회 (기존 호환성 - 메모리)
    /// </summary>
    public string GetCityName(byte index, IEnumerable<City> cities)
    {
        if (index == 255)
            return "함대소속";

        var city = cities.FirstOrDefault(c => c.Id == index);
        return city?.Name ?? $"미확인({index})";
    }

    /// <summary>
    /// 좌표가 있는 도시 목록 (DB)
    /// </summary>
    public async Task<List<City>> GetCitiesWithCoordinatesFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var entities = await _controller.GetCitiesWithCoordinatesAsync();
        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 픽셀 좌표 업데이트 (DB)
    /// </summary>
    public async Task<bool> UpdatePixelCoordinatesAsync(byte cityId, int? pixelX, int? pixelY)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var result = await _controller.UpdateCityPixelCoordinatesAsync(cityId, pixelX, pixelY);

        // 캐시 갱신
        if (result)
        {
            await RefreshCacheAsync();
        }

        return result;
    }

    /// <summary>
    /// Entity -> Model 변환
    /// </summary>
    private static City ToModel(CityEntity entity)
    {
        return new City
        {
            Id = entity.Id,
            Name = entity.Name,
            Latitude = entity.Latitude,
            Longitude = entity.Longitude,
            HasLibrary = entity.HasLibrary,
            HasShipyard = entity.HasShipyard,
            CulturalSphere = entity.CulturalSphere,
            PixelX = entity.PixelX,
            PixelY = entity.PixelY
        };
    }
}
