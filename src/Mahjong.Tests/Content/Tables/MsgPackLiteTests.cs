using Mahjong.Core.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Content.Tables;

/// <summary>
/// MsgPackLite 的子集边界：支持的类型码逐种解码、拒绝的类型码逐种报错、截断/头溢出/尾随字节。
/// 输入全部是手写字节，不经过查表加载。
/// </summary>
public sealed class MsgPackLiteTests
{
    private const string Label = "content/tables/dictionary_normal.bytes";

    [Fact]
    public void ReadUInt64_AcceptsEveryIntegerWidth()
    {
        AssertUInt64([0x00], 0);
        AssertUInt64([0x7f], 127);
        AssertUInt64([0xcc, 0x80], 128);
        AssertUInt64([0xcc, 0xff], 255);
        AssertUInt64([0xcd, 0x01, 0x00], 256);
        AssertUInt64([0xcd, 0xff, 0xff], 65535);
        AssertUInt64([0xce, 0x00, 0x01, 0x00, 0x00], 65536);
        AssertUInt64([0xce, 0xff, 0xff, 0xff, 0xff], uint.MaxValue);
        AssertUInt64([0xcf, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00], 4294967296);
        AssertUInt64([0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff], ulong.MaxValue);
    }

    [Fact]
    public void ReadInt32_AcceptsNonNegativeValuesUpToInt32Max()
    {
        Assert.Equal(0, Reader([0x00]).ReadInt32());
        Assert.Equal(4, Reader([0x04]).ReadInt32());
        Assert.Equal(127, Reader([0x7f]).ReadInt32());
        Assert.Equal(65535, Reader([0xcd, 0xff, 0xff]).ReadInt32());
        Assert.Equal(int.MaxValue, Reader([0xce, 0x7f, 0xff, 0xff, 0xff]).ReadInt32());
    }

    [Fact]
    public void ReadInt32_RejectsValuesAboveInt32Range()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => Reader([0xcf, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00]).ReadInt32());

        Assert.Contains("Int32", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Label, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadBool_AcceptsBothLiterals()
    {
        MsgPackLite reader = Reader(0xc2);
        Assert.False(reader.ReadBool());
        Assert.True(reader.IsAtEnd);

        Assert.True(Reader(0xc3).ReadBool());
    }

    [Fact]
    public void ReadDouble_AcceptsFloat64()
    {
        Assert.Equal(0.0, Reader([0xcb, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]).ReadDouble());
        Assert.Equal(1.5, Reader([0xcb, 0x3f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]).ReadDouble());
        Assert.Equal(
            double.MaxValue,
            Reader([0xcb, 0x7f, 0xef, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]).ReadDouble());
        Assert.True(double.IsNaN(
            Reader([0xcb, 0x7f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]).ReadDouble()));
        Assert.True(double.IsNegative(
            Reader([0xcb, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]).ReadDouble()));
    }

    [Fact]
    public void ReadArrayHeader_AcceptsFixArrayBoundaries()
    {
        Assert.Equal(0, Reader([0x90]).ReadArrayHeader());
        Assert.Equal(15, Reader([0x9f, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]).ReadArrayHeader());
    }

    [Fact]
    public void ReadArrayHeader_AcceptsArray16AndArray32()
    {
        Assert.Equal(16, Reader([0xdc, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]).ReadArrayHeader());
        Assert.Equal(3, Reader([0xdd, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00]).ReadArrayHeader());
        Assert.Equal(65535, Reader(Array32Header(65535, 65535)).ReadArrayHeader());
    }

    [Fact]
    public void ReadMapHeader_AcceptsFixMapAndMap16AndMap32()
    {
        Assert.Equal(0, Reader([0x80]).ReadMapHeader());
        Assert.Equal(15, Reader(FixMapHeaderWithBody(15)).ReadMapHeader());
        Assert.Equal(2, Reader([0xde, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00]).ReadMapHeader());
        Assert.Equal(2, Reader([0xdf, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00]).ReadMapHeader());
    }

    [Fact]
    public void ReadUInt64_RejectsEveryUnsupportedTypeCode()
    {
        byte[] rejected =
        [
            0xc0, 0xc1, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca,
            0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8,
            0xd9, 0xda, 0xdb, 0xe0, 0xf0, 0xff,
        ];

        foreach (byte code in rejected)
        {
            AssertUnsupportedTypeCode(payload: [code, 0, 0, 0, 0, 0, 0, 0, 0], reader => reader.ReadUInt64(), code);
        }
    }

    [Fact]
    public void ReadBool_RejectsNonBoolCodes()
    {
        byte[] rejected = [0x00, 0x7f, 0xc0, 0xc1, 0xcb, 0xcc, 0xd9, 0xe0];

        foreach (byte code in rejected)
        {
            AssertUnsupportedTypeCode([code, 0, 0, 0, 0, 0, 0, 0, 0], reader => reader.ReadBool(), code);
        }
    }

    [Fact]
    public void ReadDouble_RejectsFloat32AndOtherCodes()
    {
        byte[] rejected = [0x00, 0xc0, 0xc2, 0xc3, 0xca, 0xcc, 0xce, 0xd9, 0xe0];

        foreach (byte code in rejected)
        {
            AssertUnsupportedTypeCode([code, 0, 0, 0, 0, 0, 0, 0, 0], reader => reader.ReadDouble(), code);
        }
    }

    [Fact]
    public void ReadArrayHeader_RejectsNonArrayCodes()
    {
        byte[] rejected = [0x00, 0x7f, 0x80, 0xc0, 0xc2, 0xca, 0xcb, 0xcc, 0xce, 0xd9, 0xde, 0xdf, 0xe0];

        foreach (byte code in rejected)
        {
            AssertUnsupportedTypeCode([code, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], reader => reader.ReadArrayHeader(), code);
        }
    }

    [Fact]
    public void ReadMapHeader_RejectsNonMapCodes()
    {
        byte[] rejected = [0x00, 0x7f, 0x90, 0xc0, 0xc2, 0xca, 0xcb, 0xcc, 0xce, 0xd9, 0xdc, 0xdd, 0xe0];

        foreach (byte code in rejected)
        {
            AssertUnsupportedTypeCode([code, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], reader => reader.ReadMapHeader(), code);
        }
    }

    [Fact]
    public void ReadUInt64_RejectsTruncatedIntegerPayloads()
    {
        byte[][] truncated =
        [
            [0xcc],
            [0xcd, 0x01],
            [0xce, 0x00, 0x00, 0x01],
            [0xcf, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        ];

        foreach (byte[] payload in truncated)
        {
            AssertTruncated(payload, reader => reader.ReadUInt64());
        }
    }

    [Fact]
    public void ReadUInt64_RejectsEmptyPayload()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Reader().ReadUInt64());
        Assert.Contains("unexpected end of data", exception.Message, StringComparison.Ordinal);
        Assert.Contains("offset 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadDouble_RejectsTruncatedFloat64Payload()
    {
        AssertTruncated([0xcb, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], reader => reader.ReadDouble());
    }

    [Fact]
    public void ReadArrayHeader_RejectsTruncatedHeaders()
    {
        AssertTruncated([0xdc, 0x00], reader => reader.ReadArrayHeader());
        AssertTruncated([0xdd, 0x00, 0x00, 0x00], reader => reader.ReadArrayHeader());
    }

    [Fact]
    public void ReadMapHeader_RejectsTruncatedHeaders()
    {
        AssertTruncated([0xde, 0x00], reader => reader.ReadMapHeader());
        AssertTruncated([0xdf, 0x00, 0x00, 0x00], reader => reader.ReadMapHeader());
    }

    [Fact]
    public void Headers_RejectDeclaredCountsLargerThanRemainingBytes()
    {
        // array32 声明 4294967295 个元素，但没有剩余字节。
        AssertHeaderOverflow([0xdd, 0xff, 0xff, 0xff, 0xff], reader => reader.ReadArrayHeader());
        // array32 声明 1M 个元素，只有 5 字节。
        AssertHeaderOverflow([0xdd, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], reader => reader.ReadArrayHeader());
        // array16 声明 16 个元素，只有 2 字节。
        AssertHeaderOverflow([0xdc, 0x00, 0x10, 0x00, 0x00], reader => reader.ReadArrayHeader());
        // map32 声明 4294967295 个键值对，没有剩余字节。
        AssertHeaderOverflow([0xdf, 0xff, 0xff, 0xff, 0xff], reader => reader.ReadMapHeader());
        // map16 声明 16 个键值对（需 32 字节），只有 6 字节。
        AssertHeaderOverflow([0xde, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], reader => reader.ReadMapHeader());
        // fixmap 15 需 30 字节，只有 0 字节。
        AssertHeaderOverflow([0x8f], reader => reader.ReadMapHeader());
    }

    [Fact]
    public void Headers_AcceptDeclaredCountsWithExactlyEnoughBytes()
    {
        Assert.Equal(3, Reader([0xdd, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00]).ReadArrayHeader());
        Assert.Equal(2, Reader([0xdf, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00]).ReadMapHeader());
        Assert.Equal(1, Reader([0xdc, 0x00, 0x01, 0x00]).ReadArrayHeader());
    }

    [Fact]
    public void EnsureEnd_RejectsTrailingBytes()
    {
        MsgPackLite reader = Reader(0x01, 0x02);
        Assert.Equal(1UL, reader.ReadUInt64());
        Assert.False(reader.IsAtEnd);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => reader.EnsureEnd());

        Assert.Contains("trailing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("offset 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureEnd_AcceptsFullyConsumedDocument()
    {
        MsgPackLite reader = Reader(0x01);
        Assert.Equal(1UL, reader.ReadUInt64());
        Assert.True(reader.IsAtEnd);
        reader.EnsureEnd();
    }

    [Fact]
    public void OffsetAndIsAtEnd_TrackConsumedBytes()
    {
        MsgPackLite reader = Reader(0xcd, 0x01, 0x2c, 0xc3);
        Assert.Equal(0, reader.Offset);
        Assert.Equal(300UL, reader.ReadUInt64());
        Assert.Equal(3, reader.Offset);
        Assert.True(reader.ReadBool());
        Assert.Equal(4, reader.Offset);
        Assert.True(reader.IsAtEnd);
    }

    [Fact]
    public void Reader_RespectsPayloadSlice()
    {
        ReadOnlyMemory<byte> slice = new byte[] { 0xff, 0xff, 0x7b }.AsMemory(2);
        MsgPackLite reader = new(slice);
        Assert.Equal(123UL, reader.ReadUInt64());
        Assert.True(reader.IsAtEnd);
    }

    [Fact]
    public void Errors_ArePrefixedWithLabel()
    {
        MsgPackLite reader = new(new byte[] { 0x00 }, Label);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => reader.ReadBool());
        Assert.StartsWith(Label + ":", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertTruncated(byte[] payload, Action<MsgPackLite> read)
    {
        MsgPackLite reader = Reader(payload);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => read(reader));
        Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
        Assert.Contains("offset", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Label, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertUInt64(byte[] payload, ulong expected)
    {
        MsgPackLite reader = Reader(payload);
        Assert.Equal(expected, reader.ReadUInt64());
        Assert.True(reader.IsAtEnd);
        Assert.Equal(payload.Length, reader.Offset);
    }

    private static void AssertUnsupportedTypeCode(byte[] payload, Action<MsgPackLite> read, byte code)
    {
        MsgPackLite reader = Reader(payload);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => read(reader));
        Assert.Contains("unsupported MessagePack type code", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"0x{code:X2}", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Label, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertHeaderOverflow(byte[] payload, Action<MsgPackLite> read)
    {
        MsgPackLite reader = Reader(payload);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => read(reader));
        Assert.Contains("declares", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Label, exception.Message, StringComparison.Ordinal);
    }

    private static byte[] Array32Header(int count, int bodyBytes)
    {
        byte[] payload = new byte[5 + bodyBytes];
        payload[0] = 0xdd;
        payload[1] = (byte)(count >> 24);
        payload[2] = (byte)(count >> 16);
        payload[3] = (byte)(count >> 8);
        payload[4] = (byte)count;
        return payload;
    }

    private static byte[] FixMapHeaderWithBody(int count)
    {
        byte[] payload = new byte[1 + (count * 2)];
        payload[0] = (byte)(0x80 + count);
        return payload;
    }

    private static MsgPackLite Reader(params byte[] payload) => new(payload, Label);
}