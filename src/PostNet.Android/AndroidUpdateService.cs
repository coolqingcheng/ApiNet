using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Android.Content;
using Android.Provider;
using AndroidX.Core.Content;
using PostNet.Core.Interfaces;

namespace PostNet.Android;

public sealed class AndroidUpdateService(MainActivity activity) : IUpdateService
{
    private readonly SemaphoreSlim _gate = new(1);
    private string? _apk;
    private bool _installPending;
    public event Action<UpdateState>? StateChanged;
    public bool IsReady => _apk is not null;
    public async Task CheckAndDownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            StateChanged?.Invoke(new("正在检查更新…", Busy: true));
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Post.NET-Android", "1.0"));
            using var release = JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/coolqingcheng/Post.NET/releases/latest", cancellationToken));
            var versionText = release.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v');
            var current = typeof(Desktop.App).Assembly.GetName().Version!;
            if (!Version.TryParse(versionText, out var latest) || latest <= new Version(current.Major, current.Minor, current.Build)) { StateChanged?.Invoke(new("当前已是最新版本")); return; }
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "android-arm64" : "android-x64";
            var name = "Post.NET-" + rid + ".apk";
            var assets = release.RootElement.GetProperty("assets").EnumerateArray().ToList();
            string AssetUrl(string file) => assets.Single(x => x.GetProperty("name").GetString() == file).GetProperty("browser_download_url").GetString()!;
            var checksumLines = await client.GetStringAsync(AssetUrl("SHA256SUMS.txt"), cancellationToken);
            var expected = checksumLines.Split('\n').Select(x => x.Trim().Split("  ", 2, StringSplitOptions.None)).Single(x => x.Length == 2 && x[1] == name)[0];
            var directory = Path.Combine(activity.CacheDir!.AbsolutePath, "updates"); Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, name); var partial = target + ".partial";
            try
            {
                StateChanged?.Invoke(new("正在下载 APK…", Version: versionText, Busy: true));
                using var response = await client.GetAsync(AssetUrl(name), HttpCompletionOption.ResponseHeadersRead, cancellationToken); response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength;
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = File.Create(partial))
                {
                    var buffer = new byte[65536]; long downloaded = 0; var lastPercent = -1;
                    while (true)
                    {
                        var count = await input.ReadAsync(buffer, cancellationToken); if (count == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken); downloaded += count;
                        var percent = length > 0 ? (int)(downloaded * 100 / length) : 0;
                        if (percent != lastPercent) { lastPercent = percent; StateChanged?.Invoke(new("正在下载 APK…", percent, versionText, Busy: true)); }
                    }
                }
                await using (var file = File.OpenRead(partial))
                    if (!Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("APK 校验失败，请重新下载。");
                File.Move(partial, target, true); _apk = target;
                StateChanged?.Invoke(new("APK 已下载并校验，可以安装。", 100, versionText, Ready: true));
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        catch (Exception exception) { StateChanged?.Invoke(new("更新失败：" + exception.Message)); }
        finally { _gate.Release(); }
    }
    public void Apply(bool restart)
    {
        if (_apk is null) return;
        if (!activity.PackageManager!.CanRequestPackageInstalls())
        {
            _installPending = true;
            activity.StartActivity(new Intent(Settings.ActionManageUnknownAppSources, global::Android.Net.Uri.Parse("package:" + activity.PackageName)));
            return;
        }
        _installPending = false;
        var uri = FileProvider.GetUriForFile(activity, activity.PackageName + ".files", new Java.IO.File(_apk));
        var intent = new Intent(Intent.ActionView).SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission); activity.StartActivity(intent);
    }
    public void ResumeInstall() { if (_installPending && activity.PackageManager!.CanRequestPackageInstalls()) Apply(true); }
}
