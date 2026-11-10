using System.Runtime.InteropServices;

namespace ApiNet.Desktop.Services;

internal static partial class WindowsShellIconRefresh
{
    public static void RefreshAfterUpdate(string dataDirectory)
    {
        if (!OperatingSystem.IsWindows()) return;
        var version = typeof(WindowsShellIconRefresh).Assembly.GetName().Version?.ToString() ?? "";
        var marker = Path.Combine(dataDirectory, "shell-icon-version");
        try
        {
            if (File.Exists(marker) && File.ReadAllText(marker) == version) return;
            // Notify only this application's files; preserve the user's global icon cache.
            foreach (var path in new[] { Environment.ProcessPath, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ApiNet.exe")) })
                if (path is not null && File.Exists(path)) SHChangeNotify(0x00002000, 0x0005 | 0x2000, path, 0);
            File.WriteAllText(marker, version);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHChangeNotify(uint eventId, uint flags, string item, nint unused);
}
