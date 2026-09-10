using System.Reflection;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class SpineLocalTarget {
    public static int CountOnly(List<int> items) {
        int c = items.Count;
        return c;
    }

    public static string CountViaLocalAddress(List<int> items) {
        int c = items.Count;
        return c.ToString();
    }

    public static string CountWithBranch(List<int> items) {
        return items == null ? "<null>" : items.Count.ToString();
    }

    public static string FiveArgs(Type type, string node, bool flag, List<int> items, object parent) {
        return type.Name + "|" + node + "|" + flag + "|" + (items == null ? "<null>" : items.Count.ToString()) + "|" +
               (parent == null ? "<null>" : "set");
    }
}

public static class SpineLocalInjectionMethods {
    public static int WrapCountOnly(List<int> items, Operation<List<int>, int> original) {
        return original.Invoke(items);
    }

    public static string WrapCountViaLocalAddress(List<int> items, Operation<List<int>, string> original) {
        return original.Invoke(items);
    }

    public static string WrapCountWithBranch(List<int> items, Operation<List<int>, string> original) {
        return original.Invoke(items);
    }

    public static string WrapFiveArgs(
        Type type,
        string node,
        bool flag,
        List<int> items,
        object parent,
        Operation<Type, string, bool, List<int>, object, string> original) {
        return original.Invoke(type, node, flag, items, parent);
    }
}

/// <summary>
///     A spine that stores to a short-form local (<c>stloc.0</c>) then reads it by address
///     (<c>ldloca.s</c>, i.e. any <c>someInt.ToString()</c>) used to read an unmapped wrapper local.
/// </summary>
public sealed class AroundSpineLocalTests {
    [Fact]
    public void Around_PlainLocal_RoundTrips() {
        Assert.Equal(2, Run(nameof(SpineLocalTarget.CountOnly), nameof(SpineLocalInjectionMethods.WrapCountOnly), [new List<int> { 1, 2 }]));
    }

    [Fact]
    public void Around_LocalReadByAddress_RoundTrips() {
        Assert.Equal(
            "2",
            Run(nameof(SpineLocalTarget.CountViaLocalAddress), nameof(SpineLocalInjectionMethods.WrapCountViaLocalAddress), [new List<int> { 1, 2 }]));
    }

    [Fact]
    public void Around_BranchThenLocalReadByAddress_RoundTrips() {
        Assert.Equal(
            "2",
            Run(nameof(SpineLocalTarget.CountWithBranch), nameof(SpineLocalInjectionMethods.WrapCountWithBranch), [new List<int> { 1, 2 }]));
    }

    [Fact]
    public void Around_FiveArgsWithLocalReadByAddress_RoundTrips() {
        object[] args = [typeof(string), "node", true, new List<int> { 1, 2 }, new object()];
        Assert.Equal("String|node|True|2|set", Run(nameof(SpineLocalTarget.FiveArgs), nameof(SpineLocalInjectionMethods.WrapFiveArgs), args));
    }

    private static object Run(string targetName, string injectionName, object[] args) {
        MethodBase target = typeof(SpineLocalTarget).GetMethod(targetName)!;
        MethodBase injectionMethod = typeof(SpineLocalInjectionMethods).GetMethod(injectionName)!;
        ComposeResult result = WrapperComposer.Compose(target, [new Injection(injectionMethod, new InjectAt.Around(), "test", 0)]);
        return result.Wrapper.Invoke(null, args)!;
    }
}
