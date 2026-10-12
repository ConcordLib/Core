using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class ArrivalRaceTests
{
    private const string MonoReason =
        "Arming a notifier race needs the live UpdateWrapper detour dropped and re-applied, and mono " +
        "cannot survive that: UpdateWrapperHook.DetachObserver documents the SIGSEGV, so the detour is " +
        "meant to stay for the process lifetime. Under mono the re-applied detour comes back dead, every " +
        "later test's notifier stops firing, and 27 of them fail in a full run while each passes alone. " +
        "Nothing in a running game ever drops the hook, so this race is only reachable on CoreCLR.";

    private const string ModId = "test.arrivalrace.mod";

    private const int WindowMs = 750;

    private const int JoinMs = 10000;

    private static readonly FieldInfo HookHandlesField =
        typeof(UpdateWrapperHook).GetField("HookHandles", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("UpdateWrapperHook.HookHandles not found");

    private static readonly Assembly HarmonyCopy = typeof(HarmonyLib.Harmony).Assembly;

    [CoreClrFact(MonoReason)]
    public void TwoThreadsArmingAtOnce_LeaveExactlyOneLiveNotifier()
    {
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        Observer observer = new Observer();
        GateBackend gate = new GateBackend();
        bool[] results = new bool[2];
        Exception[] faults = new Exception[2];

        try
        {
            Assert.True(UpdateWrapperHook.TryInstall(observer, new MonoModDetourBackend(), observer.Record));
            DropLiveHook();
            int baseline = NotificationsFor(harmony, observer, typeof(BeforeTarget));
            Assert.True(baseline == 0, "a notifier was already live before the arm started and fired " + baseline + " times");

            Thread first = Arm(0, observer, gate, results, faults);
            Assert.True(gate.InsideApply.Wait(JoinMs), "the first arm never reached the backend");

            Thread second = Arm(1, observer, gate, results, faults);
            bool secondApplied = gate.SecondApply.Wait(WindowMs);

            gate.Release.Set();
            Assert.True(first.Join(JoinMs), "the first arm never returned");
            Assert.True(second.Join(JoinMs), "the second arm never returned");
            Assert.Null(faults[0]);
            Assert.Null(faults[1]);
            Assert.True(results[0]);
            Assert.True(results[1]);

            int after = NotificationsFor(harmony, observer, typeof(AfterTarget));
            Assert.True(
                after == 1,
                "one patch after a two-thread arm fired the notifier " + after + " times (" + gate.ApplyCount +
                " applies, second thread applied mid-flight: " + secondApplied + ")");
        }
        finally
        {
            gate.Release.Set();
            TestUnpatch.Own(harmony, ModId);
            gate.DisposeAll();
            RearmAndDetach();
        }
    }

    [CoreClrFact(MonoReason)]
    public void PatchIssuedMidArm_CannotLandBeforeTheArmFinishes()
    {
        MethodInfo target = Method(typeof(MidArmTarget), nameof(MidArmTarget.Compute));
        Assert.Null(PatchProcessor.GetPatchInfo(target));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        Observer observer = new Observer();
        GateBackend gate = new GateBackend();
        bool[] results = new bool[1];
        Exception[] faults = new Exception[1];
        Exception? patchFault = null;
        long landedAtMs = -1;
        Stopwatch clock = new Stopwatch();

        try
        {
            DropLiveHook();

            Thread arm = Arm(0, observer, gate, results, faults);
            Assert.True(gate.InsideApply.Wait(JoinMs), "the arm never reached the backend");
            clock.Start();

            Thread patcher = new Thread(() =>
            {
                try
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(Method(typeof(RaceMods), nameof(RaceMods.Prefix))));
                    Interlocked.Exchange(ref landedAtMs, clock.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    patchFault = ex;
                }
            });
            patcher.Start();

            bool landedMidArm = patcher.Join(WindowMs);
            long window = clock.ElapsedMilliseconds;
            gate.Release.Set();

            Assert.True(arm.Join(JoinMs), "the arm never returned");
            Assert.True(patcher.Join(JoinMs), "the patch never returned");
            Assert.Null(faults[0]);
            Assert.Null(patchFault);
            Assert.True(results[0]);

            bool seen = observer.Seen.Contains(target);
            Assert.True(
                seen,
                "a Harmony patch on " + target.Name + " landed unseen. issued while the arm was mid-flight, landed mid-arm: " +
                landedMidArm + " at " + Interlocked.Read(ref landedAtMs) + "ms, observed window " + window + "ms");
        }
        finally
        {
            gate.Release.Set();
            TestUnpatch.Own(harmony, ModId);
            gate.DisposeAll();
            RearmAndDetach();
        }
    }

    private static int NotificationsFor(HarmonyLib.Harmony harmony, Observer observer, Type owner)
    {
        MethodInfo target = Method(owner, "Compute");
        Assert.Null(PatchProcessor.GetPatchInfo(target));
        observer.Seen.Clear();
        harmony.Patch(target, prefix: new HarmonyMethod(Method(typeof(RaceMods), nameof(RaceMods.Prefix))));
        int count = 0;
        foreach (MethodBase seen in observer.Seen)
        {
            if (seen == target)
            {
                count++;
            }
        }

        return count;
    }

    private static void DropLiveHook()
    {
        Assert.NotNull(HookHandlesField);
        Dictionary<Assembly, IDetourHandle> handles = (Dictionary<Assembly, IDetourHandle>)(HookHandlesField.GetValue(null)
            ?? throw new InvalidOperationException("UpdateWrapperHook.HookHandles read back null"));
        IDetourHandle? live;
        lock (handles)
        {
            handles.TryGetValue(HarmonyCopy, out live);
            handles.Remove(HarmonyCopy);
        }

        live?.Dispose();
    }

    private static void RearmAndDetach()
    {
        UpdateWrapperHook.TryInstall(null!, new MonoModDetourBackend(), Noop);
        UpdateWrapperHook.DetachObserver();
    }

    private static void Noop(string line)
    {
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static Thread Arm(int slot, Observer observer, GateBackend gate, bool[] results, Exception[] faults)
    {
        Thread thread = new Thread(() =>
        {
            try
            {
                results[slot] = UpdateWrapperHook.TryInstall(observer, gate, observer.Record);
            }
            catch (Exception ex)
            {
                faults[slot] = ex;
            }
        });
        thread.Start();
        return thread;
    }

    private sealed class Observer : IForeignPatchObserver
    {
        public List<MethodBase> Seen { get; } = new List<MethodBase>();

        public List<string> Lines { get; } = new List<string>();

        public void Record(string line)
        {
            lock (Lines)
            {
                Lines.Add(line);
            }
        }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
            lock (Seen)
            {
                Seen.Add(target);
            }
        }
    }

    private sealed class GateBackend : IDetourBackend
    {
        private readonly MonoModDetourBackend inner = new MonoModDetourBackend();

        private readonly List<IDetourHandle> handles = new List<IDetourHandle>();

        private int applies;

        public ManualResetEventSlim InsideApply { get; } = new ManualResetEventSlim();

        public ManualResetEventSlim SecondApply { get; } = new ManualResetEventSlim();

        public ManualResetEventSlim Release { get; } = new ManualResetEventSlim();

        public int ApplyCount => Volatile.Read(ref applies);

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
        {
            throw new NotSupportedException();
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
        {
            int ordinal = Interlocked.Increment(ref applies);
            if (ordinal == 1)
            {
                InsideApply.Set();
                Release.Wait(JoinMs);
            }
            else
            {
                SecondApply.Set();
            }

            IDetourHandle handle = inner.ApplyComposed(target, added);
            lock (handles)
            {
                handles.Add(handle);
            }

            return handle;
        }

        public void DisposeAll()
        {
            lock (handles)
            {
                foreach (IDetourHandle handle in handles)
                {
                    handle.Dispose();
                }

                handles.Clear();
            }
        }
    }

    public static class BeforeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 1;
        }
    }

    public static class AfterTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 2;
        }
    }

    public static class MidArmTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 3;
        }
    }

    public static class RaceMods
    {
        public static void Prefix()
        {
        }
    }
}
