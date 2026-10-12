using System;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class CatchNestingLog {
    public static readonly List<string> Entries = [];

    public static void Clear() {
        Entries.Clear();
    }

    public static void Add(string entry) {
        Entries.Add(entry);
    }
}

public static class CatchNestingObserved {
    public static int Ran;

    public static Exception? Error;

    public static void Clear() {
        Ran = 0;
        Error = null;
    }
}

public static class CatchNestingTargets {
    public static int Plain(int x) {
        if (x < 0) {
            CatchThrower.Boom();
        }

        return x * 2;
    }

    public static int OwnCatch(int x) {
        try {
            if (x < 0) {
                CatchThrower.Boom();
            }

            return x * 2;
        } catch (InvalidOperationException) {
            CatchNestingLog.Add("own-catch");
            return -1;
        }
    }

    public static int OwnCatchThrows(int x) {
        try {
            if (x < 0) {
                CatchThrower.Boom();
            }

            return x * 2;
        } catch (InvalidOperationException) {
            CatchNestingLog.Add("own-catch");
            throw new NotSupportedException("from-handler");
        }
    }
}

public static class CatchNestingMethods {
    public static void Stop() {
        CatchNestingLog.Add("finally");
    }

    public static void SwallowInt(Exception error, ControlHandle<int> control) {
        CatchNestingObserved.Ran++;
        CatchNestingObserved.Error = error;
        CatchNestingLog.Add("catch");
        control.Cancel();
    }

    public static void ObserveInt(Exception error) {
        CatchNestingObserved.Ran++;
        CatchNestingObserved.Error = error;
        CatchNestingLog.Add("catch");
    }
}

public sealed class CatchNestingTests {
    [Fact]
    public void CatchAndFinally_RunCatchThenFinally_InBothDeclarationOrders() {
        foreach (bool catchFirst in new[] { true, false }) {
            Func<int, int> invoke = Delegate(nameof(CatchNestingTargets.Plain), catchFirst);
            CatchNestingLog.Clear();
            CatchNestingObserved.Clear();

            Assert.Equal(0, invoke(-1));

            Assert.Equal(1, CatchNestingObserved.Ran);
            Assert.Equal(["catch", "finally"], CatchNestingLog.Entries);
        }
    }

    [Fact]
    public void FinallyDeclaredFirst_ComposesTheSameIlAndTheSameOrder() {
        MethodBase target = typeof(CatchNestingTargets).GetMethod(nameof(CatchNestingTargets.Plain))!;
        Injection catchInjection = Catch(nameof(CatchNestingMethods.SwallowInt), 0);
        Injection finallyInjection = Finally(nameof(CatchNestingMethods.Stop), 1);

        string catchFirst = Shape(WrapperComposer.ComposeDump(target, [catchInjection, finallyInjection]));
        string finallyFirst = Shape(WrapperComposer.ComposeDump(target, [finallyInjection, catchInjection]));
        string finallyOnly = Shape(WrapperComposer.ComposeDump(target, [finallyInjection]));

        Assert.Equal(catchFirst, finallyFirst);
        Assert.NotEqual(finallyOnly, catchFirst);

        List<string> catchFirstOrder = RunAndCapture(target, [catchInjection, finallyInjection]);
        List<string> finallyFirstOrder = RunAndCapture(target, [finallyInjection, catchInjection]);

        Assert.Equal(2, catchFirstOrder.Count);
        Assert.Equal(catchFirstOrder, finallyFirstOrder);
    }

    [Fact]
    public void TargetWithItsOwnCatch_HandlesItsOwnThrowAndTheInjectionNeverRuns() {
        MethodBase target = typeof(CatchNestingTargets).GetMethod(nameof(CatchNestingTargets.OwnCatch))!;
        ComposeResult result = WrapperComposer.Compose(target, [Catch(nameof(CatchNestingMethods.ObserveInt), 0)]);
        Func<int, int> invoke = result.Wrapper.CreateDelegate<Func<int, int>>();
        CatchNestingLog.Clear();
        CatchNestingObserved.Clear();

        Assert.Equal(-1, invoke(-1));

        Assert.Equal(0, CatchNestingObserved.Ran);
        Assert.Equal(["own-catch"], CatchNestingLog.Entries);
    }

    [Fact]
    public void TargetThatThrowsFromItsOwnHandler_ReachesTheInjectedCatch() {
        MethodBase target = typeof(CatchNestingTargets).GetMethod(nameof(CatchNestingTargets.OwnCatchThrows))!;
        ComposeResult result = WrapperComposer.Compose(target, [Catch(nameof(CatchNestingMethods.SwallowInt), 0)]);
        Func<int, int> invoke = result.Wrapper.CreateDelegate<Func<int, int>>();
        CatchNestingLog.Clear();
        CatchNestingObserved.Clear();

        Assert.Equal(0, invoke(-1));

        Assert.Equal(1, CatchNestingObserved.Ran);
        NotSupportedException seen = Assert.IsType<NotSupportedException>(CatchNestingObserved.Error);
        Assert.Equal("from-handler", seen.Message);
        Assert.Equal(["own-catch", "catch"], CatchNestingLog.Entries);
    }

    private static string Shape(string dump) {
        int start = dump.IndexOf("locals[", StringComparison.Ordinal);
        int end = dump.IndexOf("assemblies[", StringComparison.Ordinal);
        Assert.InRange(start, 0, end);
        return dump.Substring(start, end - start);
    }

    private static List<string> RunAndCapture(MethodBase target, IReadOnlyList<Injection> injections) {
        ComposeResult result = WrapperComposer.Compose(target, injections);
        Func<int, int> invoke = result.Wrapper.CreateDelegate<Func<int, int>>();
        CatchNestingLog.Clear();
        CatchNestingObserved.Clear();
        invoke(-1);
        return new List<string>(CatchNestingLog.Entries);
    }

    private static Injection Catch(string injectionMethodName, int priority) {
        return new Injection(typeof(CatchNestingMethods).GetMethod(injectionMethodName)!, new InjectAt.Catch(), "test", priority);
    }

    private static Injection Finally(string injectionMethodName, int priority) {
        return new Injection(typeof(CatchNestingMethods).GetMethod(injectionMethodName)!, new InjectAt.Finally(), "test", priority);
    }

    private static Func<int, int> Delegate(string targetName, bool catchFirst) {
        MethodBase target = typeof(CatchNestingTargets).GetMethod(targetName)!;
        Injection catchInjection = Catch(nameof(CatchNestingMethods.SwallowInt), 0);
        Injection finallyInjection = Finally(nameof(CatchNestingMethods.Stop), 1);
        IReadOnlyList<Injection> injections = catchFirst
            ? [catchInjection, finallyInjection]
            : [finallyInjection, catchInjection];
        return WrapperComposer.Compose(target, injections).Wrapper.CreateDelegate<Func<int, int>>();
    }
}
