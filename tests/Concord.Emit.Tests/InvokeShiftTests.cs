using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class InvokeShiftLog {
    public static List<string> Entries = new List<string>();

    public static void Clear() {
        Entries.Clear();
    }
}

public static class InvokeShiftHelper {
    public static void Tick() {
        InvokeShiftLog.Entries.Add("tick");
    }
}

public class InvokeShiftHost {
    public void Run() {
        InvokeShiftHelper.Tick();
        InvokeShiftHelper.Tick();
    }
}

public static class InvokeShiftMethods {
    // The body calls the same method the injection matches, which is what an At.Invoke injection
    // that reuses the call it wraps ordinarily looks like.
    public static void First(ControlHandle handle) {
        InvokeShiftLog.Entries.Add("first");
        InvokeShiftHelper.Tick();
    }

    public static void Second(ControlHandle handle) {
        InvokeShiftLog.Entries.Add("second");
    }
}

public sealed class InvokeShiftTests {
    // At.Invoke must count By against the spine as it was before any injection spliced into it.
    // The By:1 body calls Tick itself, so counting live puts the By:2 splice in front of that
    // copied call rather than the target's second one.
    [Fact]
    public void By_CountsAgainstThePreSpliceSpine() {
        MethodBase target = typeof(InvokeShiftHost).GetMethod(nameof(InvokeShiftHost.Run))!;
        MethodBase first = typeof(InvokeShiftMethods).GetMethod(nameof(InvokeShiftMethods.First))!;
        MethodBase second = typeof(InvokeShiftMethods).GetMethod(nameof(InvokeShiftMethods.Second))!;

        Injection late = new Injection(second, new InjectAt.Invoke(typeof(InvokeShiftHelper), nameof(InvokeShiftHelper.Tick), At.Head, 2), "test", 0);
        Injection early = new Injection(first, new InjectAt.Invoke(typeof(InvokeShiftHelper), nameof(InvokeShiftHelper.Tick), At.Head, 1), "test", 0);

        InvokeShiftLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [late, early]);
        result.Wrapper.Invoke(null, [new InvokeShiftHost()]);

        Assert.Equal(["first", "tick", "tick", "second", "tick"], InvokeShiftLog.Entries);
    }
}

public sealed class NewObjShiftThing {
    public NewObjShiftThing() {
        InvokeShiftLog.Entries.Add("new");
    }
}

public class NewObjShiftHost {
    public void Run() {
        NewObjShiftThing first = new NewObjShiftThing();
        NewObjShiftThing second = new NewObjShiftThing();
        InvokeShiftLog.Entries.Add(ReferenceEquals(first, second) ? "same" : "distinct");
    }
}

public static class NewObjShiftMethods {
    // Allocates the same type the injection matches, which is what an At.NewObj injection that
    // substitutes or mirrors the allocation ordinarily looks like.
    public static void First(ControlHandle handle) {
        InvokeShiftLog.Entries.Add("first");
        NewObjShiftThing spare = new NewObjShiftThing();
        InvokeShiftLog.Entries.Add(spare is null ? "null" : "made");
    }

    public static void Second(ControlHandle handle) {
        InvokeShiftLog.Entries.Add("second");
    }
}

public sealed class NewObjShiftTests {
    // At.NewObj carries the same defect as At.Constant and At.Invoke, found while fixing those two.
    // The By:1 body allocates the matched type, so counting live puts the By:2 splice inside the
    // By:1 body, where its own continuation branch jumps straight over it and the injection never
    // runs at all.
    [Fact]
    public void By_CountsAgainstThePreSpliceSpine() {
        MethodBase target = typeof(NewObjShiftHost).GetMethod(nameof(NewObjShiftHost.Run))!;
        MethodBase first = typeof(NewObjShiftMethods).GetMethod(nameof(NewObjShiftMethods.First))!;
        MethodBase second = typeof(NewObjShiftMethods).GetMethod(nameof(NewObjShiftMethods.Second))!;

        Injection late = new Injection(second, new InjectAt.NewObj(typeof(NewObjShiftThing), At.Head, 2), "test", 0);
        Injection early = new Injection(first, new InjectAt.NewObj(typeof(NewObjShiftThing), At.Head, 1), "test", 0);

        InvokeShiftLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [late, early]);
        result.Wrapper.Invoke(null, [new NewObjShiftHost()]);

        Assert.Equal(["first", "new", "made", "new", "second", "new", "distinct"], InvokeShiftLog.Entries);
    }
}
