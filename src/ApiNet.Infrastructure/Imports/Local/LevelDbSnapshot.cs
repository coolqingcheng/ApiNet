using System.Buffers.Binary;

namespace ApiNet.Infrastructure.Imports.Local;

// Read Chromium's copied LevelDB files without opening its database or acquiring
// the original application's LOCK. The manifest selects live tables; sequence
// numbers and tombstones prevent old/deleted requests from being resurrected.
public static class LevelDbSnapshot
{
    private const int MaximumBlock = 64 * 1024 * 1024;
    public static IReadOnlyList<byte[]> ReadIndexedDbValues(string directory)
    {
        var manifest = File.ReadAllText(Path.Combine(directory, "CURRENT")).Trim();
        if (!manifest.StartsWith("MANIFEST-", StringComparison.Ordinal) || Path.GetFileName(manifest) != manifest)
            throw new FormatException("无法识别本地数据库的 CURRENT 文件。");
        HashSet<ulong> tables = [];
        ulong logNumber = 0, previousLog = 0;
        foreach (var record in LogRecords(File.ReadAllBytes(Path.Combine(directory, manifest))))
        {
            var position = 0;
            while (position < record.Length)
            {
                switch (Varint(record, ref position))
                {
                    case 1: _ = Slice(record, ref position); break;
                    case 2: logNumber = Varint(record, ref position); break;
                    case 3 or 4: _ = Varint(record, ref position); break;
                    case 5: _ = Varint(record, ref position); _ = Slice(record, ref position); break;
                    case 6: _ = Varint(record, ref position); tables.Remove(Varint(record, ref position)); break;
                    case 7:
                        _ = Varint(record, ref position);
                        tables.Add(Varint(record, ref position));
                        _ = Varint(record, ref position); _ = Slice(record, ref position); _ = Slice(record, ref position);
                        break;
                    case 9: previousLog = Varint(record, ref position); break;
                    default: throw new FormatException("本地数据库使用了尚不支持的 LevelDB 格式。");
                }
            }
        }
        var entries = new Dictionary<string, (ulong Sequence, byte[]? Value)>(StringComparer.Ordinal);
        void Add(byte[] key, ulong sequence, byte[]? value)
        {
            if (!IsObjectStoreData(key)) return;
            var id = Convert.ToBase64String(key);
            if (!entries.TryGetValue(id, out var old) || sequence >= old.Sequence) entries[id] = (sequence, value);
        }
        foreach (var number in tables)
        {
            var path = Path.Combine(directory, number.ToString("D6") + ".ldb");
            if (!File.Exists(path)) path = Path.Combine(directory, number.ToString("D6") + ".sst");
            var file = File.ReadAllBytes(path);
            if (file.Length < 48 || BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(file.Length - 8)) != 0xdb4775248b80fb57UL)
                throw new FormatException("本地数据库表文件不完整，请关闭来源软件后重试。");
            var offset = file.Length - 48;
            _ = Varint(file, ref offset); _ = Varint(file, ref offset);
            var indexOffset = Varint(file, ref offset); var indexSize = Varint(file, ref offset);
            foreach (var (_, handle) in BlockEntries(ReadBlock(file, indexOffset, indexSize)))
            {
                offset = 0;
                var dataOffset = Varint(handle, ref offset); var dataSize = Varint(handle, ref offset);
                foreach (var (key, value) in BlockEntries(ReadBlock(file, dataOffset, dataSize)))
                {
                    if (key.Length < 8) throw new FormatException("本地数据库键不完整。");
                    var tag = BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(key.Length - 8));
                    if ((tag & 255) > 1) throw new FormatException("数据库值类型无效。");
                    Add(key[..^8], tag >> 8, (tag & 255) == 0 ? null : value);
                }
            }
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*.log"))
        {
            if (!ulong.TryParse(Path.GetFileNameWithoutExtension(path), out var number) || number < logNumber && number != previousLog) continue;
            foreach (var record in LogRecords(File.ReadAllBytes(path)))
            {
                if (record.Length < 12) throw new FormatException("本地数据库日志不完整。");
                var sequence = BinaryPrimitives.ReadUInt64LittleEndian(record);
                var count = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8));
                var offset = 12;
                for (uint i = 0; i < count; i++)
                {
                    if (offset >= record.Length) throw new FormatException("本地数据库日志条目不完整。");
                    var type = record[offset++];
                    var key = Slice(record, ref offset);
                    var value = type == 1 ? Slice(record, ref offset) : type == 0 ? null : throw new FormatException("未知数据库日志条目。");
                    Add(key, sequence + i, value);
                }
            }
        }
        return entries.Values.Where(x => x.Value is not null).Select(x => x.Value!).ToArray();
    }

    private static bool IsObjectStoreData(byte[] key)
    {
        if (key.Length < 4) return false;
        var databaseBytes = (key[0] >> 5) + 1;
        var storeBytes = ((key[0] >> 2) & 7) + 1;
        var indexBytes = (key[0] & 3) + 1;
        if (key.Length < 1 + databaseBytes + storeBytes + indexBytes) return false;
        ulong Read(int start, int length) { ulong value = 0; for (var i = 0; i < length; i++) value |= (ulong)key[start + i] << (i * 8); return value; }
        return Read(1, databaseBytes) > 0 && Read(1 + databaseBytes, storeBytes) > 0 && Read(1 + databaseBytes + storeBytes, indexBytes) == 1;
    }
    private static byte[] ReadBlock(byte[] file, ulong offset, ulong length)
    {
        if (length > MaximumBlock || offset > (ulong)file.Length || length + 5 > (ulong)file.Length - offset) throw new FormatException("数据库块超出文件范围。");
        var data = file.AsSpan((int)offset, (int)length);
        if (MaskedCrc(file.AsSpan((int)offset, (int)length + 1)) != BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)(offset + length + 1))))
            throw new FormatException("本地数据库校验失败，请关闭原软件后重试。");
        return file[(int)(offset + length)] switch { 0 => data.ToArray(), 1 => Snappy(data.ToArray()), _ => throw new FormatException("不支持的数据库压缩格式。") };
    }
    private static IEnumerable<(byte[] Key, byte[] Value)> BlockEntries(byte[] block)
    {
        if (block.Length < 4) throw new FormatException("数据库块不完整。");
        var restarts = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(block.Length - 4));
        if (restarts > (block.Length - 4) / 4) throw new FormatException("数据库重启索引无效。");
        var end = block.Length - 4 - (int)restarts * 4;
        var position = 0;
        byte[] previous = [];
        while (position < end)
        {
            var shared = Varint(block, ref position); var keyLength = Varint(block, ref position); var valueLength = Varint(block, ref position);
            if (shared > (ulong)previous.Length || keyLength + valueLength > (ulong)(end - position)) throw new FormatException("数据库条目长度无效。");
            var key = new byte[(int)(shared + keyLength)];
            previous.AsSpan(0, (int)shared).CopyTo(key);
            block.AsSpan(position, (int)keyLength).CopyTo(key.AsSpan((int)shared));
            position += (int)keyLength;
            var value = block.AsSpan(position, (int)valueLength).ToArray(); position += (int)valueLength;
            yield return (key, value);
            previous = key;
        }
    }
    private static IEnumerable<byte[]> LogRecords(byte[] file)
    {
        using var fragmented = new MemoryStream();
        var fragmentStarted = false;
        for (var offset = 0; offset + 7 <= file.Length;)
        {
            var remaining = 32768 - offset % 32768;
            if (remaining < 7) { offset += remaining; continue; }
            var length = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(offset + 4));
            var type = file[offset + 6];
            if (type == 0 && length == 0) { offset += remaining; continue; }
            if (length + 7 > remaining || offset + length + 7 > file.Length) throw new FormatException("数据库日志末尾不完整，请关闭原软件后重试。");
            var crcData = new byte[length + 1]; crcData[0] = type;
            file.AsSpan(offset + 7, length).CopyTo(crcData.AsSpan(1));
            if (MaskedCrc(crcData) != BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset))) throw new FormatException("数据库日志校验失败。");
            var data = file.AsSpan(offset + 7, length).ToArray(); offset += 7 + length;
            if (type == 1) { if (fragmentStarted) throw new FormatException("数据库日志分片缺失。"); fragmented.SetLength(0); yield return data; }
            else if (type == 2) { if (fragmentStarted) throw new FormatException("数据库日志分片缺失。"); fragmentStarted = true; fragmented.SetLength(0); fragmented.Write(data); }
            else if (type is 3 or 4)
            {
                if (!fragmentStarted) throw new FormatException("数据库日志分片缺失。");
                if (fragmented.Length + length > MaximumBlock) throw new FormatException("数据库日志记录过大。");
                fragmented.Write(data);
                if (type == 4) { yield return fragmented.ToArray(); fragmented.SetLength(0); fragmentStarted = false; }
            }
            else throw new FormatException("未知数据库日志类型。");
        }
        if (fragmentStarted) throw new FormatException("数据库日志分片不完整。");
    }
    private static uint MaskedCrc(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0x82f63b78u : 0);
        }
        crc = ~crc;
        return unchecked(((crc >> 15) | (crc << 17)) + 0xa282ead8u);
    }
    private static ulong Varint(byte[] data, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64 && position < data.Length; shift += 7)
        {
            var part = data[position++]; value |= (ulong)(part & 127) << shift;
            if (shift == 63 && part > 1) throw new FormatException("数据库整数溢出。");
            if (part < 128) return value;
        }
        throw new FormatException("数据库整数编码无效。");
    }
    private static byte[] Slice(byte[] data, ref int position)
    {
        var length = Varint(data, ref position);
        if (length > (ulong)(data.Length - position)) throw new FormatException("数据库字段长度无效。");
        var value = data.AsSpan(position, (int)length).ToArray(); position += (int)length; return value;
    }
    private static byte[] Snappy(byte[] data)
    {
        var input = 0;
        var length = Varint(data, ref input);
        if (length > MaximumBlock) throw new FormatException("解压数据库块过大。");
        var output = new byte[(int)length]; var written = 0;
        while (input < data.Length && written < output.Length)
        {
            var tag = data[input++]; var kind = tag & 3;
            int count, distance;
            if (kind == 0)
            {
                count = tag >> 2;
                if (count < 60) count++;
                else
                {
                    var bytes = count - 59; count = 0;
                    if (input + bytes > data.Length) throw new FormatException("Snappy 字面量不完整。");
                    for (var i = 0; i < bytes; i++) count |= data[input++] << (8 * i);
                    count++;
                }
                if (count < 0 || count > data.Length - input || count > output.Length - written) throw new FormatException("Snappy 字面量长度无效。");
                data.AsSpan(input, count).CopyTo(output.AsSpan(written)); input += count; written += count; continue;
            }
            var distanceBytes = kind == 1 ? 1 : kind == 2 ? 2 : 4;
            if (input + distanceBytes > data.Length) throw new FormatException("Snappy 引用不完整。");
            count = kind == 1 ? 4 + ((tag >> 2) & 7) : 1 + (tag >> 2);
            distance = kind == 1 ? (tag & 224) << 3 : 0;
            for (var i = 0; i < distanceBytes; i++) distance |= data[input++] << (8 * i);
            if (distance <= 0 || distance > written || count > output.Length - written) throw new FormatException("Snappy 引用无效。");
            for (var i = 0; i < count; i++) output[written + i] = output[written - distance + i];
            written += count;
        }
        if (written != output.Length || input != data.Length) throw new FormatException("数据库块解压不完整。");
        return output;
    }
}
