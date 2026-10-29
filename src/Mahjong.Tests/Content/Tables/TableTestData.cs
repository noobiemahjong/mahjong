using System.Buffers.Binary;

namespace Mahjong.Tests.Content.Tables;

/// <summary>
/// 测试用的最小 MessagePack 写入器：只写查表格式用到的类型，独立于被测的 <c>MsgPackLite</c>，
/// 避免「用被测代码生成输入」造成的自证。
/// </summary>
internal sealed class TableBytesWriter
{
    private readonly List<byte> _bytes = new();

    public TableBytesWriter ArrayHeader(int count) => count switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(count)),
        <= 15 => Append([(byte)(0x90 + count)]),
        <= ushort.MaxValue => Append([0xdc, (byte)(count >> 8), (byte)count]),
        _ => Append([0xdd, (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count]),
    };

    public TableBytesWriter MapHeader(int count) => count switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(count)),
        <= 15 => Append([(byte)(0x80 + count)]),
        <= ushort.MaxValue => Append([0xde, (byte)(count >> 8), (byte)count]),
        _ => Append([0xdf, (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count]),
    };

    public TableBytesWriter UInt(ulong value)
    {
        if (value <= 0x7f)
        {
            return Append([(byte)value]);
        }

        if (value <= byte.MaxValue)
        {
            return Append([0xcc, (byte)value]);
        }

        if (value <= ushort.MaxValue)
        {
            return AppendUInt(0xcd, value, 2);
        }

        if (value <= uint.MaxValue)
        {
            return AppendUInt(0xce, value, 4);
        }

        return AppendUInt(0xcf, value, 8);
    }

    public TableBytesWriter Bool(bool value) => Append([value ? (byte)0xc3 : (byte)0xc2]);

    public TableBytesWriter Float64(double value)
    {
        Span<byte> buffer = stackalloc byte[9];
        buffer[0] = 0xcb;
        BinaryPrimitives.WriteUInt64BigEndian(buffer[1..], BitConverter.DoubleToUInt64Bits(value));
        return Append(buffer);
    }

    public TableBytesWriter Raw(params byte[] bytes) => Append(bytes);

    public byte[] ToArray() => _bytes.ToArray();

    private TableBytesWriter AppendUInt(byte code, ulong value, int size)
    {
        Span<byte> buffer = stackalloc byte[9];
        buffer[0] = code;
        Span<byte> payload = buffer.Slice(1, size);
        if (size == 2)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)value);
        }
        else if (size == 4)
        {
            BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(payload, value);
        }

        return Append(buffer[..(size + 1)]);
    }

    private TableBytesWriter Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            _bytes.Add(value);
        }

        return this;
    }
}

/// <summary>查表文件的固定文件名与合法/畸形文档构造器。</summary>
internal static class TableTestData
{
    public const string NormalWin = "dictionary_normal.bytes";
    public const string WindWin = "dictionary_wind.bytes";
    public const string DragonWin = "dictionary_dragon.bytes";
    public const string NormalAi = "ai_dictionary_normal.bytes";
    public const string WindAi = "ai_dictionary_wind.bytes";
    public const string DragonAi = "ai_dictionary_dragon.bytes";

    public static string[] AllFileNames =>
    [
        NormalWin, WindWin, DragonWin, NormalAi, WindAi, DragonAi,
    ];

    /// <summary>一条胡牌行：[needJoker, hasPair, hasWin, winningRanks]。</summary>
    public static byte[] WinRow(int needJoker, bool hasPair, bool hasWin, params int[] winningRanks)
    {
        TableBytesWriter writer = new TableBytesWriter()
            .ArrayHeader(4)
            .UInt((ulong)needJoker)
            .Bool(hasPair)
            .Bool(hasWin)
            .ArrayHeader(winningRanks.Length);
        foreach (int rank in winningRanks)
        {
            writer.UInt((ulong)rank);
        }

        return writer.ToArray();
    }

    /// <summary>一条牌效行：[hasPair, probability]。</summary>
    public static byte[] AiRow(bool hasPair, double probability) =>
        new TableBytesWriter().ArrayHeader(2).Bool(hasPair).Float64(probability).ToArray();

    /// <summary>array(1) -> map&lt;ulong, list&lt;row&gt;&gt; 的胡牌表。</summary>
    public static byte[] WinTable(params (ulong Key, byte[][] Rows)[] entries)
    {
        TableBytesWriter writer = new TableBytesWriter().ArrayHeader(1).MapHeader(entries.Length);
        foreach ((ulong key, byte[][] rows) in entries)
        {
            writer.UInt(key).ArrayHeader(rows.Length);
            foreach (byte[] row in rows)
            {
                writer.Raw(row);
            }
        }

        return writer.ToArray();
    }

    /// <summary>array(1) -> map&lt;ulong, list&lt;row&gt;&gt; 的牌效表。</summary>
    public static byte[] AiTable(params (ulong Key, byte[][] Rows)[] entries)
    {
        TableBytesWriter writer = new TableBytesWriter().ArrayHeader(1).MapHeader(entries.Length);
        foreach ((ulong key, byte[][] rows) in entries)
        {
            writer.UInt(key).ArrayHeader(rows.Length);
            foreach (byte[] row in rows)
            {
                writer.Raw(row);
            }
        }

        return writer.ToArray();
    }

    /// <summary>6 个文件都合法的最小内容集（每表 1 个 key）。</summary>
    public static Dictionary<string, byte[]> ValidFiles() => new(StringComparer.Ordinal)
    {
        [NormalWin] = WinTable((0, [WinRow(2, hasPair: true, hasWin: false, [0, 1, 1, 1, 1, 1, 1, 1, 1])])),
        [WindWin] = WinTable((0, [WinRow(0, hasPair: false, hasWin: false, [0, 0, 0, 1])])),
        [DragonWin] = WinTable((0, [WinRow(1, hasPair: true, hasWin: false, [0, 0, 1])])),
        [NormalAi] = AiTable((0, [AiRow(hasPair: true, 0.25)])),
        [WindAi] = AiTable((0, [AiRow(hasPair: false, 0.5)])),
        [DragonAi] = AiTable((0, [AiRow(hasPair: true, 0.75)])),
    };
}

/// <summary>系统临时目录下的查表目录，测试结束删除。</summary>
internal sealed class TempTableDirectory : IDisposable
{
    private TempTableDirectory(string path) => Path = path;

    public string Path { get; }

    public static TempTableDirectory Create() => CreateAt(System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "mahjong-tables-" + Guid.NewGuid().ToString("N")));

    public static TempTableDirectory CreateValid() => CreateWith(TableTestData.ValidFiles());

    public static TempTableDirectory CreateWith(IReadOnlyDictionary<string, byte[]> files)
    {
        TempTableDirectory directory = Create();
        directory.WriteAll(files);
        return directory;
    }

    public static TempTableDirectory CreateAt(string path)
    {
        Directory.CreateDirectory(path);
        return new TempTableDirectory(path);
    }

    public string FilePath(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Write(string fileName, byte[] bytes) => File.WriteAllBytes(FilePath(fileName), bytes);

    public void WriteAll(IReadOnlyDictionary<string, byte[]> files)
    {
        foreach ((string fileName, byte[] bytes) in files)
        {
            Write(fileName, bytes);
        }
    }

    public void Delete(string fileName) => File.Delete(FilePath(fileName));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }
}
