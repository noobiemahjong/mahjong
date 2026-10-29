using System.Buffers.Binary;
using System.Text;

namespace Mahjong.Tests.Content;

/// <summary>
/// 测试专用最小 MessagePack 写入器。
/// </summary>
/// <remarks>
/// 独立于生产 <c>Mahjong.Core.Content.Tables.MsgPackLite</c>（不调用它、不共享代码）：
/// 负向测试若用被测实现自己造字节，读失败无法区分「写入器错」与「读取器错」。
/// 只覆盖本仓库表数据用到的子集，另可写非法类型码/非法长度头，供拒绝路径测试使用。
/// 编码规范见 https://github.com/msgpack/msgpack/blob/master/spec.md。
/// </remarks>
internal sealed class TestMsgPackWriter
{
    private readonly List<byte> _bytes = [];

    /// <summary>返回已写入字节的副本。</summary>
    public byte[] ToArray() => [.. _bytes];

    /// <summary>原样追加字节（用于构造被测读取器应拒绝的非法类型码/截断数据）。</summary>
    public TestMsgPackWriter Bytes(params byte[] values)
    {
        _bytes.AddRange(values);
        return this;
    }

    /// <summary>positive fixint（0x00–0x7f）。</summary>
    public TestMsgPackWriter FixInt(byte value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 0x7f);
        _bytes.Add(value);
        return this;
    }

    /// <summary>uint8（0xcc）。</summary>
    public TestMsgPackWriter UInt8(byte value) => Bytes(0xcc, value);

    /// <summary>uint16（0xcd，大端）。</summary>
    public TestMsgPackWriter UInt16(ushort value) => Bytes(0xcd, (byte)(value >> 8), (byte)value);

    /// <summary>uint32（0xce，大端）。</summary>
    public TestMsgPackWriter UInt32(uint value) =>
        Bytes(0xce, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    /// <summary>uint64（0xcf，大端）。</summary>
    public TestMsgPackWriter UInt64(ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        _bytes.Add(0xcf);
        _bytes.AddRange(buffer);
        return this;
    }

    /// <summary>最小无符号编码：fixint / uint8 / uint16 / uint32 / uint64 中能容纳 value 的最短者。</summary>
    public TestMsgPackWriter UInt(ulong value)
    {
        if (value <= 0x7f)
        {
            return FixInt((byte)value);
        }

        if (value <= byte.MaxValue)
        {
            return UInt8((byte)value);
        }

        if (value <= ushort.MaxValue)
        {
            return UInt16((ushort)value);
        }

        return value <= uint.MaxValue ? UInt32((uint)value) : UInt64(value);
    }

    /// <summary>有符号整数；非负走 <see cref="UInt"/>，负数走 msgpack 的有符号编码（负向测试可用）。</summary>
    public TestMsgPackWriter Int(long value)
    {
        if (value >= 0)
        {
            return UInt((ulong)value);
        }

        if (value >= -32)
        {
            _bytes.Add((byte)(value & 0xff));
            return this;
        }

        if (value >= sbyte.MinValue)
        {
            return Bytes(0xd0, (byte)(value & 0xff));
        }

        if (value >= short.MinValue)
        {
            return Bytes(0xd1, (byte)(value >> 8), (byte)value);
        }

        if (value >= int.MinValue)
        {
            return Bytes(0xd2, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        _bytes.Add(0xd3);
        _bytes.AddRange(buffer);
        return this;
    }

    /// <summary>bool（0xc2 false / 0xc3 true）。</summary>
    public TestMsgPackWriter Bool(bool value) => Bytes(value ? (byte)0xc3 : (byte)0xc2);

    /// <summary>float64（0xcb，大端 IEEE-754）。</summary>
    public TestMsgPackWriter Float64(double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        _bytes.Add(0xcb);
        _bytes.AddRange(buffer);
        return this;
    }

    /// <summary>float32（0xca，大端）；本仓库表数据不用，供非法类型码测试使用。</summary>
    public TestMsgPackWriter Float32(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        _bytes.Add(0xca);
        _bytes.AddRange(buffer);
        return this;
    }

    /// <summary>nil（0xc0）；本仓库表数据不用，供非法类型码测试使用。</summary>
    public TestMsgPackWriter Nil() => Bytes(0xc0);

    /// <summary>UTF-8 字符串（fixstr / str8 / str16）；本仓库表数据不用，供非法类型码测试使用。</summary>
    public TestMsgPackWriter String(string value)
    {
        var payload = Encoding.UTF8.GetBytes(value);
        if (payload.Length <= 31)
        {
            _bytes.Add((byte)(0xa0 | payload.Length));
        }
        else if (payload.Length <= byte.MaxValue)
        {
            _bytes.AddRange([0xd9, (byte)payload.Length]);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            _bytes.AddRange([0xda, (byte)(payload.Length >> 8), (byte)payload.Length]);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(value), "test strings stay under 64 KiB");
        }

        _bytes.AddRange(payload);
        return this;
    }

    /// <summary>fixarray 头（0x90–0x9f），count 必须 0–15。</summary>
    public TestMsgPackWriter FixArray(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 15);
        _bytes.Add((byte)(0x90 | count));
        return this;
    }

    /// <summary>array16 头（0xdc），count 必须 0–65535。</summary>
    public TestMsgPackWriter Array16(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, ushort.MaxValue);
        return Bytes(0xdc, (byte)(count >> 8), (byte)count);
    }

    /// <summary>array32 头（0xdd），不校验 value 能否被实际数据满足（截断/溢出测试需要）。</summary>
    public TestMsgPackWriter Array32(uint count) =>
        Bytes(0xdd, (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count);

    /// <summary>按 count 选择最短的 array 头。</summary>
    public TestMsgPackWriter ArrayHeader(int count) =>
        count <= 15 ? FixArray(count) : count <= ushort.MaxValue ? Array16(count) : Array32((uint)count);

    /// <summary>fixmap 头（0x80–0x8f），count 必须 0–15。</summary>
    public TestMsgPackWriter FixMap(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 15);
        _bytes.Add((byte)(0x80 | count));
        return this;
    }

    /// <summary>map16 头（0xde），count 必须 0–65535。</summary>
    public TestMsgPackWriter Map16(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, ushort.MaxValue);
        return Bytes(0xde, (byte)(count >> 8), (byte)count);
    }

    /// <summary>map32 头（0xdf），不校验 value 能否被实际数据满足（截断/溢出测试需要）。</summary>
    public TestMsgPackWriter Map32(uint count) =>
        Bytes(0xdf, (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count);

    /// <summary>按 count 选择最短的 map 头。</summary>
    public TestMsgPackWriter MapHeader(int count) =>
        count <= 15 ? FixMap(count) : count <= ushort.MaxValue ? Map16(count) : Map32((uint)count);
}
