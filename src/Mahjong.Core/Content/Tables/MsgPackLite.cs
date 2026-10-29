using System.Buffers.Binary;

namespace Mahjong.Core.Content.Tables;

/// <summary>
/// 手写 MessagePack 子集读取器，只接受查表文件实际使用的编码：
/// positive fixint(0x00–0x7f)、uint8/16/32/64(0xcc–0xcf)、fixarray(0x90–0x9f)/array16/32、
/// fixmap(0x80–0x8f)/map16/32、bool(0xc2/0xc3)、float64(0xcb)。
/// 其余类型码（str/bin/nil/负数 fixint/int8/16/32/64/float32/ext 等）不在子集内，遇到即抛
/// <see cref="InvalidDataException"/>，不做兼容性跳过。
/// 长度一律按头部声明读取：头长度超过剩余字节立即失败，不按最坏长度预分配。
/// </summary>
public sealed class MsgPackLite
{
    private readonly ReadOnlyMemory<byte> _payload;
    private int _offset;

    /// <param name="payload">完整文档字节。</param>
    /// <param name="label">诊断前缀（通常是文件路径），拼进所有异常消息。</param>
    public MsgPackLite(ReadOnlyMemory<byte> payload, string? label = null)
    {
        _payload = payload;
        Label = label;
    }

    /// <summary>异常消息前缀，通常是查表文件路径；调用方可补充 key 等上下文。</summary>
    public string? Label { get; set; }

    /// <summary>已消费的字节数，用于错误定位。</summary>
    public int Offset => _offset;

    /// <summary>文档总字节数。</summary>
    public int Length => _payload.Length;

    /// <summary>是否已消费到文档末尾。</summary>
    public bool IsAtEnd => _offset >= _payload.Length;

    /// <summary>读非负整数（positive fixint / uint8 / uint16 / uint32 / uint64）。</summary>
    /// <param name="context">错误消息中的字段说明。</param>
    /// <exception cref="InvalidDataException">类型码不在子集内或数据被截断。</exception>
    public ulong ReadUInt64(string context = "")
    {
        int start = _offset;
        byte code = ReadTypeCode(context);
        return code switch
        {
            <= 0x7f => code,
            0xcc => ReadBigEndian(1, start, context),
            0xcd => ReadBigEndian(2, start, context),
            0xce => ReadBigEndian(4, start, context),
            0xcf => ReadBigEndian(8, start, context),
            _ => throw Unsupported(code, start, context),
        };
    }

    /// <summary>读整数并保证落在 <see cref="int"/> 范围内。</summary>
    /// <exception cref="InvalidDataException">类型码不在子集内、被截断或超出范围和。</exception>
    public int ReadInt32(string context = "")
    {
        int start = _offset;
        ulong value = ReadUInt64(context);
        if (value > int.MaxValue)
        {
            throw Error(Describe(context) + $"integer value {value} at offset {start} exceeds Int32 range");
        }

        return (int)value;
    }

    /// <summary>读 bool（仅 0xc2 / 0xc3）。</summary>
    /// <exception cref="InvalidDataException">类型码不是 bool 或数据被截断。</exception>
    public bool ReadBool(string context = "")
    {
        int start = _offset;
        byte code = ReadTypeCode(context);
        return code switch
        {
            0xc2 => false,
            0xc3 => true,
            _ => throw Unsupported(code, start, context),
        };
    }

    /// <summary>读 float64（仅 0xcb；float32 不属于子集）。</summary>
    /// <exception cref="InvalidDataException">类型码不是 float64 或数据被截断。</exception>
    public double ReadDouble(string context = "")
    {
        int start = _offset;
        byte code = ReadTypeCode(context);
        if (code != 0xcb)
        {
            throw Unsupported(code, start, context);
        }

        return BitConverter.UInt64BitsToDouble(ReadBigEndian(8, start, context));
    }

    /// <summary>读数组头部，返回元素个数（fixarray / array16 / array32）。</summary>
    /// <exception cref="InvalidDataException">类型码不是数组、数据被截断，或声明的元素数超过剩余字节。</exception>
    public int ReadArrayHeader(string context = "")
    {
        int start = _offset;
        byte code = ReadTypeCode(context);
        ulong count = code switch
        {
            >= 0x90 and <= 0x9f => (ulong)(code - 0x90),
            0xdc => ReadBigEndian(2, start, context),
            0xdd => ReadBigEndian(4, start, context),
            _ => throw Unsupported(code, start, context),
        };
        return GuardElementCount(count, start, context, bytesPerElement: 1, "array");
    }

    /// <summary>读映射头部，返回键值对个数（fixmap / map16 / map32）。</summary>
    /// <exception cref="InvalidDataException">类型码不是映射、数据被截断，或声明的条目数超过剩余字节。</exception>
    public int ReadMapHeader(string context = "")
    {
        int start = _offset;
        byte code = ReadTypeCode(context);
        ulong count = code switch
        {
            >= 0x80 and <= 0x8f => (ulong)(code - 0x80),
            0xde => ReadBigEndian(2, start, context),
            0xdf => ReadBigEndian(4, start, context),
            _ => throw Unsupported(code, start, context),
        };
        return GuardElementCount(count, start, context, bytesPerElement: 2, "map");
    }

    /// <summary>断言文档已全部消费；用于拒绝尾随字节。</summary>
    /// <exception cref="InvalidDataException">还有未消费字节。</exception>
    public void EnsureEnd(string context = "")
    {
        int trailing = _payload.Length - _offset;
        if (trailing != 0)
        {
            throw Error(Describe(context) + $"{trailing} trailing byte(s) after the document at offset {_offset}");
        }
    }

    private int GuardElementCount(ulong count, int start, string context, int bytesPerElement, string kind)
    {
        long remaining = _payload.Length - _offset;
        if (count > (ulong)(remaining / bytesPerElement))
        {
            throw Error(Describe(context) +
                $"{kind} header at offset {start} declares {count} element(s) but only {remaining} byte(s) remain");
        }

        return (int)count;
    }

    private ulong ReadBigEndian(int size, int start, string context)
    {
        int remaining = _payload.Length - _offset;
        if (remaining < size)
        {
            throw Error(Describe(context) + $"truncated at offset {start}: need {size} byte(s), only {remaining} remain");
        }

        ReadOnlySpan<byte> span = _payload.Span.Slice(_offset, size);
        _offset += size;
        return size switch
        {
            1 => span[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(span),
            4 => BinaryPrimitives.ReadUInt32BigEndian(span),
            _ => BinaryPrimitives.ReadUInt64BigEndian(span),
        };
    }

    private byte ReadTypeCode(string context)
    {
        if (_offset >= _payload.Length)
        {
            throw Error(Describe(context) + $"unexpected end of data at offset {_offset}");
        }

        return _payload.Span[_offset++];
    }

    private InvalidDataException Unsupported(byte code, int start, string context) =>
        Error(Describe(context) + $"unsupported MessagePack type code 0x{code:X2} at offset {start}");

    private static string Describe(string context) => context.Length == 0 ? string.Empty : context + ": ";

    private InvalidDataException Error(string message) =>
        new(string.IsNullOrEmpty(Label) ? message : Label + ": " + message);
}
