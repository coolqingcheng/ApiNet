namespace ApiNet.Core;

public static class WorkspaceDataPaths
{
    public static string LegacyDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApiNet");
    public static string DefaultDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApiNetData")
        : LegacyDirectory;

    // Keep the original files available for rollback. Publish the database last so a
    // failed copy can be retried without treating an incomplete migration as complete.
    public static bool MigrateLegacyData(string sourceDirectory, string destinationDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory);
        var destination = Path.GetFullPath(destinationDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var database = Path.Combine(destination, "workspace.db");
        var oldDatabase = Path.Combine(source, "workspace.db");
        if (source.Equals(destination, comparison) || !File.Exists(oldDatabase) || File.Exists(database)) return false;

        Directory.CreateDirectory(destination);
        using var migrationLock = new FileStream(Path.Combine(destination, ".migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(database)) return false;
        using var input = new FileStream(oldDatabase, FileMode.Open, FileAccess.Read, FileShare.None);
        var temporary = Path.Combine(destination, "workspace.db.migrating-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            var wal = oldDatabase + "-wal";
            if (File.Exists(wal)) File.Copy(wal, database + "-wal", true);
            var certificates = Path.Combine(source, "certificates");
            if (Directory.Exists(certificates))
                foreach (var file in Directory.EnumerateFiles(certificates, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(destination, "certificates", Path.GetRelativePath(certificates, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, true);
                }
            File.Move(temporary, database);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
