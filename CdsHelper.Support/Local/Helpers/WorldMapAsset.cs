using System.IO;
using System.Net.Http;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 폴더에 WORLD.CDS가 없을 때 쓸 세계지도 원본.
/// </summary>
public static class WorldMapAsset
{
    public const string FileName = "world.cdsx";
    public const string DownloadUrl =
        "https://github.com/Kyeongrok/cds-helper/releases/download/map-assets/world.cdsx";

    public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

    /// <summary>WORLD.CDS 대체 파일을 내려받고, 성공하면 저장된 경로를 돌려준다.</summary>
    public static string? EnsureDownloaded()
    {
        if (File.Exists(FilePath)) return FilePath;

        string tempPath = FilePath + ".part";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var data = client.GetByteArrayAsync(DownloadUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(tempPath, data);
            File.Move(tempPath, FilePath, overwrite: true);
            return FilePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return null;
        }
    }
}
