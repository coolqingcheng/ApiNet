using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ApiNet.Platform;

internal static partial class WindowsShellIconRefresh
{
    public static void Refresh(string dataDirectory, string applicationDirectory, IEnumerable<string>? shortcutDirectories = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var resource = typeof(WindowsShellIconRefresh).Assembly.GetManifestResourceStream("ApiNet.ShellIcon.ico")
                ?? throw new IOException("Missing Windows shell icon.");
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            var icon = bytes.ToArray();
            // Explorer keys its icon cache by path. Give changed artwork a new path,
            // and keep old files available for shortcuts belonging to an older install.
            var iconDirectory = Path.GetFullPath(Path.Combine(dataDirectory, "shell-icons"));
            var iconPath = Path.Combine(iconDirectory, $"apinet-{Convert.ToHexString(SHA256.HashData(icon)).ToLowerInvariant()}.ico");
            Directory.CreateDirectory(iconDirectory);
            if (!File.Exists(iconPath) || !File.ReadAllBytes(iconPath).AsSpan().SequenceEqual(icon))
                File.WriteAllBytes(iconPath, icon);

            var executable = Path.GetFullPath(Path.Combine(applicationDirectory, "ApiNet.Desktop.exe"));
            var launcher = Path.GetFullPath(Path.Combine(applicationDirectory, "..", "ApiNet.exe"));
            foreach (var directory in (shortcutDirectories ?? GetShortcutDirectories()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(directory)) continue;
                    foreach (var path in Directory.EnumerateFiles(directory, "*.lnk"))
                    {
                        try
                        {
                            using var shortcut = WindowsShellLink.Open(path);
                            var target = shortcut.TargetPath;
                            if (!target.Equals(executable, StringComparison.OrdinalIgnoreCase)
                                && !(File.Exists(launcher) && target.Equals(launcher, StringComparison.OrdinalIgnoreCase))) continue;
                            var (oldIcon, index) = shortcut.Icon;
                            if (oldIcon.Equals(iconPath, StringComparison.OrdinalIgnoreCase) && index == 0) continue;
                            // Saving the existing link preserves its target, arguments,
                            // AppUserModelID, user-assigned name and pin position.
                            shortcut.SetIcon(iconPath);
                            shortcut.Save(path);
                            Notify(path);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
                        {
                            Trace.TraceWarning($"Cannot refresh shortcut '{path}': {exception.Message}");
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Cannot inspect shortcuts in '{directory}': {exception.Message}");
                }
            }
            Notify(iconPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            // A shell integration failure must not prevent opening the workspace or installation.
            Trace.TraceWarning($"Cannot refresh Windows shell icon: {exception.Message}");
        }
    }

    private static IEnumerable<string> GetShortcutDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
    }

    private static void Notify(string path) => SHChangeNotify(0x00002000, 0x0005 | 0x1000, path, 0);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHChangeNotify(uint eventId, uint flags, string item, nint unused);
}
