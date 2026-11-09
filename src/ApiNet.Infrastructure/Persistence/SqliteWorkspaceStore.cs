using System.Text.Json;
using ApiNet.Core.Interfaces;
using ApiNet.Core.Models;
using ApiNet.Core.Serialization;
using Microsoft.Data.Sqlite;
using ApiNet.Core;

namespace ApiNet.Infrastructure.Persistence;

public sealed class SqliteWorkspaceStore : IWorkspaceStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString;
    public string DataDirectory
    {
        get;
    }

    public SqliteWorkspaceStore(string? directory = null)
    {
        DataDirectory = directory ?? WorkspaceDataPaths.DefaultDirectory;
        if (directory is null && OperatingSystem.IsWindows())
            WorkspaceDataPaths.MigrateLegacyData(WorkspaceDataPaths.LegacyDirectory, DataDirectory);
        Directory.CreateDirectory(DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(DataDirectory, "workspace.db") }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS workspace (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS history (id TEXT PRIMARY KEY, timestamp TEXT NOT NULL, json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_history_time ON history(timestamp DESC);";
        command.ExecuteNonQuery();
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(DataDirectory, "workspace.db"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task<WorkspaceDocument?> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM workspace WHERE id=1";
            var value = command.ExecuteScalar() as string;
            return value is null ? null : JsonSerializer.Deserialize(value, AppJsonContext.Default.WorkspaceDocument);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WorkspaceDocument document)
    {
        var json = JsonSerializer.Serialize(document, AppJsonContext.Default.WorkspaceDocument);
        await _gate.WaitAsync();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO workspace(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
            command.Parameters.AddWithValue("$json", json);
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    public async Task AddHistoryAsync(HistoryEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, AppJsonContext.Default.HistoryEntry);
        await _gate.WaitAsync();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO history(id,timestamp,json) VALUES($id,$time,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$time", entry.Timestamp.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$json", json);
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }

    public async Task<List<HistoryEntry>> GetHistoryAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json_remove(json,'$.request.body','$.request.headers','$.response.body','$.response.headers','$.messages') FROM history ORDER BY timestamp DESC LIMIT 1000";
            using var reader = command.ExecuteReader();
            List<HistoryEntry> entries = [];
            while (reader.Read())
                entries.Add(JsonSerializer.Deserialize(reader.GetString(0), AppJsonContext.Default.HistoryEntry)!);
            return entries;
        }
        finally { _gate.Release(); }
    }

    public async Task<HistoryEntry?> GetHistoryDetailAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM history WHERE id=$id";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize(json, AppJsonContext.Default.HistoryEntry) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearHistoryAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM history";
            command.ExecuteNonQuery();
        }
        finally { _gate.Release(); }
    }
}
