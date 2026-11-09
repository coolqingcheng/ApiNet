using ApiNet.Core;
using ApiNet.Core.Models;
using ApiNet.Infrastructure.Persistence;
using ApiNet.Setup;
using Microsoft.Data.Sqlite;

namespace ApiNet.Tests;

public class InstallerTests
{
    [Fact]
    public void InstallerDefaultsToRepairAndForwardsApplicationArgumentsSafely()
    {
        var root = Path.Combine(Path.GetTempPath(), "ApiNet 测试");
        var log = Path.Combine(root, "setup log.txt");
        var options = InstallerOptions.Parse(["--no-start", "--installto", root, "--log=" + log, "--", "--installto", "app-argument"], "unused", "unused.log");
        Assert.Equal(Path.GetFullPath(root), options.InstallDirectory);
        Assert.Equal(Path.GetFullPath(log), options.LogPath);
        Assert.False(options.StartApplication);
        Assert.Equal(["--", "--installto", "app-argument"], options.ForwardedArguments);
        Assert.True(InstallerOptions.Parse([], root, log).StartApplication);
        Assert.False(InstallerOptions.Parse(["--silent"], root, log).StartApplication);
        Assert.Equal(Path.GetFullPath(root), InstallerOptions.Parse(["--installto=" + root], "unused", log).InstallDirectory);
        Assert.Throws<ArgumentException>(() => InstallerOptions.Parse(["--installto"], root, log));
    }

    [Fact]
    public async Task MigrationKeepsWorkspaceHistoryWalAndCertificatesAndCanBeRetried()
    {
        var root = Path.Combine(Path.GetTempPath(), "apinet-migrate-" + Guid.NewGuid().ToString("N"));
        var oldStore = new SqliteWorkspaceStore(Path.Combine(root, "original"));
        var request = new RequestDefinition { Name = "保留的请求" };
        await oldStore.SaveAsync(new WorkspaceDocument { Requests = [request], SharedVariables = [new() { Key = "host", Value = "https://example.com" }] });
        await oldStore.AddHistoryAsync(new HistoryEntry { Request = request, Response = new HttpResult { StatusCode = 200, Body = "saved" } });
        // Snapshot a stable WAL database into a legacy installation fixture.
        var legacy = Path.Combine(root, "legacy-install");
        Directory.CreateDirectory(legacy);
        foreach (var file in Directory.GetFiles(oldStore.DataDirectory, "workspace.db*"))
            if (!file.EndsWith("-shm")) File.Copy(file, Path.Combine(legacy, Path.GetFileName(file)));
        Directory.CreateDirectory(Path.Combine(legacy, "certificates"));
        byte[] certificate = [1, 2, 255, 0, 128];
        File.WriteAllBytes(Path.Combine(legacy, "certificates", "apinet-root.pfx"), certificate);
        var target = Path.Combine(root, "data");
        Assert.True(WorkspaceDataPaths.MigrateLegacyData(legacy, target));
        var migrated = new SqliteWorkspaceStore(target);
        Assert.Equal("保留的请求", (await migrated.LoadAsync())!.Requests.Single().Name);
        Assert.Equal("https://example.com", (await migrated.LoadAsync())!.SharedVariables.Single().Value);
        Assert.Equal("saved", (await migrated.GetHistoryDetailAsync((await migrated.GetHistoryAsync()).Single().Id))!.Response.Body);
        Assert.Equal(certificate, File.ReadAllBytes(Path.Combine(target, "certificates", "apinet-root.pfx")));
        Assert.True(File.Exists(Path.Combine(legacy, "workspace.db")));
        Assert.False(WorkspaceDataPaths.MigrateLegacyData(legacy, target));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void MigrationNeverOverwritesExistingDataAndSkipsAnEmptyLegacyDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "apinet-migrate-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "old");
        var target = Path.Combine(root, "data");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Assert.False(WorkspaceDataPaths.MigrateLegacyData(source, target));
        File.WriteAllText(Path.Combine(source, "workspace.db"), "old");
        File.WriteAllText(Path.Combine(target, "workspace.db"), "keep-new");
        Assert.False(WorkspaceDataPaths.MigrateLegacyData(source, target));
        Assert.Equal("keep-new", File.ReadAllText(Path.Combine(target, "workspace.db")));
    }
}
