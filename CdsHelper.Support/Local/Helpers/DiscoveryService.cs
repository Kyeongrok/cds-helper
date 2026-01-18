using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CdsHelper.Api.Data;
using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Support.Local.Helpers;

public class DiscoveryService
{
    private AppDbContext? _dbContext;
    private Dictionary<int, DiscoveryEntity> _discoveries = new();
    private Dictionary<string, int> _nameToIdMap = new(); // 발견물 이름 -> ID 매핑
    private bool _initialized;

    public async Task InitializeAsync(string dbPath, string? csvPath = null)
    {
        if (_initialized) return;

        _dbContext = AppDbContextFactory.Create(dbPath);
        _dbContext.Database.EnsureCreated();
        EnsureTablesExist();

        // CSV 파일이 있으면 마이그레이션
        if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
        {
            await MigrateFromCsvAsync(csvPath);
        }

        // 캐시 로드
        await RefreshCacheAsync();
        _initialized = true;
    }

    private void EnsureTablesExist()
    {
        _dbContext?.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Discoveries (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                HintId INTEGER,
                AppearCondition TEXT,
                BookName TEXT,
                FOREIGN KEY (HintId) REFERENCES Hints(Id) ON DELETE SET NULL
            )");

        _dbContext?.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS DiscoveryParents (
                DiscoveryId INTEGER NOT NULL,
                ParentDiscoveryId INTEGER NOT NULL,
                PRIMARY KEY (DiscoveryId, ParentDiscoveryId),
                FOREIGN KEY (DiscoveryId) REFERENCES Discoveries(Id) ON DELETE CASCADE,
                FOREIGN KEY (ParentDiscoveryId) REFERENCES Discoveries(Id) ON DELETE RESTRICT
            )");
    }

    private async Task MigrateFromCsvAsync(string csvPath)
    {
        if (_dbContext == null) return;

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // 기존 데이터 확인
        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM Discoveries";
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            if (count > 0) return; // 이미 데이터가 있으면 스킵
        }

        // 힌트 이름 -> ID 매핑 로드
        var hintNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hints = await _dbContext.Hints.AsNoTracking().ToListAsync();
        foreach (var hint in hints)
        {
            if (!hintNameToId.ContainsKey(hint.Name))
                hintNameToId[hint.Name] = hint.Id;
        }

        // CSV 파싱
        var lines = await File.ReadAllLinesAsync(csvPath, Encoding.UTF8);
        var discoveries = new List<(int Id, string Name, string? HintName, string? Condition, string? AppearCondition, string? BookName)>();

        for (int i = 1; i < lines.Length; i++) // 헤더 스킵
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseCsvLine(line);
            if (fields.Count < 2) continue;

            if (!int.TryParse(fields[0].Trim(), out var id)) continue;

            var name = fields.Count > 1 ? fields[1].Trim() : "";
            var hintName = fields.Count > 2 ? NullIfEmpty(fields[2].Trim()) : null;
            var condition = fields.Count > 3 ? NullIfEmpty(fields[3].Trim()) : null;
            var appearCondition = fields.Count > 4 ? NullIfEmpty(fields[4].Trim()) : null;
            var bookName = fields.Count > 5 ? NullIfEmpty(fields[5].Trim()) : null;

            if (string.IsNullOrEmpty(name)) continue;

            discoveries.Add((id, name, hintName, condition, appearCondition, bookName));
        }

        // 이름 -> ID 매핑 생성 (선행 발견물 매핑용)
        var nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in discoveries)
        {
            if (!nameToId.ContainsKey(d.Name))
                nameToId[d.Name] = d.Id;
        }

        // 발견물 INSERT
        foreach (var d in discoveries)
        {
            // 힌트 이름으로 힌트 ID 찾기
            int? hintId = null;
            if (!string.IsNullOrEmpty(d.HintName))
            {
                hintId = FindHintId(d.HintName, hintNameToId);
            }

            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT OR REPLACE INTO Discoveries (Id, Name, HintId, AppearCondition, BookName)
                VALUES (@id, @name, @hintId, @appearCondition, @bookName)";

            AddParameter(insertCmd, "@id", d.Id);
            AddParameter(insertCmd, "@name", d.Name);
            AddParameter(insertCmd, "@hintId", hintId);
            AddParameter(insertCmd, "@appearCondition", d.AppearCondition);
            AddParameter(insertCmd, "@bookName", d.BookName);

            await insertCmd.ExecuteNonQueryAsync();
        }

        // 선행 발견물 매핑 INSERT
        foreach (var d in discoveries)
        {
            if (string.IsNullOrEmpty(d.Condition)) continue;

            var parentNames = ParseCondition(d.Condition);
            foreach (var parentName in parentNames)
            {
                // 발견물 이름으로 ID 찾기
                var parentId = FindDiscoveryId(parentName, nameToId);
                if (parentId == null) continue;

                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT OR IGNORE INTO DiscoveryParents (DiscoveryId, ParentDiscoveryId)
                    VALUES (@discoveryId, @parentId)";

                AddParameter(insertCmd, "@discoveryId", d.Id);
                AddParameter(insertCmd, "@parentId", parentId.Value);

                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        EventQueueService.Instance.DataLoaded("DiscoveryService", $"발견물 {discoveries.Count}개 마이그레이션 완료");
    }

    /// <summary>
    /// 힌트 이름으로 힌트 ID 찾기 (유사 매칭 지원)
    /// </summary>
    private int? FindHintId(string hintName, Dictionary<string, int> hintNameToId)
    {
        // 정확한 매칭
        if (hintNameToId.TryGetValue(hintName, out var id))
            return id;

        // 공백 제거 후 매칭
        var normalized = hintName.Replace(" ", "");
        foreach (var kvp in hintNameToId)
        {
            if (kvp.Key.Replace(" ", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        // 부분 매칭
        foreach (var kvp in hintNameToId)
        {
            if (kvp.Key.Contains(hintName, StringComparison.OrdinalIgnoreCase) ||
                hintName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    /// <summary>
    /// 게재조건 파싱 (쉼표로 구분된 선행 발견물 목록)
    /// </summary>
    private List<string> ParseCondition(string condition)
    {
        var result = new List<string>();

        // "희망봉,말라카해협,마젤란해협 발견" 같은 형식 파싱
        // " 발견" 제거
        var cleaned = Regex.Replace(condition, @"\s*발견\s*$", "", RegexOptions.IgnoreCase);

        // 쉼표로 분리
        var parts = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            // "발견" 제거 (각 항목에서도)
            trimmed = Regex.Replace(trimmed, @"\s*발견\s*$", "", RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    /// <summary>
    /// 발견물 이름으로 ID 찾기 (유사 매칭 지원)
    /// </summary>
    private int? FindDiscoveryId(string name, Dictionary<string, int> nameToId)
    {
        // 정확한 매칭
        if (nameToId.TryGetValue(name, out var id))
            return id;

        // 공백 제거 후 매칭
        var normalized = name.Replace(" ", "");
        foreach (var kvp in nameToId)
        {
            if (kvp.Key.Replace(" ", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        // 부분 매칭 (name이 키에 포함되거나, 키가 name에 포함)
        foreach (var kvp in nameToId)
        {
            if (kvp.Key.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    private List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void AddParameter(System.Data.Common.DbCommand cmd, string name, object? value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }

    private async Task RefreshCacheAsync()
    {
        if (_dbContext == null) return;

        var discoveries = await _dbContext.Discoveries
            .Include(d => d.Hint)
            .AsNoTracking()
            .ToListAsync();
        _discoveries = discoveries.ToDictionary(d => d.Id);
        _nameToIdMap = discoveries.ToDictionary(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    public DiscoveryEntity? GetDiscovery(int id)
    {
        return _discoveries.TryGetValue(id, out var discovery) ? discovery : null;
    }

    public int? GetDiscoveryIdByName(string name)
    {
        return _nameToIdMap.TryGetValue(name, out var id) ? id : null;
    }

    public Dictionary<int, DiscoveryEntity> GetAllDiscoveries()
    {
        return new Dictionary<int, DiscoveryEntity>(_discoveries);
    }

    /// <summary>
    /// 특정 발견물의 선행 발견물 ID 목록 조회
    /// </summary>
    public async Task<List<int>> GetParentDiscoveryIdsAsync(int discoveryId)
    {
        if (_dbContext == null) return new List<int>();

        return await _dbContext.DiscoveryParents
            .Where(dp => dp.DiscoveryId == discoveryId)
            .Select(dp => dp.ParentDiscoveryId)
            .ToListAsync();
    }

    /// <summary>
    /// 모든 발견물의 선행 발견물 매핑 조회
    /// </summary>
    public async Task<Dictionary<int, List<int>>> GetAllParentMappingsAsync()
    {
        if (_dbContext == null) return new Dictionary<int, List<int>>();

        var mappings = await _dbContext.DiscoveryParents
            .AsNoTracking()
            .ToListAsync();

        return mappings
            .GroupBy(dp => dp.DiscoveryId)
            .ToDictionary(g => g.Key, g => g.Select(dp => dp.ParentDiscoveryId).ToList());
    }
}
