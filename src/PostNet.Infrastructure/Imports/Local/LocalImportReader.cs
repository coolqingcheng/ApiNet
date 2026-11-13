using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using PostNet.Core.Interfaces;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PostNet.Infrastructure.Imports.Local;

public sealed class LocalImportReader
{
    private readonly string _appData;
    private readonly string _localData;
    private readonly string _home;
    private readonly bool _systemInstallations;
    public LocalImportReader(string? appData = null, string? localData = null, string? home = null)
    {
        _systemInstallations = appData is null && localData is null && home is null;
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _appData = appData ?? (OperatingSystem.IsMacOS() ? Path.Combine(_home, "Library", "Application Support") : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _localData = localData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
    public Task<IReadOnlyList<LocalImportSource>> DetectAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<LocalImportSource>>(() =>
    {
        var sources = new List<LocalImportSource>();
        foreach (var format in new[] { "ApiPost", "Postman" })
        {
            var executable = Executables(format).FirstOrDefault(File.Exists);
            // Enumerate the actual directory names: on a case-insensitive macOS
            // volume Postman/postman refer to the same profile.
            var profiles = Directory.Exists(_appData) ? Directory.EnumerateDirectories(_appData, "*", new EnumerationOptions { IgnoreInaccessible = true }).Where(x => Path.GetFileName(x).Equals(format, StringComparison.OrdinalIgnoreCase)) : [];
            var paths = profiles.Concat(new[] { Path.Combine(_home, "snap", format.ToLowerInvariant(), "current", ".config", format), Path.Combine(_home, ".var", "app", "com.getpostman.Postman", "config", format) }).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var path in paths.Where(Directory.Exists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backup = Backups(path).FirstOrDefault();
                if (executable is not null || backup is not null || Databases(path).Any())
                    sources.Add(new LocalImportSource(format + ":" + path, format, format, path, executable, backup));
            }
            if (executable is not null && sources.All(x => x.Format != format))
                sources.Add(new LocalImportSource(format, format, format, Path.Combine(_appData, format == "ApiPost" ? "apipost" : format), executable));
        }
        return sources;
    }, cancellationToken);

    public async Task<string> ReadAsync(LocalImportSource source, CancellationToken cancellationToken = default)
    {
        // Every invocation gets a new directory: stale WAL files from another
        // database must never enter this snapshot.
        var temporary = Path.Combine(Path.GetTempPath(), "ApiNet", "imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var records = new JsonArray();
            var databases = Databases(source.DataDirectory).ToList();
            var warnings = new JsonArray();
            var failures = 0;
            foreach (var database in databases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var snapshot = Path.Combine(temporary, Guid.NewGuid().ToString("N"));
                    await CopySnapshotAsync(database, snapshot, cancellationToken);
                    var values = await Task.Run(() => LevelDbSnapshot.ReadIndexedDbValues(snapshot), cancellationToken);
                    if (values.Count == 0) continue;
                    var decoded = await DecodeAsync(source, values, temporary, cancellationToken);
                    foreach (var record in decoded) records.Add(record?.DeepClone());
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or JsonException)
                { failures++; }
            }
            // A partial database cannot be used for incremental updates: missing
            // records could otherwise be misreported as deletions.
            if (failures == 0 && records.Count > 0)
                return new JsonObject { ["apinetLocalFormat"] = source.Format, ["records"] = records, ["apinetWarnings"] = warnings }.ToJsonString();
            var backup = Backups(source.DataDirectory).FirstOrDefault() ?? source.BackupPath;
            if (backup is not null && File.Exists(backup))
            {
                var root = JsonNode.Parse(await File.ReadAllTextAsync(backup, cancellationToken)) as JsonObject ?? throw new FormatException("本地备份不是有效的 JSON 对象。");
                root["apinetWarnings"] = new JsonArray("正在读取自动备份：" + Path.GetFileName(backup) + "。备份可能早于原软件当前数据，请确认导入预览。");
                return root.ToJsonString();
            }
            throw new InvalidOperationException("无法完整读取此版本的本地数据。请先关闭原软件后重试；若仍失败，请在原软件导出 Postman / ApiPost JSON 后导入。加密数据和仅云端保存的数据无法直接读取。");
        }
        finally
        {
            try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public static async Task CopySnapshotAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destination);
        long total = 0;
        var current = await File.ReadAllTextAsync(Path.Combine(source, "CURRENT"), cancellationToken);
        foreach (var path in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(path);
            if (name != "CURRENT" && !name.StartsWith("MANIFEST-", StringComparison.Ordinal) && Path.GetExtension(name) is not (".log" or ".ldb" or ".sst")) continue;
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
            total += input.Length;
            if (total > 512L * 1024 * 1024) throw new IOException("本地数据库大于 512 MB，请使用 JSON 导出导入。");
            await using var output = new FileStream(Path.Combine(destination, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken);
        }
        if (current != await File.ReadAllTextAsync(Path.Combine(source, "CURRENT"), cancellationToken)) throw new IOException("来源数据库正在切换文件，请关闭原软件后重试。");
    }
    private async Task<JsonArray> DecodeAsync(LocalImportSource source, IReadOnlyList<byte[]> values, string temporary, CancellationToken cancellationToken)
    {
        var input = Path.Combine(temporary, "values.json");
        var output = Path.Combine(temporary, "decoded.json");
        var script = Path.Combine(temporary, "decode.cjs");
        await using (var stream = File.Create(input))
        {
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartArray(); foreach (var value in values) writer.WriteBase64StringValue(value); writer.WriteEndArray();
        }
        await using (var resource = typeof(LocalImportReader).Assembly.GetManifestResourceStream("PostNet.Infrastructure.Imports.Local.DecodeIndexedDb.cjs")!)
        await using (var file = File.Create(script)) await resource.CopyToAsync(file, cancellationToken);
        var runtimes = new[] { source.ExecutablePath }.Concat(Executables(source.Format)).Concat(Executables(source.Format == "ApiPost" ? "Postman" : "ApiPost")).Concat(NodeExecutables()).OfType<string>().Where(File.Exists).Distinct();
        foreach (var runtime in runtimes)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var info = new ProcessStartInfo(runtime) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            info.Environment["ELECTRON_RUN_AS_NODE"] = "1";
            info.Environment.Remove("NODE_OPTIONS");
            info.ArgumentList.Add(script); info.ArgumentList.Add(input); info.ArgumentList.Add(output); info.ArgumentList.Add(source.Format);
            Process? process = null;
            try
            {
                File.Delete(output);
                process = Process.Start(info) ?? throw new IOException("无法启动数据解码器。");
                var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
                var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(stdout, stderr);
                if (process.ExitCode != 0 || !File.Exists(output)) continue;
                if (new FileInfo(output).Length > 256L * 1024 * 1024) throw new FormatException("本地数据过大，请分项目导出 JSON。");
                var root = JsonNode.Parse(await File.ReadAllTextAsync(output, cancellationToken))!.AsObject();
                if (root["failed"]?.GetValue<int>() != 0) continue;
                return root["records"]!.AsArray();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally
            {
                if (process is not null) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } process.Dispose(); }
            }
        }
        throw new InvalidOperationException("未找到兼容的本地 V8 数据解码器，请通过原软件导出 JSON。");
    }
    private static IEnumerable<string> Databases(string root)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root, "*.indexeddb.leveldb", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.Ordinal);
    }
    private static IEnumerable<string> Backups(string root) => !Directory.Exists(root) ? [] : Directory.EnumerateFiles(root, "backup-*.json", new EnumerationOptions { IgnoreInaccessible = true }).OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
    private IEnumerable<string> Executables(string format)
    {
        var name = format == "ApiPost" ? "Apipost" : "Postman";
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(_localData, "Programs", name, name + ".exe");
            yield return Path.Combine(_localData, name, name + ".exe");
            if (_systemInstallations)
            {
                foreach (var path in RegisteredExecutables(name)) yield return path;
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name, name + ".exe");
            }
            var root = Path.Combine(_localData, name);
            if (Directory.Exists(root)) foreach (var dir in Directory.EnumerateDirectories(root, "app-*", new EnumerationOptions { IgnoreInaccessible = true }).OrderDescending(StringComparer.Ordinal)) yield return Path.Combine(dir, name + ".exe");
        }
        else if (OperatingSystem.IsMacOS())
        {
            foreach (var root in _systemInstallations ? new[] { "/Applications", Path.Combine(_home, "Applications") } : new[] { Path.Combine(_home, "Applications") })
                foreach (var appName in new[] { name, format, format.ToLowerInvariant() }.Distinct()) yield return Path.Combine(root, appName + ".app", "Contents", "MacOS", appName);
        }
        else
        {
            foreach (var root in _systemInstallations ? new[] { "/opt", "/usr/share", Path.Combine(_home, ".local", "share"), "/snap/" + format.ToLowerInvariant() + "/current/usr/share" } : new[] { Path.Combine(_home, ".local", "share") })
                foreach (var appName in new[] { name, format, format.ToLowerInvariant() }.Distinct())
                    foreach (var binary in new[] { name, format, format.ToLowerInvariant() }.Distinct()) yield return Path.Combine(root, appName, binary);
            if (_systemInstallations) foreach (var path in PathExecutables(format.ToLowerInvariant())) yield return path;
        }
    }
    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RegisteredExecutables(string name)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var registry = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var id in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(id);
                var displayName = entry?.GetValue("DisplayName") as string;
                if (displayName is null || !displayName.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry?.GetValue("DisplayIcon") is string icon)
                {
                    icon = Environment.ExpandEnvironmentVariables(icon.Trim());
                    if (icon.StartsWith('"')) { var end = icon.IndexOf('"', 1); if (end > 1) yield return icon[1..end]; }
                    else yield return icon.Split(',')[0].Trim();
                }
                if (entry?.GetValue("InstallLocation") is string location && location.Length > 0) yield return Path.Combine(location, name + ".exe");
            }
        }
    }
    private static IEnumerable<string> NodeExecutables() => PathExecutables(OperatingSystem.IsWindows() ? "node.exe" : "node");
    private static IEnumerable<string> PathExecutables(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(x => Path.Combine(x.Trim('"'), name));
}
