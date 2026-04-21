using Velopack;
using Velopack.Sources;

namespace CdsHelper.Form.Local.Services;

public class UpdateService
{
    private readonly UpdateManager _updateManager;
    private Velopack.UpdateInfo? _pendingUpdate;

    public UpdateService()
    {
        _updateManager = new UpdateManager(
            new GithubSource("https://github.com/Kyeongrok/cds-helper", null, false));
    }

    public bool IsInstalled => _updateManager.IsInstalled;

    public async Task<string?> CheckForUpdateAsync()
    {
        if (!IsInstalled) return null;
        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            return _pendingUpdate?.TargetFullRelease.Version.ToString();
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadUpdateAsync(Action<int>? onProgress = null)
    {
        if (_pendingUpdate == null) return;
        await _updateManager.DownloadUpdatesAsync(_pendingUpdate, onProgress);
    }

    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate == null) return;
        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}
