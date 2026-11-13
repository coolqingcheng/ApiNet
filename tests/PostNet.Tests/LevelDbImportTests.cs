using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using PostNet.Core.Interfaces;
using PostNet.Infrastructure.Imports.Local;

namespace PostNet.Tests;

public class LevelDbImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "apinet-leveldb-" + Guid.NewGuid().ToString("N"));
    public LevelDbImportTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    [Fact]
    public void ManifestSelectsLiveCompressedTablesAndWalAppliesLatestValuesAndTombstones()
    {
        var manifest = new List<byte>();
        manifest.AddRange(Var(2)); manifest.AddRange(Var(10));
        AddTable(manifest, 1); AddTable(manifest, 2);
        manifest.AddRange(Var(6)); manifest.AddRange(Var(0)); manifest.AddRange(Var(2));
        File.WriteAllText(Path.Combine(_root, "CURRENT"), "MANIFEST-000001\n");
        File.WriteAllBytes(Path.Combine(_root, "MANIFEST-000001"), Log(manifest.ToArray()));
        File.WriteAllBytes(Path.Combine(_root, "000001.ldb"), Table([(Key(1), 1UL, "old"u8.ToArray()), (Key(2), 2UL, "deleted"u8.ToArray()), (Key(3), 3UL, Encoding.UTF8.GetBytes(new string('a', 80)))]));
        // Obsolete table is deliberately corrupt: it must never be read.
        File.WriteAllBytes(Path.Combine(_root, "000002.ldb"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_root, "000010.log"), Log(Batch(20, (Key(1), "new"u8.ToArray()), (Key(2), null), (new byte[] { 0, 0, 0, 0 }, "metadata"u8.ToArray()))));
        // A WAL below the manifest's current log number is obsolete.
        File.WriteAllBytes(Path.Combine(_root, "000009.log"), Log(Batch(99, (Key(1), "wrong"u8.ToArray()))));
        var values = LevelDbSnapshot.ReadIndexedDbValues(_root).Select(Encoding.UTF8.GetString).ToList();
        Assert.Equal(2, values.Count);
        Assert.Contains("new", values);
        Assert.Contains(new string('a', 80), values);
    }
    [Fact]
    public void FragmentedWalRecordsAreReassembledAndCorruptionFailsClosed()
    {
        InitializeEmpty();
        var value = Encoding.UTF8.GetBytes(new string('z', 70000));
        var path = Path.Combine(_root, "000001.log");
        File.WriteAllBytes(path, Log(Batch(1, (Key(1), value))));
        Assert.Equal(value, Assert.Single(LevelDbSnapshot.ReadIndexedDbValues(_root)));
        var corrupt = File.ReadAllBytes(path); corrupt[30] ^= 1; File.WriteAllBytes(path, corrupt);
        Assert.Throws<FormatException>(() => LevelDbSnapshot.ReadIndexedDbValues(_root));
        File.WriteAllBytes(path, Log(Batch(1, (Key(1), value)))[..^3]);
        Assert.Throws<FormatException>(() => LevelDbSnapshot.ReadIndexedDbValues(_root));
    }
    [Fact]
    public async Task SnapshotCopiesWithSharedReadAndLeavesOriginalBytesAndLockUntouched()
    {
        InitializeEmpty();
        var log = Path.Combine(_root, "000001.log");
        var expected = Log(Batch(1, (Key(1), "value"u8.ToArray())));
        File.WriteAllBytes(log, expected);
        using var heldOpen = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        using var locked = new FileStream(Path.Combine(_root, "LOCK"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var destination = Path.Combine(_root, "copy");
        await LocalImportReader.CopySnapshotAsync(_root, destination);
        Assert.False(File.Exists(Path.Combine(destination, "LOCK")));
        heldOpen.Dispose();
        Assert.Equal(expected, await File.ReadAllBytesAsync(log));
        Assert.Equal("value", Encoding.UTF8.GetString(Assert.Single(LevelDbSnapshot.ReadIndexedDbValues(destination))));
    }
    [Fact]
    public async Task DetectsLeftoverPostmanDataAndChoosesLatestBackupWithoutChangingIt()
    {
        var directory = Path.Combine(_root, "Postman"); Directory.CreateDirectory(directory);
        var older = Path.Combine(directory, "backup-2025-01-01.json");
        var newest = Path.Combine(directory, "backup-2025-02-01.json");
        await File.WriteAllTextAsync(older, "{\"collections\":[]}");
        const string json = """{"collections":[],"environments":[{"id":"e","name":"Latest","values":[{"key":"host","value":"https://example.test"}]}]}""";
        await File.WriteAllTextAsync(newest, json);
        var reader = new LocalImportReader(_root, Path.Combine(_root, "programs"), _root);
        var source = Assert.Single(await reader.DetectAsync());
        Assert.Null(source.ExecutablePath);
        Assert.Equal(newest, source.BackupPath);
        var result = JsonNode.Parse(await reader.ReadAsync(source))!;
        Assert.Equal("Latest", result["environments"]![0]!["name"]!.GetValue<string>());
        Assert.NotNull(result["apinetWarnings"]);
        Assert.Equal(json, await File.ReadAllTextAsync(newest));
    }
    [Fact]
    public async Task ReadsSyntheticIndexedDbUsingLocalV8RuntimeAndFiltersAccountRecords()
    {
        var database = Path.Combine(_root, "IndexedDB", "file__0.indexeddb.leveldb"); Directory.CreateDirectory(database);
        File.WriteAllText(Path.Combine(database, "CURRENT"), "MANIFEST-000001\n");
        File.WriteAllBytes(Path.Combine(database, "MANIFEST-000001"), Log([2, 1]));
        // V8 wire version 15: {target_id:'r', target_type:'api', project_id:'p',
        // name:'Synthetic', url:'https://example.test'}; no real user data.
        var encoded = Convert.FromBase64String(V8Fixture);
        File.WriteAllBytes(Path.Combine(database, "000001.log"), Log(Batch(1, (Key(1), new byte[] { 1, 0 }.Concat(encoded).ToArray()), (Key(2), Convert.FromBase64String(AccountFixture)))));
        var source = new LocalImportSource("test", "ApiPost", "ApiPost", _root);
        var result = JsonNode.Parse(await new LocalImportReader(_root, _root, _root).ReadAsync(source))!;
        Assert.Equal("r", Assert.Single(result["records"]!.AsArray())!["target_id"]!.GetValue<string>());
        Assert.Equal("ApiPost", result["apinetLocalFormat"]!.GetValue<string>());
        Assert.Contains(LevelDbSnapshot.ReadIndexedDbValues(database), x => x.SequenceEqual(new byte[] { 1, 0 }.Concat(encoded)));
        // Partial data must not become a misleading incremental preview.
        File.WriteAllBytes(Path.Combine(database, "000001.log"), Log(Batch(1, (Key(1), encoded), (Key(2), new byte[] { 1, 2, 3 }))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalImportReader(_root, _root, _root).ReadAsync(source));
    }
    private const string AccountFixture = "/w9vIgJpZCIHYWNjb3VudCIEYXV0aG8iBXRva2VuIhdzeW50aGV0aWMtcHJpdmF0ZS12YWx1ZXsBewI=";
    private const string V8Fixture = "/w9vIgl0YXJnZXRfaWQiAXIiC3RhcmdldF90eXBlIgNhcGkiCnByb2plY3RfaWQiAXAiBG5hbWUiCVN5bnRoZXRpYyIDdXJsIhRodHRwczovL2V4YW1wbGUudGVzdHsF";
    private void InitializeEmpty()
    {
        File.WriteAllText(Path.Combine(_root, "CURRENT"), "MANIFEST-000001\n");
        File.WriteAllBytes(Path.Combine(_root, "MANIFEST-000001"), Log([2, 1]));
    }
    private static byte[] Key(byte id) => [0, 1, 1, 1, id];
    private static byte[] Var(ulong value) { var bytes = new List<byte>(); while (value >= 128) { bytes.Add((byte)(value | 128)); value >>= 7; } bytes.Add((byte)value); return bytes.ToArray(); }
    private static byte[] Field(byte[] value) => Var((ulong)value.Length).Concat(value).ToArray();
    private static void AddTable(List<byte> bytes, ulong number) { foreach (var value in new ulong[] { 7, 0, number, 100, 0, 0 }) bytes.AddRange(Var(value)); }
    private static byte[] Batch(ulong sequence, params (byte[] Key, byte[]? Value)[] entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(sequence); writer.Write(entries.Length);
        foreach (var (key, value) in entries) { writer.Write((byte)(value is null ? 0 : 1)); writer.Write(Field(key)); if (value is not null) writer.Write(Field(value)); }
        return stream.ToArray();
    }
    private static byte[] Log(byte[] record)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var position = 0;
        while (position < record.Length)
        {
            var available = 32768 - (int)(stream.Position % 32768) - 7;
            if (available <= 0) { writer.Write(new byte[available + 7]); continue; }
            var length = Math.Min(available, record.Length - position);
            var type = (byte)(position == 0 ? length == record.Length ? 1 : 2 : position + length == record.Length ? 4 : 3);
            var data = record.AsSpan(position, length).ToArray();
            writer.Write(Crc(new[] { type }.Concat(data).ToArray())); writer.Write((ushort)length); writer.Write(type); writer.Write(data); position += length;
        }
        return stream.ToArray();
    }
    private static byte[] Block(IEnumerable<(byte[] Key, byte[] Value)> entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var (key, value) in entries) { writer.Write((byte)0); writer.Write(Var((ulong)key.Length)); writer.Write(Var((ulong)value.Length)); writer.Write(key); writer.Write(value); }
        writer.Write(0); writer.Write(1); return stream.ToArray();
    }
    private static byte[] Table((byte[] Key, ulong Sequence, byte[] Value)[] values)
    {
        var data = Block(values.Select(x => (x.Key.Concat(BitConverter.GetBytes((x.Sequence << 8) | 1)).ToArray(), x.Value)));
        var compressed = Compress(data);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Append(byte[] block, byte type) { writer.Write(block); writer.Write(type); writer.Write(Crc(block.Concat(new[] { type }).ToArray())); }
        Append(compressed, 1);
        var indexOffset = (ulong)stream.Position;
        var index = Block([(new byte[] { 255 }, Var(0).Concat(Var((ulong)compressed.Length)).ToArray())]); Append(index, 0);
        var footer = new byte[48]; var handle = new byte[] { 0, 0 }.Concat(Var(indexOffset)).Concat(Var((ulong)index.Length)).ToArray(); handle.CopyTo(footer, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(footer.AsSpan(40), 0xdb4775248b80fb57UL); writer.Write(footer); return stream.ToArray();
    }
    private static byte[] Compress(byte[] input)
    {
        // Literal encoding plus overlapping copy-2 runs exercises real Snappy
        // decoding without depending on an external native compression library.
        var output = new List<byte>(Var((ulong)input.Length));
        for (var i = 0; i < input.Length;)
        {
            var count = 0;
            if (i > 0) while (count < 64 && i + count < input.Length && input[i + count] == input[i - 1]) count++;
            if (count >= 4) { output.Add((byte)(((count - 1) << 2) | 2)); output.Add(1); output.Add(0); i += count; }
            else { output.Add(0); output.Add(input[i++]); }
        }
        return output.ToArray();
    }
    private static uint Crc(byte[] bytes)
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++) { var r = n; for (var bit = 0; bit < 8; bit++) r = (r >> 1) ^ ((r & 1) == 0 ? 0 : 0x82f63b78u); table[n] = r; }
        uint value = uint.MaxValue; foreach (var b in bytes) value = table[(value ^ b) & 255] ^ (value >> 8);
        value = ~value; return unchecked(((value >> 15) | (value << 17)) + 0xa282ead8u);
    }
}
