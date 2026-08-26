using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 함대 그림 8방향. 게임이 안 켜져 있어도 배를 그릴 수 있게 <c>asset/ship</c> 에 넣어 둔 것이다.
/// </summary>
/// <remarks>
/// 원본은 실행 중인 CDS_95 의 배 아틀라스(VA 0x5D68C8)에서 클래스 0 의 8방향을 뜬 것이다.
/// EXE 파일에는 없다 — 그 자리가 .data 의 초기화되지 않은 뒷부분(rawsize 0x51C00 밖)이라
/// 실행 중에만 찬다. 그래서 한 번 떠서 PNG 로 남겨 두고 여기서 읽는다.
///
/// 파일은 <c>asset/ship/ship_0.png</c> ~ <c>ship_7.png</c>(배)와
/// <c>asset/horse/horse_0.png</c> ~ <c>horse_7.png</c>(말), 한 장 48x48 이고 비침은 알파 0 이다.
/// 말은 게임이 16방향을 넷으로 접어 쓰므로 두 장씩 같은 그림이다.
/// 색인이 아니라 색이 그대로 들어 있으므로 그림판으로 열어 고쳐도 그대로 나온다.
///
/// 번호는 게임 방향(반시계)을 둘로 접은 것이다 — 0 북, 2 서, 4 남, 6 동.
/// 게임이 떠 있으면 <see cref="GameShipReader"/> 가 읽은 것을 쓰고, 없을 때 이 표로 물러선다.
/// </remarks>
public static class ShipSprites
{
    public const int Width = 48;
    public const int Size = Width * Width;
    public const int Directions = 8;

    /// <summary>실행 파일 옆의 이 폴더들에서 찾는다.</summary>
    public const string ShipDirectory = "asset/ship";
    public const string HorseDirectory = "asset/horse";

    /// <summary>배 그림 벌 수(0~3). 게임의 함선 등급과 같은 차례다.</summary>
    public const int SkinCount = 4;

    private static int _skin = -1;
    private static string? _folder;

    /// <summary>
    /// 어느 벌의 배 그림을 쓸지(0~3). <c>asset/ship-g0</c> ~ <c>ship-g3</c> 에서 읽는다 —
    /// 게임의 <c>CDS95Util/shipskin</c> 에 있는 넉 벌을 풀어 둔 것이다.
    /// -1 이면 예전처럼 <see cref="ShipDirectory"/> 를 쓴다.
    /// </summary>
    /// <remarks>바꾸면 들고 있던 그림을 버린다 — 다음에 그릴 때 새 벌로 다시 읽는다.</remarks>
    public static int Skin
    {
        get => _skin;
        set
        {
            int next = value >= 0 && value < SkinCount ? value : -1;
            if (_skin == next) return;
            lock (Gate)
            {
                _skin = next;
                for (int i = 0; i < Directions; i++) Frames[0][i] = null;   // 배만 다시 읽는다
            }
        }
    }

    /// <summary>
    /// 등록해 넣은 배가 제 그림을 들고 있는 폴더의 온 경로. null 이면 <see cref="Skin"/> 대로 읽는다.
    /// </summary>
    /// <remarks>
    /// <see cref="Skin"/> 보다 이쪽이 세다. 앱에서 손으로 등록한 배는 <c>asset</c> 밖
    /// (<c>%APPDATA%\CdsHelper\ships\{Id}</c>)에 그림을 두기 때문이다.
    /// </remarks>
    public static string? Folder
    {
        get => _folder;
        set
        {
            string? next = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_folder == next) return;
            lock (Gate)
            {
                _folder = next;
                for (int i = 0; i < Directions; i++) Frames[0][i] = null;   // 배만 다시 읽는다
            }
        }
    }

    /// <summary>어느 배의 그림을 쓸지 한 번에 정한다 — 폴더와 벌 번호를 같이 맞춘다.</summary>
    public static void Use(Hull? hull)
    {
        Folder = hull?.SpriteFolder;
        Skin = hull?.Skin ?? 0;
    }

    /// <summary>[0] 배, [1] 말(육상·정박).</summary>
    private static readonly uint[]?[][] Frames = [new uint[Directions][], new uint[Directions][]];
    private static readonly object Gate = new();

    /// <summary>못 읽은 까닭 한 줄. 다 읽었으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 16방향 값으로 48x48 그림 한 장(BGRA, 비침은 알파 0). 파일이 없으면 빈 span.
    /// 한 번 읽은 것은 들고 있는다.
    /// </summary>
    public static ReadOnlySpan<uint> Frame(int heading16, bool onLand = false)
    {
        int set = onLand ? 1 : 0;
        int i = (heading16 & 0xF) >> 1;
        var cached = Frames[set][i];
        if (cached != null) return cached;

        lock (Gate)
        {
            Frames[set][i] ??= Load(set, i) ?? [];
            return Frames[set][i];
        }
    }

    private static uint[]? Load(int set, int index)
    {
        string path;
        if (set == 0 && _folder != null)
        {
            // 등록해 넣은 배 — 그림이 asset 밖에 있으므로 온 경로 그대로 쓴다.
            path = Path.Combine(_folder, $"ship_{index}.png");
        }
        else
        {
            var dir = set == 1 ? HorseDirectory : _skin >= 0 ? $"asset/ship-g{_skin}" : ShipDirectory;
            var name = set == 1 ? "horse" : "ship";
            path = Path.Combine(AppContext.BaseDirectory, dir, $"{name}_{index}.png");
        }

        // 못 찾으면 기본 벌로 물러선다 — 반쪽짜리라도 배는 그려야 한다.
        if (!File.Exists(path) && set == 0 && (_skin >= 0 || _folder != null))
            path = Path.Combine(AppContext.BaseDirectory, ShipDirectory, $"ship_{index}.png");
        if (!File.Exists(path))
        {
            LastError = $"{path} 없음";
            return null;
        }
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth != Width || src.PixelHeight != Width)
            {
                LastError = $"{path} 크기가 {src.PixelWidth}x{src.PixelHeight} — {Width}x{Width} 이어야 합니다";
                return null;
            }
            var px = new uint[Size];
            src.CopyPixels(px, Width * 4, 0);
            LastError = "";
            return px;
        }
        catch (Exception ex)
        {
            LastError = $"{path} 를 읽지 못했습니다 — {ex.Message}";
            return null;
        }
    }
}
