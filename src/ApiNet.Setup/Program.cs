using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ApiNet.Core;

namespace ApiNet.Setup;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 1;
        string? temporaryDirectory = null;
        var silent = args.TakeWhile(x => x != "--").Any(x => x is "--silent" or "-s");
        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApiNetSetupLogs", "setup.log");
        try
        {
            var options = InstallerOptions.Parse(args, WorkspaceDataPaths.LegacyDirectory, log);
            log = options.LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            // Serialize repairs for this user, including calls targeting a custom directory.
            using var gate = new Mutex(false, "Local\\ApiNet.Setup");
            try { if (!gate.WaitOne(TimeSpan.FromMinutes(2))) throw new IOException("另一个安装任务仍在运行，请稍后重试。"); }
            catch (AbandonedMutexException) { }
            try
            {
                temporaryDirectory = Path.Combine(Path.GetTempPath(), "ApiNet-Setup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);
                var installer = Path.Combine(temporaryDirectory, "VelopackSetup.exe");
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApiNet.Setup.Payload.exe") ?? throw new IOException("安装包内容缺失，请重新下载安装包。"))
                using (var output = new FileStream(installer, FileMode.CreateNew, FileAccess.Write, FileShare.None)) resource.CopyTo(output);

                if (!options.Help)
                {
                    StopInstalledApplication(options.InstallDirectory);
                    var data = Environment.GetEnvironmentVariable("APINET_DATA_DIR") ?? WorkspaceDataPaths.DefaultDirectory;
                    if (IsWithin(data, options.InstallDirectory)) throw new IOException("数据目录位于安装目录中，请将 APINET_DATA_DIR 设置到独立目录后再安装。");
                    WorkspaceDataPaths.MigrateLegacyData(options.InstallDirectory, data);
                }
                var start = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = temporaryDirectory };
                // Native Velopack's silent mode accepts overwrite/repair and retains its
                // normal extraction, rollback, registration and update layout.
                start.ArgumentList.Add("--silent");
                start.ArgumentList.Add("--installto"); start.ArgumentList.Add(options.InstallDirectory);
                start.ArgumentList.Add("--log"); start.ArgumentList.Add(log);
                foreach (var argument in options.ForwardedArguments) start.ArgumentList.Add(argument);
                using var child = Process.Start(start) ?? throw new IOException("无法启动安装程序。");
                child.WaitForExit();
                if (child.ExitCode != 0) throw new IOException($"安装未完成（错误码 {child.ExitCode}）。");
                if (!options.Help)
                {
                    var launcher = Path.Combine(options.InstallDirectory, "ApiNet.exe");
                    if (File.Exists(launcher)) SHChangeNotify(0x00002000, 0x0005 | 0x2000, launcher, 0);
                }
                if (options.StartApplication && !options.Help)
                {
                    var launcher = Path.Combine(options.InstallDirectory, "ApiNet.exe");
                    if (!File.Exists(launcher)) throw new IOException("安装完成后未找到应用启动程序。");
                    var launch = new ProcessStartInfo(launcher) { UseShellExecute = true, WorkingDirectory = options.InstallDirectory };
                    var separator = options.ForwardedArguments.ToList().IndexOf("--");
                    if (separator >= 0) foreach (var argument in options.ForwardedArguments.Skip(separator + 1)) launch.ArgumentList.Add(argument);
                    Process.Start(launch)?.Dispose();
                }
                return 0;
            }
            finally { gate.ReleaseMutex(); }
        }
        catch (Exception exception)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.AppendAllText(log, $"\n{DateTimeOffset.Now:O} ApiNet Setup: {exception}\n"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (!silent) MessageBox(0, $"{exception.Message}\n\n安装日志：{log}", "ApiNet 安装未完成", 0x10);
            return 1;
        }
        finally
        {
            if (temporaryDirectory is not null)
                try { Directory.Delete(temporaryDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsWithin(string candidate, string root)
    {
        var path = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        var directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return path.Equals(directory, StringComparison.OrdinalIgnoreCase) || path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private static void StopInstalledApplication(string installDirectory)
    {
        foreach (var process in Process.GetProcessesByName("ApiNet.Desktop"))
            using (process)
            {
                string? executable;
                try { executable = process.MainModule?.FileName; }
                catch (InvalidOperationException) { continue; }
                catch (Win32Exception) { continue; }
                if (executable is null || !IsWithin(executable, installDirectory)) continue;
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000)) { process.Kill(true); process.WaitForExit(); }
                }
                catch (InvalidOperationException) { }
            }
    }
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint window, string text, string caption, uint type);
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHChangeNotify(uint eventId, uint flags, string item, nint unused);
}
