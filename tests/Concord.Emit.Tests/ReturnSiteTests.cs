using System.Reflection;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class ReturnSiteTarget {
    public static int Pick(int which) {
        if (which == 0) {
            return 10;
        }

        if (which == 1) {
            return 20;
        }

        return 30;
    }
}

public static class ReturnSiteInjectionMethods {
    public static void Double(ControlHandle<int> ch) {
        int current = ch.ReturnValue;
        ch.ReturnValue = current * 2;
    }
}

public sealed class ReturnSiteTests {
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 40)]
    [InlineData(2, 60)]
    public void Return_EveryReturn_TransformsValueAtEachSite(int which, int expected) {
        MethodBase target = typeof(ReturnSiteTarget).GetMethod(nameof(ReturnSiteTarget.Pick))!;
        MethodBase injectionMethod = typeof(ReturnSiteInjectionMethods).GetMethod(nameof(ReturnSiteInjectionMethods.Double))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 20)]
    [InlineData(2, 30)]
    public void Return_ByFirstReturnOnly_TransformsOnlyThatSite(int which, int expected) {
        MethodBase target = typeof(ReturnSiteTarget).GetMethod(nameof(ReturnSiteTarget.Pick))!;
        MethodBase injectionMethod = typeof(ReturnSiteInjectionMethods).GetMethod(nameof(ReturnSiteInjectionMethods.Double))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Return_ByThirdReturn_TransformsOnlyThatSite() {
        MethodBase target = typeof(ReturnSiteTarget).GetMethod(nameof(ReturnSiteTarget.Pick))!;
        MethodBase injectionMethod = typeof(ReturnSiteInjectionMethods).GetMethod(nameof(ReturnSiteInjectionMethods.Double))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(3), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);

        Assert.Equal(10, result.Wrapper.Invoke(null, [0]));
        Assert.Equal(20, result.Wrapper.Invoke(null, [1]));
        Assert.Equal(60, result.Wrapper.Invoke(null, [2]));
    }

    [Fact]
    public void Return_ByExceedingReturnCount_ThrowsCONC035() {
        MethodBase target = typeof(ReturnSiteTarget).GetMethod(nameof(ReturnSiteTarget.Pick))!;
        MethodBase injectionMethod = typeof(ReturnSiteInjectionMethods).GetMethod(nameof(ReturnSiteInjectionMethods.Double))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(4), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [ret]));
        Assert.Equal("CONC035", ex.Code);
    }
}
