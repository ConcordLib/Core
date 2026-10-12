using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Emit.Tests;

public static class CatchLog {
    public static readonly List<string> Entries = [];

    public static void Clear() {
        Entries.Clear();
    }

    public static void Add(string entry) {
        Entries.Add(entry);
    }
}

public static class CatchObserved {
    public static int Ran;

    public static Exception? Error;

    public static int Int;

    public static string? Text;

    public static int? Maybe;

    public static long Before;

    public static double After;

    public static void Clear() {
        Ran = 0;
        Error = null;
        Int = -1;
        Text = "unset";
        Maybe = -1;
        Before = -1;
        After = -1;
    }
}

public static class CatchThrower {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Boom() {
        throw new InvalidOperationException("boom");
    }
}

public static class CatchTargets {
    public static int Doubled(int x) {
        if (x < 0) {
            CatchThrower.Boom();
        }

        return x * 2;
    }

    public static void Void(int x) {
        if (x < 0) {
            CatchThrower.Boom();
        }

        CatchLog.Add("body");
    }

    public static DateTime Stamp(int x) {
        if (x < 0) {
            CatchThrower.Boom();
        }

        return new DateTime(2020, 1, 1);
    }

    public static string Text(int x) {
        if (x < 0) {
            CatchThrower.Boom();
        }

        return "body";
    }

    public static int? Maybe(int x) {
        if (x < 0) {
            CatchThrower.Boom();
        }

        return x;
    }

    public static int Tracked(int seed) {
        Exception reason = new ArgumentException("pre");
        if (seed > 0) {
            CatchThrower.Boom();
        }

        return reason.Message.Length + seed;
    }

    public static int Measured(int seed) {
        long before = seed * 2L;
        if (seed > 0) {
            CatchThrower.Boom();
        }

        double after = seed * 3.0;
        return before.ToString().Length + after.ToString().Length;
    }
}

public static class CatchMethods {
    public static void Observe(Exception error) {
        CatchObserved.Ran++;
        CatchObserved.Error = error;
        CatchLog.Add("catch:" + error.GetType().Name + ":" + error.Message);
    }

    public static void Swallow(Exception error, ControlHandle<int> control) {
        CatchObserved.Ran++;
        CatchObserved.Error = error;
        control.Cancel();
    }

    public static void Replace(Exception error, ControlHandle<int> control) {
        CatchObserved.Ran++;
        control.ReturnValue = 99;
        control.Cancel();
    }

    public static void SwallowVoid(Exception error, ControlHandle control) {
        CatchObserved.Ran++;
        CatchLog.Add("catch");
        control.Cancel();
    }

    public static void ReplaceStamp(Exception error, ControlHandle<DateTime> control) {
        CatchObserved.Ran++;
        control.ReturnValue = new DateTime(1999, 12, 31);
        control.Cancel();
    }

    public static void ReadIntReturnValue(Exception error, ControlHandle<int> control) {
        CatchObserved.Ran++;
        CatchObserved.Int = control.ReturnValue;
        control.Cancel();
    }

    public static void ReadTextReturnValue(Exception error, ControlHandle<string> control) {
        CatchObserved.Ran++;
        CatchObserved.Text = control.ReturnValue;
        control.Cancel();
    }

    public static void ReadMaybeReturnValue(Exception error, ControlHandle<int?> control) {
        CatchObserved.Ran++;
        CatchObserved.Maybe = control.ReturnValue;
        control.Cancel();
    }

    public static void First(Exception error) {
        CatchLog.Add("first");
    }

    public static void Second(Exception error) {
        CatchLog.Add("second");
    }

    public static void ThirdSwallows(Exception error, ControlHandle<int> control) {
        CatchLog.Add("third");
        control.ReturnValue = 7;
        control.Cancel();
    }

    public static void ReadLocal(Exception error, [Local] long before) {
        CatchObserved.Before = before;
    }

    public static void CancelThenRethrow(Exception error, ControlHandle<int> control) {
        CatchObserved.Ran++;
        CatchObserved.Error = error;
        control.Cancel();
        control.Rethrow();
    }

    public static void ReadExceptionWithNoExceptionParameter(ControlHandle<int> control) {
        CatchObserved.Ran++;
        CatchObserved.Text = control.Exception.Message;
        control.Cancel();
    }

    public static void ReadExceptionLocalBesideTheException(Exception error, [Local] Exception reason) {
        CatchObserved.Ran++;
        CatchObserved.Error = error;
        CatchObserved.Text = reason.Message;
    }

    public static void ObserveNarrowException(InvalidOperationException error) {
        CatchObserved.Ran++;
        CatchObserved.Text = error.Message;
    }

    public static void ObserveTwoExceptions(Exception error, Exception alsoError) {
        CatchObserved.Ran++;
        CatchObserved.Error = error;
        CatchObserved.Text = alsoError.Message;
    }

    public static void ObserveByRefException(ref Exception error) {
        CatchObserved.Ran++;
        CatchObserved.Error = error;
    }

    public static void ObserveOutException(out Exception error) {
        CatchObserved.Ran++;
        error = new InvalidOperationException("replaced");
    }

    public static void ObserveInException(in Exception error) {
        CatchObserved.Ran++;
        CatchObserved.Error = error;
    }

    public static void ObserveLocals([Local] long before, [Local] double after) {
        CatchObserved.Ran++;
        CatchObserved.Before = before;
        CatchObserved.After = after;
    }
}

public sealed class CatchInjectionTests {
    [Fact]
    public void Catch_HandlerSeesTheThrownExceptionTypeAndMessage() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.Observe));
        CatchLog.Clear();
        CatchObserved.Clear();

        Assert.Throws<InvalidOperationException>(() => invoke(-1));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Equal(["catch:InvalidOperationException:boom"], CatchLog.Entries);
    }

    [Fact]
    public void Catch_HandlerReplacesTheReturnValue_CallerReadsTheReplacement() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.Replace));
        CatchObserved.Clear();

        Assert.Equal(99, invoke(-1));
        Assert.Equal(1, CatchObserved.Ran);
    }

    [Fact]
    public void Catch_HandlerSwallows_CallerGetsANormalReturn() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.Swallow));
        CatchObserved.Clear();

        Assert.Equal(0, invoke(-1));
        Assert.Equal(1, CatchObserved.Ran);
    }

    [Fact]
    public void Catch_HandlerRethrows_CallerSeesTheOriginalExceptionAndItsStack() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.Observe));
        CatchObserved.Clear();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => invoke(-1));

        Assert.Same(CatchObserved.Error, thrown);
        Assert.NotNull(thrown.StackTrace);
        Assert.Contains(nameof(CatchThrower.Boom), thrown.StackTrace);
        Assert.Contains(nameof(CatchThrower.Boom), FirstFrame(thrown.StackTrace!));
    }

    [Fact]
    public void Catch_HandlerCancelsThenRethrows_CallerSeesTheOriginalException() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.CancelThenRethrow));
        CatchObserved.Clear();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => invoke(-1));

        Assert.Same(CatchObserved.Error, thrown);
        Assert.Equal(1, CatchObserved.Ran);
    }

    [Fact]
    public void Catch_ReadsExceptionProperty_WithNoExceptionParameterOnTheHandler() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.ReadExceptionWithNoExceptionParameter));
        CatchObserved.Clear();

        Assert.Equal(0, invoke(-1));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Equal("boom", CatchObserved.Text);
    }

    [Fact]
    public void Catch_NothingThrows_HandlerNeverRuns() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.Observe));
        CatchLog.Clear();
        CatchObserved.Clear();

        Assert.Equal(8, invoke(4));

        Assert.Equal(0, CatchObserved.Ran);
        Assert.Empty(CatchLog.Entries);
    }

    [Fact]
    public void Catch_SwallowsOnAVoidTarget() {
        Action<int> invoke = Wrap<Action<int>>(nameof(CatchTargets.Void), nameof(CatchMethods.SwallowVoid));
        CatchLog.Clear();
        CatchObserved.Clear();

        invoke(-1);

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Equal(["catch"], CatchLog.Entries);
    }

    [Fact]
    public void Catch_ReplacesOnAValueTypeTarget() {
        Func<int, DateTime> invoke = Wrap<Func<int, DateTime>>(nameof(CatchTargets.Stamp), nameof(CatchMethods.ReplaceStamp));
        CatchObserved.Clear();

        Assert.Equal(new DateTime(1999, 12, 31), invoke(-1));
        Assert.Equal(1, CatchObserved.Ran);
    }

    [Fact]
    public void Catch_SeveralInjections_ChainInTheSameOrderAsFinallyBodies() {
        MethodBase target = typeof(CatchTargets).GetMethod(nameof(CatchTargets.Doubled))!;
        ComposeResult asCatch = WrapperComposer.Compose(target, [
            Catch(nameof(CatchMethods.First), 0),
            Catch(nameof(CatchMethods.Second), 1),
            Catch(nameof(CatchMethods.ThirdSwallows), 2),
        ]);
        CatchLog.Clear();
        Assert.Equal(7, asCatch.Wrapper.CreateDelegate<Func<int, int>>()(-1));
        List<string> catchOrder = new List<string>(CatchLog.Entries);

        ComposeResult asFinally = WrapperComposer.Compose(target, [
            Finally(nameof(CatchMethods.First), 0),
            Finally(nameof(CatchMethods.Second), 1),
        ]);
        CatchLog.Clear();
        asFinally.Wrapper.CreateDelegate<Func<int, int>>()(4);
        List<string> finallyOrder = new List<string>(CatchLog.Entries);

        Assert.Equal(["third", "second", "first"], catchOrder);
        Assert.Equal(["second", "first"], finallyOrder);
    }

    [Fact]
    public void Catch_ReturnValueInsideTheHandler_IsTheZeroDefaultForAReferenceType() {
        Func<int, string> invoke = Wrap<Func<int, string>>(nameof(CatchTargets.Text), nameof(CatchMethods.ReadTextReturnValue));
        CatchObserved.Clear();

        Assert.Null(invoke(-1));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Null(CatchObserved.Text);
    }

    [Fact]
    public void Catch_ReturnValueInsideTheHandler_IsTheZeroDefaultForAValueType() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Doubled), nameof(CatchMethods.ReadIntReturnValue));
        CatchObserved.Clear();

        Assert.Equal(0, invoke(-1));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Equal(0, CatchObserved.Int);
    }

    [Fact]
    public void Catch_ReturnValueInsideTheHandler_IsTheZeroDefaultForANullable() {
        Func<int, int?> invoke = Wrap<Func<int, int?>>(nameof(CatchTargets.Maybe), nameof(CatchMethods.ReadMaybeReturnValue));
        CatchObserved.Clear();

        Assert.Null(invoke(-1));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Null(CatchObserved.Maybe);
        Assert.False(CatchObserved.Maybe.HasValue);
    }

    [Fact]
    public void Catch_LocalReadOnTheThrowingPath_SeesPreThrowAssignmentsAndZeroAfterThem() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Measured), nameof(CatchMethods.ObserveLocals));
        CatchObserved.Clear();

        Assert.Throws<InvalidOperationException>(() => invoke(3));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Equal(6L, CatchObserved.Before);
        Assert.Equal(0.0, CatchObserved.After);
    }

    [Fact]
    public void Catch_LocalReadBesideTheExceptionParameter_SeesThePreThrowValue() {
        Func<int, int> invoke = Wrap<Func<int, int>>(nameof(CatchTargets.Measured), nameof(CatchMethods.ReadLocal));
        CatchObserved.Clear();

        Assert.Throws<InvalidOperationException>(() => invoke(3));

        Assert.Equal(6L, CatchObserved.Before);
    }

    [Fact]
    public void Finally_LocalReadOnTheThrowingPath_SeesPreThrowAssignmentsAndZeroAfterThem() {
        MethodBase target = typeof(CatchTargets).GetMethod(nameof(CatchTargets.Measured))!;
        Injection observe = new Injection(
            typeof(CatchMethods).GetMethod(nameof(CatchMethods.ObserveLocals))!, new InjectAt.Finally(), "test", 0);
        ComposeResult result = WrapperComposer.Compose(target, [observe]);
        Func<int, int> invoke = result.Wrapper.CreateDelegate<Func<int, int>>();
        CatchObserved.Clear();

        Assert.Throws<InvalidOperationException>(() => invoke(3));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Equal(6L, CatchObserved.Before);
        Assert.Equal(0.0, CatchObserved.After);
    }

    [Fact]
    public void Catch_ExceptionTypedLocalParameter_BindsTheLocalNotTheCaughtException() {
        Func<int, int> invoke = Wrap<Func<int, int>>(
            nameof(CatchTargets.Tracked), nameof(CatchMethods.ReadExceptionLocalBesideTheException));
        CatchObserved.Clear();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => invoke(3));

        Assert.Equal(1, CatchObserved.Ran);
        Assert.Same(thrown, CatchObserved.Error);
        Assert.Equal("pre", CatchObserved.Text);
    }

    [Fact]
    public void Catch_ExceptionParameterTypedAsASubclass_IsRejectedAtComposeTime() {
        MethodBase target = typeof(CatchTargets).GetMethod(nameof(CatchTargets.Measured))!;
        Injection injection = Catch(nameof(CatchMethods.ObserveNarrowException), 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [injection]));

        Assert.Equal("CONC170", error.Code);
        Assert.Contains("declares exception parameter 'error' as 'System.InvalidOperationException'", error.Message);
    }

    [Fact]
    public void Catch_SecondSystemExceptionParameter_IsRejectedAtComposeTime() {
        MethodBase target = typeof(CatchTargets).GetMethod(nameof(CatchTargets.Measured))!;
        Injection injection = Catch(nameof(CatchMethods.ObserveTwoExceptions), 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [injection]));

        Assert.Equal("CONC170", error.Code);
        Assert.Contains("declares a second System.Exception parameter 'alsoError'", error.Message);
    }

    [Theory]
    [InlineData(nameof(CatchMethods.ObserveByRefException))]
    [InlineData(nameof(CatchMethods.ObserveOutException))]
    [InlineData(nameof(CatchMethods.ObserveInException))]
    public void Catch_ExceptionParameterPassedByReference_IsRejectedAtComposeTime(string injectionMethodName) {
        MethodBase target = typeof(CatchTargets).GetMethod(nameof(CatchTargets.Doubled))!;
        Injection injection = Catch(injectionMethodName, 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [injection]));

        Assert.Equal("CONC170", error.Code);
        Assert.Contains("at At.Catch", error.Message);
        Assert.Contains("exception parameter 'error' by reference", error.Message);
    }

    private static string FirstFrame(string stackTrace) {
        int newline = stackTrace.IndexOf('\n');
        return newline < 0 ? stackTrace : stackTrace.Substring(0, newline);
    }

    private static Injection Catch(string injectionMethodName, int priority) {
        return new Injection(typeof(CatchMethods).GetMethod(injectionMethodName)!, new InjectAt.Catch(), "test", priority);
    }

    private static Injection Finally(string injectionMethodName, int priority) {
        return new Injection(typeof(CatchMethods).GetMethod(injectionMethodName)!, new InjectAt.Finally(), "test", priority);
    }

    private static TDelegate Wrap<TDelegate>(string targetName, string injectionMethodName)
        where TDelegate : System.Delegate {
        MethodBase target = typeof(CatchTargets).GetMethod(targetName)!;
        ComposeResult result = WrapperComposer.Compose(target, [Catch(injectionMethodName, 0)]);
        return result.Wrapper.CreateDelegate<TDelegate>();
    }
}
