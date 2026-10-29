using System.Reflection;
using System.Runtime.CompilerServices;
using Mahjong.Core;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 模型/流程层静态状态守卫：<c>Mahjong.Core</c> 与 <c>Mahjong.Session</c> 两个程序集都不得声明可变静态字段；
/// 静态 readonly 的常用可变集合也判违规；数组、常量与其他 readonly 值允许；只按编译器命名（以 <c>&lt;</c> 开头）
/// 排除编译器生成类型（<c>&lt;&gt;c</c> 等 lambda 缓存不是手写模型状态），并断言保留的非编译器命名类型不带
/// [CompilerGenerated]，防止手写类型标注绕过；扫描逻辑用合成类型自检，Session 扫描路径再注入合成类型，
/// 证明「空集无违规」不是恒真断言。
/// </summary>
public sealed class StaticStateGuardTests
{
    private const BindingFlags StaticFieldFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] MutableCollectionDefinitions =
    [
        typeof(List<>),
        typeof(Dictionary<,>),
        typeof(HashSet<>),
        typeof(Queue<>),
        typeof(Stack<>),
    ];

    [Fact]
    public void CoreAssembly_DeclaresNoMutableStaticFields()
    {
        Assembly coreAssembly = typeof(CoreAssembly).Assembly;
        Type[] types = GetHandwrittenTypes(coreAssembly);
        Assert.NotEmpty(types);
        Assert.Contains(typeof(TileZone), types);

        // C# 源码不能声明以 '<' 开头的类型名，因此保留的类型都是手写类型：它们不得带 [CompilerGenerated]。
        Assert.DoesNotContain(types, type => type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false));

        Assert.Empty(FindMutableStaticFieldNames(coreAssembly));
    }

    [Fact]
    public void SessionAssembly_DeclaresNoMutableStaticFields()
    {
        Assembly sessionAssembly = Assembly.Load("Mahjong.Session");
        Assert.Equal("Mahjong.Session", sessionAssembly.GetName().Name);
        Assert.DoesNotContain(
            GetHandwrittenTypes(sessionAssembly),
            type => type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false));

        // Mahjong.Session 目前没有手写类型，只断言「空集无违规」会恒真（G8 weak）。这里把合成类型注入
        // 同一条程序集扫描路径（GetHandwrittenTypes + FindMutableStaticFields），先证明该路径对 static
        // 可变字段与 static readonly 可变集合都会命中，再运行真实的 Session 扫描。
        string[] injected = FindMutableStaticFieldNames(sessionAssembly, typeof(SyntheticSessionStatics));
        Assert.Equal(
            new[]
            {
                "SyntheticSessionStatics.MutableCounter",
                "SyntheticSessionStatics.SessionCache",
            },
            injected);

        Assert.Empty(FindMutableStaticFieldNames(sessionAssembly));
    }

    [Fact]
    public void Scanner_DetectsSyntheticMutableStatics()
    {
        string[] offenders = FindMutableStaticFields([typeof(SyntheticStatics)])
            .Select(item => item.Field)
            .Order()
            .ToArray();

        Assert.Equal(new[] { "Cache", "Counter" }, offenders);
    }

    [Fact]
    public void Scanner_AllowsArraysConstsAndOtherReadonlyValues()
    {
        (Type Type, string Field)[] offenders = FindMutableStaticFields([typeof(SyntheticStatics)]);

        Assert.DoesNotContain(offenders, item => item.Field.StartsWith("Allowed", StringComparison.Ordinal));
    }

    private static Type[] GetHandwrittenTypes(Assembly assembly) =>
        assembly.GetTypes().Where(type => !HasCompilerGeneratedName(type)).ToArray();

    /// <summary>按程序集扫描可变静态字段名；可注入额外合成类型用于自检同一路径。</summary>
    private static string[] FindMutableStaticFieldNames(Assembly assembly, params Type[] injectedTypes)
    {
        Type[] types = [.. GetHandwrittenTypes(assembly), .. injectedTypes];
        return FindMutableStaticFields(types)
            .Select(item => item.Type.Name + "." + item.Field)
            .Order()
            .ToArray();
    }

    private static (Type Type, string Field)[] FindMutableStaticFields(IEnumerable<Type> types)
    {
        var offenders = new List<(Type Type, string Field)>();

        foreach (Type type in types)
        {
            if (HasCompilerGeneratedName(type))
            {
                continue;
            }

            foreach (FieldInfo field in type.GetFields(StaticFieldFlags))
            {
                if (field.IsLiteral)
                {
                    continue;
                }

                if (!field.IsInitOnly)
                {
                    offenders.Add((type, field.Name));
                    continue;
                }

                Type fieldType = field.FieldType;
                if (fieldType.IsArray)
                {
                    continue;
                }

                Type definition = fieldType.IsGenericType ? fieldType.GetGenericTypeDefinition() : fieldType;
                if (MutableCollectionDefinitions.Contains(definition))
                {
                    offenders.Add((type, field.Name));
                }
            }
        }

        return offenders.ToArray();
    }

    // 只按编译器命名排除：C# 源码无法声明以 '<' 开头的类型名（<>c、<PrivateImplementationDetails> 等），
    // 因此不会漏掉也不会误放行手写类型；按属性的排除可被手写类型人工标注绕过。
    private static bool HasCompilerGeneratedName(Type type) => type.Name.StartsWith('<');

    private static class SyntheticSessionStatics
    {
        public static long MutableCounter = 0;

        public static readonly List<int> SessionCache = [];
    }

    private static class SyntheticStatics
    {
        public static int Counter = 1;

        public static readonly List<int> Cache = [];

        public static readonly int[] AllowedArray = [1, 2, 3];

        public const int AllowedConst = 1;

        public static readonly TileType AllowedValue = default;
    }
}
