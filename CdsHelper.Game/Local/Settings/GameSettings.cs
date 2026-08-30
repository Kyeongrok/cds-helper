using System.IO;
using System.Text.Json;

namespace CdsHelper.Game.Local.Settings;

/// <summary>이대로 <c>game-settings.json</c> 이 된다.</summary>
public sealed class GameSettingsData
{
    /// <summary>앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 켬.</summary>
    public bool AutoOpenShipMap { get; set; } = true;

    /// <summary>함대 창에서 배경음악을 틀지. 기본은 켬.</summary>
    public bool BgmEnabled { get; set; } = true;

    /// <summary>효과음(닻·거절 따위)을 낼지. 기본은 켬.</summary>
    public bool SfxEnabled { get; set; } = true;

    /// <summary>배경음악·효과음의 크기(0~100). 기본은 다 크게.</summary>
    public int BgmVolume { get; set; } = GameSettings.MaxVolume;
    public int SfxVolume { get; set; } = GameSettings.MaxVolume;

    /// <summary>게임 창 단추의 좌우 여백(점).</summary>
    public int BandPad { get; set; } = GameSettings.DefaultBandPad;

    /// <summary>도시 창이 열릴 때 줄 효과. <see cref="Settings.CityOpenEffect"/> 의 이름이다.</summary>
    public string CityOpenEffect { get; set; } = "Expand";

    /// <summary>지도 위에 좌표 상자를 겹쳐 보일지.</summary>
    public bool ShowCoordOverlay { get; set; } = true;

    /// <summary>지도 위의 까만 조작 줄을 보일지.</summary>
    public bool ShowToolBar { get; set; } = true;

    /// <summary>지도 위에 만난 사람 상자를 겹쳐 보일지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowPeopleOverlay { get; set; }

    /// <summary>
    /// 게임 상단 띠에 켜 둔 칸 이름들("날짜"·"소지금" …). 한 번도 안 건드렸으면 null 이라
    /// 부르는 쪽 기본값이 선다.
    /// </summary>
    public List<string>? BarCells { get; set; }

    /// <summary>지도 위에 바람·해류 화살표를 얹을지.</summary>
    public bool ShowFlowArrows { get; set; }
}

/// <summary>
/// 이 앱이 품고 있는 놀이에만 쓰는 설정. <c>%APPDATA%\CdsHelper\game-settings.json</c> 에 적는다.
/// </summary>
/// <remarks>
/// 앱 설정(<c>CdsHelper.Support</c> 의 <c>AppSettings</c>)과 갈라 두었다. 그쪽은 지도·발견물처럼
/// 이 앱이 도구로서 하는 일이고, 여기는 놀이 쪽이라 섞일 까닭이 없다. 갈라 두면 놀이를 통째로
/// 들어내도 앱 설정은 그대로다.
///
/// <b>실제 CDS_95 를 자동으로 조작하는 값(<c>AutoConfirmDialog</c> 따위)은 여기 없다</b> —
/// 그건 놀이가 아니라 도구 쪽 일이라 <c>AppSettings</c> 에 남아 있다.
///
/// 예전에는 이 값들이 <c>settings.json</c> 에 같이 들어 있었다. 새 파일이 없으면 그 파일에서
/// 한 번 옮겨 온다(<see cref="MigrateFromLegacy"/>) — 쓰던 사람이 맞춰 둔 값을 잃지 않는다.
/// </remarks>
public static class GameSettings
{
    /// <summary>
    /// 게임 창 단추의 좌우 여백 기본값(점).
    /// </summary>
    /// <remarks>
    /// 띠 마구리는 실제로 16점이라, 16 이면 마구리가 통째로 글자 밖에 선다. 그만큼 다 비우면
    /// 조금 헐거워 보여 눈으로 맞춰 12 로 잡았다 — 마구리 무늬가 글자에 살짝 걸치는 자리다.
    ///
    /// 이 값은 <b>적어 둔 것이 없을 때만</b> 선다. 한 번이라도 개발 창에서 만졌으면
    /// <c>game-settings.json</c> 에 적힌 값이 이긴다.
    /// </remarks>
    public const int DefaultBandPad = 12;

    /// <summary>단추 여백을 이 사이로만 잡는다.</summary>
    public const int MinBandPad = 0, MaxBandPad = 32;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "game-settings.json");

    /// <summary>옛 자리 — 갈라 놓기 전에는 여기 같이 들어 있었다.</summary>
    private static readonly string LegacyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static GameSettingsData _data = new();
    private static bool _loaded;

    static GameSettings() => Load();

    /// <summary>
    /// 적어 둔 것을 읽는다. 두 번 불러도 한 번만 읽는다.
    /// </summary>
    /// <remarks>
    /// 앱을 켤 때 한 번 불러 둔다(<c>App.OnStartup</c>). 옛 <c>settings.json</c> 에서 옮겨 오는
    /// 일이 여기서 벌어지는데, 그 전에 앱 설정이 먼저 저장되면 옛 값이 지워진 뒤라 놓치게 된다.
    /// </remarks>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (File.Exists(FilePath))
            {
                _data = JsonSerializer.Deserialize<GameSettingsData>(File.ReadAllText(FilePath)) ?? new();
                return;
            }

            if (MigrateFromLegacy()) Save();
        }
        catch
        {
            // 읽다 넘어져도 놀이는 기본값으로 굴러가야 한다.
            _data = new GameSettingsData();
        }
    }

    /// <summary>옛 <c>settings.json</c> 에 있던 값을 한 번 옮겨 온다. 옮길 게 있었으면 참.</summary>
    private static bool MigrateFromLegacy()
    {
        if (!File.Exists(LegacyPath)) return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LegacyPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var moved = new GameSettingsData();
            bool any = false;

            any |= Bool("AutoOpenShipMap", v => moved.AutoOpenShipMap = v);
            any |= Bool("BgmEnabled", v => moved.BgmEnabled = v);
            any |= Bool("SfxEnabled", v => moved.SfxEnabled = v);
            any |= Bool("ShowCoordOverlay", v => moved.ShowCoordOverlay = v);
            any |= Bool("ShowPeopleOverlay", v => moved.ShowPeopleOverlay = v);
            any |= Bool("ShowToolBar", v => moved.ShowToolBar = v);
            any |= Bool("ShowFlowArrows", v => moved.ShowFlowArrows = v);

            if (root.TryGetProperty("BandPad", out var pad) && pad.TryGetInt32(out int padValue))
            {
                moved.BandPad = Math.Clamp(padValue, MinBandPad, MaxBandPad);
                any = true;
            }

            if (root.TryGetProperty("CityOpenEffect", out var effect) && effect.ValueKind == JsonValueKind.String)
            {
                moved.CityOpenEffect = effect.GetString() ?? "Expand";
                any = true;
            }

            if (root.TryGetProperty("BarCells", out var cells) && cells.ValueKind == JsonValueKind.Array)
            {
                moved.BarCells = [.. cells.EnumerateArray()
                    .Where(c => c.ValueKind == JsonValueKind.String)
                    .Select(c => c.GetString()!)];
                any = true;
            }

            if (any) _data = moved;
            return any;

            bool Bool(string name, Action<bool> set)
            {
                if (!root.TryGetProperty(name, out var value)) return false;
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;

                set(value.GetBoolean());
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, Json));
        }
        catch
        {
            // 못 적어도 이번 판은 굴러간다.
        }
    }

    private static T Get<T>(Func<GameSettingsData, T> read)
    {
        Load();
        return read(_data);
    }

    private static void Set(Action<GameSettingsData> write)
    {
        Load();
        write(_data);
        Save();
    }

    // ── 값들 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 <b>켬</b>이다 — 이 앱이
    /// 하는 일이 곧 그 창이라, 켤 때마다 메뉴에서 한 번 더 누르게 할 까닭이 없다.
    /// </summary>
    public static bool AutoOpenShipMap
    {
        get => Get(d => d.AutoOpenShipMap);
        set => Set(d => d.AutoOpenShipMap = value);
    }

    /// <summary>함대 창의 배경음악을 틀지. 설정 창에서 켜고 끈다.</summary>
    /// <summary>소리 크기의 위와 한 번에 오르내리는 폭.</summary>
    public const int MaxVolume = 100, VolumeStep = 10;

    /// <summary>배경음악 크기(0~100).</summary>
    public static int BgmVolume
    {
        get => Math.Clamp(Get(d => d.BgmVolume), 0, MaxVolume);
        set => Set(d => d.BgmVolume = Math.Clamp(value, 0, MaxVolume));
    }

    /// <summary>효과음 크기(0~100).</summary>
    public static int SfxVolume
    {
        get => Math.Clamp(Get(d => d.SfxVolume), 0, MaxVolume);
        set => Set(d => d.SfxVolume = Math.Clamp(value, 0, MaxVolume));
    }

    public static bool BgmEnabled
    {
        get => Get(d => d.BgmEnabled);
        set => Set(d => d.BgmEnabled = value);
    }

    /// <summary>효과음을 낼지. 배경음악과 따로 켜고 끈다.</summary>
    public static bool SfxEnabled
    {
        get => Get(d => d.SfxEnabled);
        set => Set(d => d.SfxEnabled = value);
    }

    /// <summary>
    /// 게임 창 단추의 좌우 여백(점).
    /// </summary>
    /// <remarks>
    /// 띠는 왼끝·가운데·오른끝 셋으로 짓고 양 끝(마구리)이 16점씩이다. 이 값만큼을 글자
    /// 바깥에 비워 두므로, 16 이면 마구리가 통째로 글자 밖에 서고 그보다 작으면 글자가
    /// 마구리 위로 조금씩 올라앉는다. 바꾼 값은 <b>다음에 여는 창</b>부터 든다.
    /// </remarks>
    public static int BandPad
    {
        get => Get(d => Math.Clamp(d.BandPad, MinBandPad, MaxBandPad));
        set => Set(d => d.BandPad = Math.Clamp(value, MinBandPad, MaxBandPad));
    }

    /// <summary>도시 창이 열릴 때 줄 효과. 개발 창에서 고른다.</summary>
    public static CityOpenEffect CityOpenEffect
    {
        get => Get(d => Enum.TryParse<CityOpenEffect>(d.CityOpenEffect, out var effect)
            ? effect
            : Settings.CityOpenEffect.Expand);
        set => Set(d => d.CityOpenEffect = value.ToString());
    }

    /// <summary>지도 위에 좌표 상자를 겹쳐 보일지. 개발 창에서 켜고 끈다.</summary>
    public static bool ShowCoordOverlay
    {
        get => Get(d => d.ShowCoordOverlay);
        set => Set(d => d.ShowCoordOverlay = value);
    }

    /// <summary>
    /// 지도 위에 <b>만난 사람</b> 상자를 겹쳐 보일지. 개발 창의 "정보" 가 켜고 끈다.
    /// </summary>
    public static bool ShowPeopleOverlay
    {
        get => Get(d => d.ShowPeopleOverlay);
        set => Set(d => d.ShowPeopleOverlay = value);
    }

    /// <summary>지도 위의 까만 조작 줄을 보일지. 개발 창에서 켜고 끈다.</summary>
    public static bool ShowToolBar
    {
        get => Get(d => d.ShowToolBar);
        set => Set(d => d.ShowToolBar = value);
    }

    /// <summary>게임 상단 띠에 켜 둔 칸 이름들. 도시정보 창에서 켜고 끈다.</summary>
    public static IReadOnlyList<string>? BarCells
    {
        get => Get(d => d.BarCells);
        set => Set(d => d.BarCells = value == null ? null : [.. value]);
    }

    /// <summary>지도 위에 바람·해류 화살표를 얹을지. 함대 창 커맨드에서 켜고 끈다.</summary>
    public static bool ShowFlowArrows
    {
        get => Get(d => d.ShowFlowArrows);
        set => Set(d => d.ShowFlowArrows = value);
    }
}
