using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>CDSX 에셋처럼 고정 릴리즈에서 BGM 곡을 받아 앱 캐시에 저장한다.</summary>
public static class BgmAssetDownloader
{
    private const string ReleaseBase =
        "https://github.com/Kyeongrok/cds-helper/releases/download/bgm-assets/";

    public static string CachePath(int track) =>
        Path.Combine(AppContext.BaseDirectory, "bgm", $"Track{track:D2}.mp3");

    public static async Task<(bool Success, string Error)> DownloadAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            string cacheDirectory = Path.GetDirectoryName(CachePath(BgmPlayer.RequiredTrack))!;
            Directory.CreateDirectory(cacheDirectory);
            string tempDirectory = Path.Combine(cacheDirectory, ".part");
            Directory.CreateDirectory(tempDirectory);

            const int firstTrack = 2;
            const int lastTrack = 29;
            const int trackCount = lastTrack - firstTrack + 1;
            int completed = 0;
            progress?.Report(0);

            for (int track = firstTrack; track <= lastTrack; track++)
            {
                string target = CachePath(track);
                if (File.Exists(target))
                {
                    completed++;
                    progress?.Report(completed * 100 / trackCount);
                    continue;
                }

                string temp = Path.Combine(tempDirectory, $"Track{track:D2}.mp3");
                using var response = await client.GetAsync(
                    $"{ReleaseBase}Track{track:D2}.mp3",
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = File.Create(temp))
                    await input.CopyToAsync(output, cancellationToken);
                File.Move(temp, target, overwrite: true);
                completed++;
                progress?.Report(completed * 100 / trackCount);
            }

            return File.Exists(CachePath(BgmPlayer.RequiredTrack))
                ? (true, "")
                : (false, $"Track{BgmPlayer.RequiredTrack:D2}.mp3를 다운로드하지 못했습니다.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (false, "다운로드가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally { }
    }
}
