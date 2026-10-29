using System.Reflection;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// TileType/Tile 的相等性契约：只比 Id；Suit/Rank/Type 不参与；相等性与 GetHashCode 一致，
/// 可直接作为 Dictionary/HashSet 键。
/// </summary>
public sealed class TileEqualityTests
{
    [Fact]
    public void TileType_EqualsComparesIdOnly()
    {
        var left = new TileType(7, SuitType.Character, 1);
        var right = new TileType(7, SuitType.Bamboo, 9);

        Assert.True(left == right);
        Assert.False(left != right);
        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void TileType_EqualsDistinguishesDifferentIds()
    {
        var left = new TileType(7, SuitType.Character, 1);
        var right = new TileType(8, SuitType.Character, 1);

        Assert.False(left == right);
        Assert.True(left != right);
        Assert.False(left.Equals(right));
        Assert.False(left.Equals((object)right));
    }

    [Fact]
    public void TileType_EqualsObjectNullIsFalse()
    {
        var value = new TileType(7, SuitType.Character, 1);
        Assert.False(value.Equals(null));
    }

    [Fact]
    public void Tile_EqualsComparesIdOnly()
    {
        Tile left = RulesTestData.FromId(57);
        var right = new Tile(57, RulesTestData.TypeOf(0));

        Assert.NotEqual(left.Type, right.Type);
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Tile_EqualsDistinguishesDifferentIds()
    {
        Tile left = RulesTestData.Physical(5, 0);
        Tile right = RulesTestData.Physical(5, 1);

        Assert.False(left == right);
        Assert.True(left != right);
        Assert.False(left.Equals(right));
    }

    [Fact]
    public void TileType_UsableAsDictionaryKeyByIdOnly()
    {
        var dictionary = new Dictionary<TileType, int> { [new TileType(7, SuitType.Character, 1)] = 1 };
        dictionary[new TileType(7, SuitType.Wind, 4)] = 2;

        Assert.Single(dictionary);
        Assert.Equal(2, dictionary[new TileType(7, SuitType.Dragon, 3)]);
    }

    [Fact]
    public void Tile_UsableAsHashSetMemberByIdOnly()
    {
        var set = new HashSet<Tile> { RulesTestData.FromId(57), new Tile(57, RulesTestData.TypeOf(0)) };

        Assert.Single(set);
        Assert.Contains(RulesTestData.FromId(57), set);
        Assert.Contains(new Tile(57, RulesTestData.TypeOf(33)), set);
    }

    [Fact]
    public void Tiles_WithDifferentIdsStayDistinctInSets()
    {
        var set = new HashSet<Tile> { RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1) };

        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void TileTypeAndTile_AreValueTypesImplementingEquatable()
    {
        Assert.True(typeof(TileType).IsValueType);
        Assert.True(typeof(Tile).IsValueType);
        Assert.Contains(typeof(IEquatable<TileType>), typeof(TileType).GetInterfaces());
        Assert.Contains(typeof(IEquatable<Tile>), typeof(Tile).GetInterfaces());
    }

    [Fact]
    public void TileTypeAndTile_DeclareTheirOwnEqualityOperators()
    {
        AssertDeclaresEqualityOperators(typeof(TileType));
        AssertDeclaresEqualityOperators(typeof(Tile));
    }

    [Fact]
    public void TileTypeAndTile_DoNotOverrideToString()
    {
        Assert.Same(typeof(ValueType), typeof(TileType).GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType);
        Assert.Same(typeof(ValueType), typeof(Tile).GetMethod(nameof(ToString), Type.EmptyTypes)!.DeclaringType);
    }

    [Fact]
    public void Tile_UnknownIdIsZero()
    {
        Assert.Equal(0, (int)Tile.UnknownId);
        FieldInfo field = typeof(Tile).GetField(nameof(Tile.UnknownId), BindingFlags.Public | BindingFlags.Static)!;
        Assert.True(field.IsLiteral);
        Assert.Same(typeof(ushort), field.FieldType);
    }

    private static void AssertDeclaresEqualityOperators(Type type)
    {
        MethodInfo? equality = type.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static);
        MethodInfo? inequality = type.GetMethod("op_Inequality", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(equality);
        Assert.NotNull(inequality);
        Assert.Same(type, equality!.DeclaringType);
        Assert.Same(type, inequality!.DeclaringType);
    }
}
