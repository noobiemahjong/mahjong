using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Content.Tables;

/// <summary>
/// TableCatalog.Load 的结构校验与查询语义。全部用系统临时目录里的小型合成表，
/// 真实 15 MB 表只由 RealTableCatalogTests 做正例。
/// </summary>
public sealed class TableCatalogLoadTests
{
    [Fact]
    public void Load_RejectsNullDirectory()
    {
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() => { _ = TableCatalog.Load(null!); });
        Assert.Equal("tablesDirectory", exception.ParamName);
    }

    [Fact]
    public void Load_RejectsBlankDirectory()
    {
        foreach (string directory in new[] { "", " ", "\t" })
        {
            Assert.ThrowsAny<ArgumentException>(() => { _ = TableCatalog.Load(directory); });
        }
    }

    [Fact]
    public void Load_MissingDirectory_ReportsPath()
    {
        string missing = Path.Combine(Path.GetTempPath(), "mahjong-missing-" + Guid.NewGuid().ToString("N"));
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => { _ = TableCatalog.Load(missing); });
        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ParsesWinRowsInFileOrderWithAllFields()
    {
        Dictionary<string, byte[]> files = TableTestData.ValidFiles();
        files[TableTestData.NormalWin] = TableTestData.WinTable(
            (0, [
                TableTestData.WinRow(2, hasPair: true, hasWin: false, [0, 1, 1, 1, 1, 1, 1, 1, 1]),
                TableTestData.WinRow(3, hasPair: false, hasWin: true),
                TableTestData.WinRow(4, hasPair: true, hasWin: false, [1, 1, 1, 1, 1, 1, 1, 1, 1]),
            ]));
        using TempTableDirectory directory = TempTableDirectory.CreateWith(files);

        TableCatalog catalog = TableCatalog.Load(directory.Path);
        Assert.True(catalog.TryGetWinRows(SuitType.Character, 0, out IReadOnlyList<WinRow> rows));
        Assert.Equal(3, rows.Count);

        Assert.Equal(2, rows[0].NeedJoker);
        Assert.True(rows[0].HasPair);
        Assert.False(rows[0].HasWin);
        Assert.Equal(new[] { 0, 1, 1, 1, 1, 1, 1, 1, 1 }, rows[0].WinningRanks);

        Assert.Equal(3, rows[1].NeedJoker);
        Assert.False(rows[1].HasPair);
        Assert.True(rows[1].HasWin);
        Assert.Empty(rows[1].WinningRanks);

        Assert.Equal(4, rows[2].NeedJoker);
        Assert.True(rows[2].HasPair);
        Assert.False(rows[2].HasWin);
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 }, rows[2].WinningRanks);
    }

    [Fact]
    public void Load_ParsesAiRowsWithAllFields()
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        TableCatalog catalog = TableCatalog.Load(directory.Path);

        Assert.True(catalog.TryGetAiRows(SuitType.Wind, 0, out IReadOnlyList<AiRow> rows));
        Assert.Single(rows);
        Assert.False(rows[0].HasPair);
        Assert.Equal(0.5, rows[0].Probability);
    }

    [Fact]
    public void Load_MapsEachSuitToItsOwnTable()
    {
        Dictionary<string, byte[]> files = TableTestData.ValidFiles();
        files[TableTestData.NormalWin] = TableTestData.WinTable((0, [TableTestData.WinRow(0, hasPair: true, hasWin: true)]));
        files[TableTestData.WindWin] = TableTestData.WinTable((0, [TableTestData.WinRow(1, hasPair: false, hasWin: true)]));
        files[TableTestData.DragonWin] = TableTestData.WinTable((0, [TableTestData.WinRow(2, hasPair: true, hasWin: true)]));
        files[TableTestData.NormalAi] = TableTestData.AiTable((0, [TableTestData.AiRow(hasPair: true, 0.1)]));
        files[TableTestData.WindAi] = TableTestData.AiTable((0, [TableTestData.AiRow(hasPair: false, 0.2)]));
        files[TableTestData.DragonAi] = TableTestData.AiTable((0, [TableTestData.AiRow(hasPair: true, 0.3)]));
        using TempTableDirectory directory = TempTableDirectory.CreateWith(files);

        TableCatalog catalog = TableCatalog.Load(directory.Path);

        Assert.True(catalog.TryGetWinRows(SuitType.Character, 0, out IReadOnlyList<WinRow> characterRows));
        Assert.True(catalog.TryGetWinRows(SuitType.Dot, 0, out IReadOnlyList<WinRow> dotRows));
        Assert.True(catalog.TryGetWinRows(SuitType.Bamboo, 0, out IReadOnlyList<WinRow> bambooRows));
        Assert.True(catalog.TryGetWinRows(SuitType.Wind, 0, out IReadOnlyList<WinRow> windRows));
        Assert.True(catalog.TryGetWinRows(SuitType.Dragon, 0, out IReadOnlyList<WinRow> dragonRows));

        Assert.Same(characterRows, dotRows);
        Assert.Same(characterRows, bambooRows);
        Assert.NotSame(characterRows, windRows);
        Assert.NotSame(windRows, dragonRows);
        Assert.Equal(0, characterRows[0].NeedJoker);
        Assert.Equal(1, windRows[0].NeedJoker);
        Assert.Equal(2, dragonRows[0].NeedJoker);

        Assert.True(catalog.TryGetAiRows(SuitType.Character, 0, out IReadOnlyList<AiRow> normalAi));
        Assert.True(catalog.TryGetAiRows(SuitType.Wind, 0, out IReadOnlyList<AiRow> windAi));
        Assert.True(catalog.TryGetAiRows(SuitType.Dragon, 0, out IReadOnlyList<AiRow> dragonAi));
        Assert.Equal(0.1, normalAi[0].Probability);
        Assert.Equal(0.2, windAi[0].Probability);
        Assert.Equal(0.3, dragonAi[0].Probability);
    }

    [Fact]
    public void Load_AcceptsAlternateRootAndMapHeaders()
    {
        (byte[] RootHeader, byte[] MapHeader)[] variants =
        [
            ([0x91], [0x81]),
            ([0xdc, 0x00, 0x01], [0xde, 0x00, 0x01]),
            ([0xdd, 0x00, 0x00, 0x00, 0x01], [0xdf, 0x00, 0x00, 0x00, 0x01]),
        ];

        foreach ((byte[] rootHeader, byte[] mapHeader) in variants)
        {
            Dictionary<string, byte[]> files = TableTestData.ValidFiles();
            files[TableTestData.DragonWin] = new TableBytesWriter()
                .Raw(rootHeader)
                .Raw(mapHeader)
                .UInt(0)
                .ArrayHeader(1)
                .Raw(TableTestData.WinRow(0, hasPair: false, hasWin: true))
                .ToArray();
            using TempTableDirectory directory = TempTableDirectory.CreateWith(files);

            TableCatalog catalog = TableCatalog.Load(directory.Path);
            Assert.True(catalog.TryGetWinRows(SuitType.Dragon, 0, out IReadOnlyList<WinRow> rows));
            Assert.Single(rows);
        }
    }

    [Fact]
    public void Load_AcceptsUInt64EncodedKeys()
    {
        Dictionary<string, byte[]> files = TableTestData.ValidFiles();
        files[TableTestData.NormalWin] = new TableBytesWriter()
            .ArrayHeader(1)
            .MapHeader(1)
            .Raw(0xcf, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x2c)
            .ArrayHeader(1)
            .Raw(TableTestData.WinRow(0, hasPair: false, hasWin: true))
            .ToArray();
        using TempTableDirectory directory = TempTableDirectory.CreateWith(files);

        TableCatalog catalog = TableCatalog.Load(directory.Path);
        Assert.Equal(1, catalog.WinRowKeyCount(SuitType.Character));
        Assert.True(catalog.TryGetWinRows(SuitType.Character, 300, out IReadOnlyList<WinRow> rows));
        Assert.Single(rows);
        Assert.False(catalog.TryGetWinRows(SuitType.Character, 299, out _));
    }

    [Theory]
    [InlineData(TableTestData.NormalWin)]
    [InlineData(TableTestData.WindWin)]
    [InlineData(TableTestData.DragonWin)]
    [InlineData(TableTestData.NormalAi)]
    [InlineData(TableTestData.WindAi)]
    [InlineData(TableTestData.DragonAi)]
    public void Load_MissingFile_ReportsFilePath(string fileName)
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        directory.Delete(fileName);

        InvalidDataException exception = AssertTableLoadFails(directory);
        Assert.Contains(directory.FilePath(fileName), exception.Message, StringComparison.Ordinal);
        Assert.Contains("not found", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TableTestData.NormalWin)]
    [InlineData(TableTestData.WindWin)]
    [InlineData(TableTestData.DragonWin)]
    [InlineData(TableTestData.NormalAi)]
    [InlineData(TableTestData.WindAi)]
    [InlineData(TableTestData.DragonAi)]
    public void Load_TruncatedFile_ReportsFilePath(string fileName)
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        byte[] bytes = File.ReadAllBytes(directory.FilePath(fileName));
        directory.Write(fileName, bytes[..^1]);

        InvalidDataException exception = AssertTableLoadFails(directory);
        Assert.Contains(directory.FilePath(fileName), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TableTestData.NormalWin)]
    [InlineData(TableTestData.WindWin)]
    [InlineData(TableTestData.DragonWin)]
    [InlineData(TableTestData.NormalAi)]
    [InlineData(TableTestData.WindAi)]
    [InlineData(TableTestData.DragonAi)]
    public void Load_EmptyFile_ReportsFilePath(string fileName)
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        directory.Write(fileName, []);

        InvalidDataException exception = AssertTableLoadFails(directory);
        Assert.Contains(directory.FilePath(fileName), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TableTestData.NormalWin)]
    [InlineData(TableTestData.WindWin)]
    [InlineData(TableTestData.DragonWin)]
    [InlineData(TableTestData.NormalAi)]
    [InlineData(TableTestData.WindAi)]
    [InlineData(TableTestData.DragonAi)]
    public void Load_TrailingByteAfterDocument_ReportsFilePath(string fileName)
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        byte[] bytes = File.ReadAllBytes(directory.FilePath(fileName));
        directory.Write(fileName, [.. bytes, 0x00]);

        InvalidDataException exception = AssertTableLoadFails(directory);
        Assert.Contains(directory.FilePath(fileName), exception.Message, StringComparison.Ordinal);
        Assert.Contains("trailing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RootArrayWithTwoElements_Fails()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            new TableBytesWriter().Raw(0x92, 0x80, 0x80).ToArray());
        Assert.Contains("exactly 1 element", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RootElementNotMap_Fails()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            new TableBytesWriter().Raw(0x91, 0x00).ToArray());
        Assert.Contains("unsupported MessagePack type code 0x00", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Map32DeclaringHugeCount_FailsWithoutLooping()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            new TableBytesWriter().Raw(0x91, 0xdf, 0xff, 0xff, 0xff, 0xff).ToArray());
        Assert.Contains("declares 4294967295", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RowsArray32DeclaringHugeCount_FailsWithoutLooping()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            new TableBytesWriter()
                .ArrayHeader(1)
                .Raw(0xdf, 0x00, 0x00, 0x00, 0x01)
                .UInt(0)
                .Raw(0xdd, 0xff, 0xff, 0xff, 0xff)
                .ToArray());
        Assert.Contains("declares 4294967295", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_DuplicateKey_Fails()
    {
        byte[] row = TableTestData.WinRow(0, hasPair: false, hasWin: true);
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row]), (0, [row])));
        Assert.Contains("duplicate key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_EmptyRowList_Fails()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [])));
        Assert.Contains("empty row list", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_KeyEncodedAsString_Fails()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            new TableBytesWriter()
                .ArrayHeader(1)
                .MapHeader(1)
                .Raw(0xa1, 0x61)
                .ArrayHeader(1)
                .Raw(TableTestData.WinRow(0, hasPair: false, hasWin: true))
                .ToArray());
        Assert.Contains("unsupported MessagePack type code 0xA1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_KeyEncodedAsNegativeFixInt_Fails()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            new TableBytesWriter()
                .ArrayHeader(1)
                .MapHeader(1)
                .Raw(0xff)
                .ArrayHeader(1)
                .Raw(TableTestData.WinRow(0, hasPair: false, hasWin: true))
                .ToArray());
        Assert.Contains("unsupported MessagePack type code 0xFF", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RowElementNotArray_Fails()
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [[0x00]])));
        Assert.Contains("unsupported MessagePack type code 0x00", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Load_WinRowWithWrongFieldCount_Fails(int fieldCount)
    {
        TableBytesWriter row = new TableBytesWriter().ArrayHeader(fieldCount);
        for (int index = 0; index < fieldCount; index++)
        {
            row.UInt(0);
        }

        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row.ToArray()])));
        Assert.Contains("must have 4 fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WinRowWithNeedJokerAsBool_Fails()
    {
        byte[] row = new TableBytesWriter()
            .ArrayHeader(4)
            .Bool(true)
            .Bool(false)
            .Bool(true)
            .ArrayHeader(0)
            .ToArray();
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("unsupported MessagePack type code 0xC3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WinRowWithHasPairAsInteger_Fails()
    {
        byte[] row = new TableBytesWriter()
            .ArrayHeader(4)
            .UInt(0)
            .UInt(1)
            .Bool(true)
            .ArrayHeader(0)
            .ToArray();
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("unsupported MessagePack type code 0x01", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WinRowWithHasWinAsInteger_Fails()
    {
        byte[] row = new TableBytesWriter()
            .ArrayHeader(4)
            .UInt(0)
            .Bool(false)
            .UInt(1)
            .ArrayHeader(0)
            .ToArray();
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("unsupported MessagePack type code 0x01", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WinRowWithWinningRanksAsMap_Fails()
    {
        byte[] row = new TableBytesWriter()
            .ArrayHeader(4)
            .UInt(0)
            .Bool(false)
            .Bool(false)
            .Raw(0x80)
            .ToArray();
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("unsupported MessagePack type code 0x80", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NormalWinRowWithWrongWinningRankCount_Fails()
    {
        byte[] row = TableTestData.WinRow(0, hasPair: false, hasWin: false, [0, 1, 1, 1, 1, 1, 1, 1]);
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("must list 9 winning rank(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WindWinRowWithWrongWinningRankCount_Fails()
    {
        byte[] row = TableTestData.WinRow(0, hasPair: false, hasWin: false, [0, 1, 1, 1, 1, 1, 1, 1, 1]);
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.WindWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("must list 4 winning rank(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_DragonWinRowWithWrongWinningRankCount_Fails()
    {
        byte[] row = TableTestData.WinRow(0, hasPair: false, hasWin: false, [0, 1, 1, 1]);
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.DragonWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("must list 3 winning rank(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WinRowWithHasWinButNonEmptyRanks_Fails()
    {
        byte[] row = TableTestData.WinRow(0, hasPair: true, hasWin: true, [0, 1, 1, 1, 1, 1, 1, 1, 1]);
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("hasWin=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WinRowWithHasWinFalseAndEmptyRanks_Fails()
    {
        byte[] row = TableTestData.WinRow(0, hasPair: false, hasWin: false);
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("must list 9 winning rank(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WinRowWithWinningRankCountAboveFour_Fails()
    {
        byte[] row = TableTestData.WinRow(0, hasPair: false, hasWin: false, [0, 5, 0, 0, 0, 0, 0, 0, 0]);
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.NormalWin,
            TableTestData.WinTable((0, [row])));
        Assert.Contains("at most 4 copies", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Load_AiRowWithWrongFieldCount_Fails(int fieldCount)
    {
        TableBytesWriter row = new TableBytesWriter().ArrayHeader(fieldCount);
        for (int index = 0; index < fieldCount; index++)
        {
            row.Bool(true);
        }

        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.WindAi,
            TableTestData.AiTable((0, [row.ToArray()])));
        Assert.Contains("must have 2 fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_AiRowWithHasPairAsInteger_Fails()
    {
        byte[] row = new TableBytesWriter().ArrayHeader(2).UInt(1).Float64(0.5).ToArray();
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.WindAi,
            TableTestData.AiTable((0, [row])));
        Assert.Contains("unsupported MessagePack type code 0x01", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_AiRowWithIntegerProbability_Fails()
    {
        byte[] row = new TableBytesWriter().ArrayHeader(2).Bool(true).UInt(1).ToArray();
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.WindAi,
            TableTestData.AiTable((0, [row])));
        Assert.Contains("unsupported MessagePack type code 0x01", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_AiRowWithFloat32Probability_Fails()
    {
        byte[] row = new TableBytesWriter()
            .ArrayHeader(2)
            .Bool(true)
            .Raw(0xca, 0x3f, 0x00, 0x00, 0x00)
            .ToArray();
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.WindAi,
            TableTestData.AiTable((0, [row])));
        Assert.Contains("unsupported MessagePack type code 0xCA", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Load_AiRowWithNonFiniteProbability_Fails(double probability)
    {
        InvalidDataException exception = AssertRejectsSingleFile(
            TableTestData.WindAi,
            TableTestData.AiTable((0, [TableTestData.AiRow(hasPair: true, probability)])));
        Assert.Contains("non-finite probability", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_CountsKeysPerTable()
    {
        Dictionary<string, byte[]> files = TableTestData.ValidFiles();
        files[TableTestData.NormalWin] = TableTestData.WinTable(
            (0, [TableTestData.WinRow(0, hasPair: false, hasWin: true)]),
            (1, [TableTestData.WinRow(0, hasPair: false, hasWin: true)]),
            (2, [TableTestData.WinRow(0, hasPair: false, hasWin: true)]));
        files[TableTestData.NormalAi] = TableTestData.AiTable(
            (0, [TableTestData.AiRow(hasPair: true, 0.1)]),
            (1, [TableTestData.AiRow(hasPair: true, 0.2)]));
        using TempTableDirectory directory = TempTableDirectory.CreateWith(files);

        TableCatalog catalog = TableCatalog.Load(directory.Path);
        Assert.Equal(3, catalog.WinRowKeyCount(SuitType.Character));
        Assert.Equal(3, catalog.WinRowKeyCount(SuitType.Dot));
        Assert.Equal(3, catalog.WinRowKeyCount(SuitType.Bamboo));
        Assert.Equal(1, catalog.WinRowKeyCount(SuitType.Wind));
        Assert.Equal(1, catalog.WinRowKeyCount(SuitType.Dragon));
        Assert.Equal(2, catalog.AiRowKeyCount(SuitType.Character));
        Assert.Equal(1, catalog.AiRowKeyCount(SuitType.Wind));
        Assert.Equal(1, catalog.AiRowKeyCount(SuitType.Dragon));
    }

    [Fact]
    public void TryGetWinRows_MissingKey_ReturnsFalseAndEmptyList()
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        TableCatalog catalog = TableCatalog.Load(directory.Path);

        Assert.False(catalog.TryGetWinRows(SuitType.Character, ulong.MaxValue, out IReadOnlyList<WinRow> rows));
        Assert.Same(Array.Empty<WinRow>(), rows);
    }

    [Fact]
    public void TryGetAiRows_MissingKey_ReturnsFalseAndEmptyList()
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        TableCatalog catalog = TableCatalog.Load(directory.Path);

        Assert.False(catalog.TryGetAiRows(SuitType.Dragon, ulong.MaxValue, out IReadOnlyList<AiRow> rows));
        Assert.Same(Array.Empty<AiRow>(), rows);
    }

    [Fact]
    public void TryGet_UnmappedSuit_ReturnsFalseAndEmptyList()
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        TableCatalog catalog = TableCatalog.Load(directory.Path);

        foreach (SuitType suit in new[] { (SuitType)0, (SuitType)6, (SuitType)255 })
        {
            Assert.False(catalog.TryGetWinRows(suit, 0, out IReadOnlyList<WinRow> winRows));
            Assert.Same(Array.Empty<WinRow>(), winRows);
            Assert.False(catalog.TryGetAiRows(suit, 0, out IReadOnlyList<AiRow> aiRows));
            Assert.Same(Array.Empty<AiRow>(), aiRows);
            Assert.Equal(0, catalog.WinRowKeyCount(suit));
            Assert.Equal(0, catalog.AiRowKeyCount(suit));
        }
    }

    [Fact]
    public void Queries_DoNotAllocate()
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        TableCatalog catalog = TableCatalog.Load(directory.Path);

        for (int warmup = 0; warmup < 100; warmup++)
        {
            _ = catalog.TryGetWinRows(SuitType.Character, 0, out _);
            _ = catalog.TryGetWinRows(SuitType.Wind, ulong.MaxValue, out _);
            _ = catalog.TryGetAiRows(SuitType.Dragon, 0, out _);
            _ = catalog.WinRowKeyCount(SuitType.Dragon);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            _ = catalog.TryGetWinRows(SuitType.Character, 0, out IReadOnlyList<WinRow> hit);
            _ = catalog.TryGetWinRows(SuitType.Wind, ulong.MaxValue, out IReadOnlyList<WinRow> miss);
            _ = catalog.TryGetAiRows(SuitType.Dragon, 0, out IReadOnlyList<AiRow> ai);
            _ = catalog.AiRowKeyCount(SuitType.Dragon);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    private static InvalidDataException AssertTableLoadFails(TempTableDirectory directory)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => { _ = TableCatalog.Load(directory.Path); });
        Assert.Contains(directory.Path, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    private static InvalidDataException AssertRejectsSingleFile(string fileName, byte[] bytes)
    {
        using TempTableDirectory directory = TempTableDirectory.CreateValid();
        directory.Write(fileName, bytes);
        InvalidDataException exception = AssertTableLoadFails(directory);
        Assert.Contains(directory.FilePath(fileName), exception.Message, StringComparison.Ordinal);
        return exception;
    }
}