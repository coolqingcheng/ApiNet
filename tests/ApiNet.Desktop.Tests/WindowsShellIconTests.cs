using System.Security.Cryptography;
using ApiNet.Platform;

namespace ApiNet.Desktop.Tests;

public sealed class WindowsShellIconTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "apinet-shell-" + Guid.NewGuid().ToString("N"));
    private string ApplicationDirectory => Path.Combine(_root, "安装目录", "current");
    private string DataDirectory => Path.Combine(_root, "用户数据");
    private string ShortcutDirectory => Path.Combine(_root, "TaskBar");

    public WindowsShellIconTests()
    {
        Directory.CreateDirectory(ApplicationDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ShortcutDirectory);
        File.WriteAllBytes(Path.Combine(ApplicationDirectory, "ApiNet.Desktop.exe"), []);
        File.WriteAllBytes(Path.Combine(ApplicationDirectory, "..", "ApiNet.exe"), []);
    }

    [WindowsFact]
    public void RepairsRenamedPinsAndLauncherLinksAndPreservesArgumentsAndUnrelatedLinks()
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Path.Combine(ApplicationDirectory, "ApiNet.Desktop.exe");
        var launcher = Path.GetFullPath(Path.Combine(ApplicationDirectory, "..", "ApiNet.exe"));
        var pin = CreateShortcut("我固定的网络工具.lnk", executable, "--example \"中文 value\"");
        var launcherLink = CreateShortcut("启动器.lnk", launcher);
        var unrelated = CreateShortcut("ApiNet.lnk", Path.Combine(_root, "Other.exe"));
        var unrelatedBytes = File.ReadAllBytes(unrelated);

        Refresh();

        using var repaired = WindowsShellLink.Open(pin);
        Assert.Equal(executable, repaired.TargetPath);
        Assert.Equal("--example \"中文 value\"", repaired.Arguments);
        Assert.Equal(0, repaired.Icon.Index);
        Assert.StartsWith(Path.Combine(DataDirectory, "shell-icons"), repaired.Icon.Path);
        using var iconStream = typeof(WindowsShellIconRefresh).Assembly.GetManifestResourceStream("ApiNet.ShellIcon.ico")!;
        using var expected = new MemoryStream();
        iconStream.CopyTo(expected);
        Assert.Equal(expected.ToArray(), File.ReadAllBytes(repaired.Icon.Path));
        Assert.Contains(Convert.ToHexString(SHA256.HashData(expected.ToArray())).ToLowerInvariant(), repaired.Icon.Path);
        using var repairedLauncher = WindowsShellLink.Open(launcherLink);
        Assert.Equal(launcher, repairedLauncher.TargetPath);
        Assert.Equal(repaired.Icon, repairedLauncher.Icon);
        Assert.Equal(unrelatedBytes, File.ReadAllBytes(unrelated));
    }

    [WindowsFact]
    public void RechecksNewPinsWithinSameVersionAndDoesNotRewriteCorrectShortcuts()
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(DataDirectory, "shell-icon-version"), typeof(WindowsShellIconRefresh).Assembly.GetName().Version!.ToString());
        var first = CreateShortcut("首次固定.lnk", Path.Combine(ApplicationDirectory, "ApiNet.Desktop.exe"));
        Refresh();
        var unchangedTime = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(first, unchangedTime);
        var second = CreateShortcut("后续固定.lnk", Path.Combine(ApplicationDirectory, "ApiNet.Desktop.exe"));

        Refresh();

        Assert.Equal(unchangedTime, File.GetLastWriteTimeUtc(first));
        using var firstLink = WindowsShellLink.Open(first);
        using var secondLink = WindowsShellLink.Open(second);
        Assert.Equal(firstLink.Icon, secondLink.Icon);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(DataDirectory, "shell-icons")));
    }

    [WindowsFact]
    public void LockedOrInvalidShortcutDoesNotBlockOtherRepairsAndIsRetried()
    {
        if (!OperatingSystem.IsWindows()) return;
        var locked = CreateShortcut("locked.lnk", Path.Combine(ApplicationDirectory, "ApiNet.Desktop.exe"));
        File.WriteAllBytes(Path.Combine(ShortcutDirectory, "broken.lnk"), [0, 1, 2]);
        var valid = CreateShortcut("valid.lnk", Path.Combine(ApplicationDirectory, "ApiNet.Desktop.exe"));
        using (var lockedFile = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Refresh();
        using (var validLink = WindowsShellLink.Open(valid)) Assert.True(File.Exists(validLink.Icon.Path));
        Refresh();
        using var lockedLink = WindowsShellLink.Open(locked);
        Assert.True(File.Exists(lockedLink.Icon.Path));
        Assert.StartsWith(DataDirectory, lockedLink.Icon.Path);
    }

    private void Refresh() => WindowsShellIconRefresh.Refresh(DataDirectory, ApplicationDirectory, [ShortcutDirectory]);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private string CreateShortcut(string name, string target, string arguments = "")
    {
        var path = Path.Combine(ShortcutDirectory, name);
        using var shortcut = new WindowsShellLink();
        shortcut.SetTarget(target);
        shortcut.SetArguments(arguments);
        shortcut.SetIcon(Path.Combine(_root, "old-icon.ico"), 2);
        shortcut.Save(path);
        return path;
    }

    public void Dispose() => Directory.Delete(_root, true);
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows Shell integration requires Windows.";
    }
}
