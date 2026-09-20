using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>릴리즈에서 BGM 묶음을 받아 게임 폴더에 설치한다.</summary>
public static class BgmAssetDownloader
{
    private const string DownloadUrl =
        "https://github.com/Kyeongrok/cds-helper/releases/latest/download/bgm.zip";

    public static async Task<(bool Success, string Error)> DownloadAsync(
        string gameDirectory, CancellationToken cancellationToken = default)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CdsHelper", $"bgm-{Guid.NewGuid():N}");
        string archivePath = Path.Combine(tempRoot, "bgm.zip");
        string extractDirectory = Path.Combine(tempRoot, "extract");

        try
        {
            Directory.CreateDirectory(tempRoot);

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await client.GetAsync(DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(archivePath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            ZipFile.ExtractToDirectory(archivePath, extractDirectory);
            string sourceDirectory = Directory.Exists(Path.Combine(extractDirectory, "bgm"))
                ? Path.Combine(extractDirectory, "bgm")
                : extractDirectory;
            var tracks = Directory.EnumerateFiles(sourceDirectory, "Track*.mp3")
                .Where(path => string.Equals(Path.GetFileName(path), $"Track{BgmPlayer.RequiredTrack:D2}.mp3",
                    StringComparison.OrdinalIgnoreCase)
                    || int.TryParse(Path.GetFileNameWithoutExtension(path)["Track".Length..], out _))
                .ToArray();

            if (tracks.Length == 0)
                return (false, "다운로드한 BGM 묶음에 MP3 파일이 없습니다.");

            string targetDirectory = Path.Combine(gameDirectory, "bgm");
            Directory.CreateDirectory(targetDirectory);
            foreach (var track in tracks)
                File.Copy(track, Path.Combine(targetDirectory, Path.GetFileName(track)), overwrite: true);

            return BgmPlayer.IsAvailable(gameDirectory)
                ? (true, "")
                : (false, $"Track{BgmPlayer.RequiredTrack:D2}.mp3가 다운로드 묶음에 없습니다.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (false, "다운로드가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // 임시 파일 정리는 실패해도 게임 실행 자체는 막지 않는다.
            }
        }
    }
}
