// T-4 金标字面量：由按规格独立实现的 Python 脚本推导（未入库）；测试只做字面量比对。
namespace Mahjong.Tests.Rules;

/// <summary>
/// T-4 金标向量：PCG32 参考序列与发牌全槽位。
/// 数值由按规格 §3.1 与 contract 的 PCG32 定义独立实现的 Python 脚本推导，测试只做字面量比对。
/// </summary>
public static class RulesGoldenVectors
{
    /// <summary>Pcg32(seed: 42, stream: 54) 的前 8 个 NextUInt()。</summary>
    public static readonly uint[] Pcg32Seed42Stream54 = [2707161783u, 2068313097u, 3122475824u, 2211639955u, 3215226955u, 3421331566u, 3217466285u, 2167406445u];

    /// <summary>Pcg32(seed: 0, stream: 1) 的前 4 个 NextUInt()。</summary>
    public static readonly uint[] Pcg32Seed0Stream1 = [257813417u, 407017181u, 896943092u, 4175973425u];

    /// <summary>Pcg32(seed: 42, stream: 1) 的前 4 个 NextUInt()。</summary>
    public static readonly uint[] Pcg32Seed42Stream1 = [1307692281u, 3850602322u, 1491967504u, 4091771729u];

    /// <summary>Deal(seed: 42, stream: 1, bankerSeat: 0) 后 Wall 剩余 69 槽，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed42Banker0Wall =
    [
        44, 104, 91, 129, 131, 4, 42, 96, 64, 99, 134, 90,
        102, 57, 112, 33, 2, 122, 49, 82, 114, 3, 47, 89,
        25, 48, 62, 72, 125, 39, 53, 86, 111, 85, 120, 9,
        132, 59, 45, 105, 46, 51, 110, 35, 36, 41, 21, 71,
        14, 118, 7, 16, 76, 101, 95, 54, 126, 50, 94, 69,
        28, 40, 84, 107, 32, 63, 98, 74, 55,
    ];

    /// <summary>Deal(seed: 42, stream: 1, bankerSeat: 0) 的 WallTail 14 槽，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed42Banker0WallTail =
    [
        56, 124, 81, 19, 65, 88, 61, 31, 78, 37, 130, 115,
        23, 58,
    ];

    /// <summary>Deal(seed: 42, stream: 1, bankerSeat: 0) 后座位 0 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed42Banker0Hand0 =
    [
        75, 119, 73, 92, 15, 29, 52, 83, 109, 123, 60, 127,
        26, 11,
    ];

    /// <summary>Deal(seed: 42, stream: 1, bankerSeat: 0) 后座位 1 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed42Banker0Hand1 =
    [
        10, 135, 136, 87, 103, 17, 8, 121, 93, 6, 80, 5,
        38,
    ];

    /// <summary>Deal(seed: 42, stream: 1, bankerSeat: 0) 后座位 2 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed42Banker0Hand2 =
    [
        79, 66, 1, 43, 117, 128, 100, 70, 20, 106, 108, 133,
        30,
    ];

    /// <summary>Deal(seed: 42, stream: 1, bankerSeat: 0) 后座位 3 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed42Banker0Hand3 =
    [
        18, 24, 68, 27, 77, 67, 12, 116, 34, 97, 13, 113,
        22,
    ];

    /// <summary>Deal(seed: 20261029, stream: 1, bankerSeat: 2) 后 Wall 剩余 69 槽，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed20261029Banker2Wall =
    [
        1, 113, 12, 53, 114, 9, 118, 20, 89, 107, 55, 45,
        121, 72, 32, 19, 110, 58, 124, 111, 96, 103, 37, 52,
        73, 25, 91, 133, 47, 75, 65, 29, 127, 93, 2, 64,
        126, 122, 132, 131, 16, 85, 22, 21, 4, 40, 44, 66,
        86, 104, 108, 7, 99, 70, 60, 33, 67, 134, 34, 56,
        123, 83, 62, 106, 13, 69, 8, 120, 100,
    ];

    /// <summary>Deal(seed: 20261029, stream: 1, bankerSeat: 2) 的 WallTail 14 槽，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed20261029Banker2WallTail =
    [
        130, 88, 71, 61, 35, 102, 77, 49, 68, 15, 3, 94,
        46, 80,
    ];

    /// <summary>Deal(seed: 20261029, stream: 1, bankerSeat: 2) 后座位 0 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed20261029Banker2Hand0 =
    [
        136, 125, 41, 54, 87, 23, 128, 84, 78, 92, 18, 101,
        28,
    ];

    /// <summary>Deal(seed: 20261029, stream: 1, bankerSeat: 2) 后座位 1 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed20261029Banker2Hand1 =
    [
        43, 129, 119, 112, 24, 109, 11, 90, 38, 31, 42, 76,
        97,
    ];

    /// <summary>Deal(seed: 20261029, stream: 1, bankerSeat: 2) 后座位 2 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed20261029Banker2Hand2 =
    [
        117, 30, 39, 81, 5, 74, 59, 82, 27, 26, 17, 116,
        95, 51,
    ];

    /// <summary>Deal(seed: 20261029, stream: 1, bankerSeat: 2) 后座位 3 的手牌，按槽位顺序。</summary>
    public static readonly ushort[] DealSeed20261029Banker2Hand3 =
    [
        57, 135, 98, 6, 36, 48, 105, 10, 50, 79, 14, 63,
        115,
    ];
}
