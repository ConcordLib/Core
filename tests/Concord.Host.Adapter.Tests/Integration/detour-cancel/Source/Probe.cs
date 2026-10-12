using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Concord.Waylith.DetourCancelProbe
{
    internal enum Mode
    {
        Off,
        All,
        Owned,
        AttributeOnly,
    }

    internal static class Hook
    {
        internal static Mode mode = Mode.Off;
        internal static readonly HashSet<MethodBase> Owned = new HashSet<MethodBase>();
        internal static readonly List<string> Trace = new List<string>();
        internal static int Hits;
        internal static int Cancels;

        internal static bool Prefix(MethodBase method, MethodBase replacement)
        {
            Hits++;
            var attr = HasReverseAttribute(method);
            var frame = CallerFrame();
            var owned = Owned.Contains(method);
            bool cancel;
            switch (mode)
            {
                case Mode.All: cancel = true; break;
                case Mode.Owned: cancel = owned; break;
                case Mode.AttributeOnly: cancel = !attr; break;
                default: cancel = false; break;
            }

            if (cancel)
            {
                Cancels++;
            }

            Trace.Add(string.Join("|", new[]
            {
                mode.ToString(),
                Describe(method),
                attr ? "has-reverse-attr" : "no-reverse-attr",
                frame,
                owned ? "owned" : "foreign",
                cancel ? "CANCELLED" : "passed",
                replacement == null ? "null-replacement" : "replacement-" + replacement.GetType().Name,
            }));
            return !cancel;
        }

        internal static string Describe(MethodBase m)
        {
            if (m == null)
            {
                return "null";
            }

            return (m.DeclaringType == null ? "?" : m.DeclaringType.Name) + "." + m.Name;
        }

        private static bool HasReverseAttribute(MethodBase method)
        {
            foreach (var a in method.GetCustomAttributes(false))
            {
                if (a.GetType().FullName == "HarmonyLib.HarmonyReversePatch")
                {
                    return true;
                }
            }

            return false;
        }

        private static string CallerFrame()
        {
            var st = new StackTrace(false);
            for (var i = 0; i < st.FrameCount; i++)
            {
                var f = st.GetFrame(i);
                var m = f == null ? null : f.GetMethod();
                if (m == null)
                {
                    continue;
                }

                if (m.Name == "UpdateWrapper" || m.Name == "ReversePatch")
                {
                    return (m.DeclaringType == null ? "?" : m.DeclaringType.Name) + "." + m.Name;
                }
            }

            return "no-harmony-frame";
        }
    }

    internal static class Targets
    {
        [MethodImpl(MethodImplOptions.NoInlining)] internal static int GateControl() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int GateCancel() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int NeverPatched() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int UnpatchControl() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int UnpatchCancel() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int UnpatchCancelThenOff() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int Leak() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int LeakControl() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int OwnedTarget() { return 1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int ForeignTarget() { return 1; }
    }

    internal static class Sources
    {
        [MethodImpl(MethodImplOptions.NoInlining)] internal static int RevControl() { return 77; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int RevBlanketBare() { return 78; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int RevAttrOnlyAttributed() { return 79; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int RevAttrOnlyBare() { return 80; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int RevOwnedBare() { return 81; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int RevAttrDriven() { return 82; }
    }

    internal static class Standins
    {
        [MethodImpl(MethodImplOptions.NoInlining)] internal static int Control() { return -1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int BlanketBare() { return -1; }

        [HarmonyReversePatch]
        [MethodImpl(MethodImplOptions.NoInlining)] internal static int AttrOnlyAttributed() { return -1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int AttrOnlyBare() { return -1; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int OwnedBare() { return -1; }
    }

    [HarmonyPatch]
    internal static class AttrDrivenReverse
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(Sources), nameof(Sources.RevAttrDriven))]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Copy() { return -1; }
    }

    internal static class Replacements
    {
        internal static bool Pre42(ref int __result)
        {
            __result = 42;
            return false;
        }

        internal static bool Pre43(ref int __result)
        {
            __result = 43;
            return false;
        }

        internal static void Post100(ref int __result)
        {
            __result += 100;
        }
    }

    internal static class Program
    {
        private const string Id = "concord.waylith.detourcancel.probe";

        private static readonly Harmony H = new Harmony(Id);
        private static IDictionary detours;
        private static MethodInfo getRealMethod;
        private static MethodBase detourMethod;

        private static void Row(string key, object value)
        {
            Console.Out.Write(key);
            Console.Out.Write('\t');
            Console.Out.WriteLine(value == null ? "null" : value.ToString());
        }

        private static MethodInfo M(string name)
        {
            return typeof(Targets).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static MethodInfo Src(string name)
        {
            return typeof(Sources).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static MethodInfo Standin(string name)
        {
            return typeof(Standins).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static HarmonyMethod Pre(string name)
        {
            return new HarmonyMethod(typeof(Replacements).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static int Prefixes(MethodBase m)
        {
            var p = Harmony.GetPatchInfo(m);
            return p == null ? -1 : p.Prefixes.Count;
        }

        private static int Postfixes(MethodBase m)
        {
            var p = Harmony.GetPatchInfo(m);
            return p == null ? -1 : p.Postfixes.Count;
        }

        private static string Owners(MethodBase m)
        {
            var p = Harmony.GetPatchInfo(m);
            if (p == null)
            {
                return "no-patchinfo";
            }

            return p.Owners.Count == 0 ? "no-owners" : string.Join(",", p.Owners);
        }

        private static bool Listed(MethodBase m)
        {
            foreach (var patched in Harmony.GetAllPatchedMethods())
            {
                if (patched == m)
                {
                    return true;
                }
            }

            return false;
        }

        private static int DetourId(MethodBase m)
        {
            if (!detours.Contains(m))
            {
                return 0;
            }

            return RuntimeHelpers.GetHashCode(detours[m]);
        }

        private static string Setup()
        {
            var harmonyAsm = typeof(Harmony).Assembly;
            Row("harmony.location", harmonyAsm.Location);
            Row("harmony.version", harmonyAsm.GetName().Version);

            var patchTools = harmonyAsm.GetType("HarmonyLib.PatchTools", false);
            if (patchTools == null)
            {
                return "HarmonyLib.PatchTools is not in this assembly";
            }

            detourMethod = patchTools.GetMethod("DetourMethod", BindingFlags.Static | BindingFlags.NonPublic);
            if (detourMethod == null)
            {
                return "PatchTools.DetourMethod is not in this assembly";
            }

            var ps = detourMethod.GetParameters();
            Row("seam.signature", ((MethodInfo)detourMethod).ReturnType.Name + " DetourMethod(" +
                string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            Row("seam.is.void", ((MethodInfo)detourMethod).ReturnType == typeof(void) ? 1 : 0);

            var field = patchTools.GetField("detours", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                return "PatchTools.detours is not in this assembly";
            }

            detours = (IDictionary)field.GetValue(null);
            Row("seam.detours.type", field.FieldType.Name);

            var shared = harmonyAsm.GetType("HarmonyLib.HarmonySharedState", false);
            getRealMethod = shared == null
                ? null
                : shared.GetMethod("GetRealMethod", BindingFlags.Static | BindingFlags.NonPublic);
            Row("seam.getrealmethod.found", getRealMethod == null ? 0 : 1);

            H.Patch(detourMethod, prefix: new HarmonyMethod(
                typeof(Hook).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)));
            Row("hook.installed", 1);
            Row("hook.target.detoured", detours.Contains(detourMethod) ? 1 : 0);
            Row("hook.hits.after.install", Hook.Hits);
            return null;
        }

        private static void Gates()
        {
            Row("gate.control.before", Targets.GateControl());
            var hitsBefore = Hook.Hits;
            Hook.mode = Mode.Off;
            H.Patch(M("GateControl"), prefix: Pre("Pre42"));
            Row("gate.control.hook.hits.delta", Hook.Hits - hitsBefore);
            Row("gate.control.after", Targets.GateControl());
            Row("gate.control.detour.present", detours.Contains(M("GateControl")) ? 1 : 0);

            Row("gate.cancel.before", Targets.GateCancel());
            hitsBefore = Hook.Hits;
            var cancelsBefore = Hook.Cancels;
            Hook.mode = Mode.All;
            H.Patch(M("GateCancel"), prefix: Pre("Pre42"));
            Hook.mode = Mode.Off;
            Row("gate.cancel.hook.hits.delta", Hook.Hits - hitsBefore);
            Row("gate.cancel.hook.cancels.delta", Hook.Cancels - cancelsBefore);
            Row("gate.cancel.after", Targets.GateCancel());
            Row("gate.cancel.detour.present", detours.Contains(M("GateCancel")) ? 1 : 0);
        }

        private static void Bookkeeping()
        {
            Row("q1.control.prefixes", Prefixes(M("GateControl")));
            Row("q1.control.owners", Owners(M("GateControl")));
            Row("q1.cancel.prefixes", Prefixes(M("GateCancel")));
            Row("q1.cancel.owners", Owners(M("GateCancel")));
            Row("q1.neverpatched.patchinfo", Harmony.GetPatchInfo(M("NeverPatched")) == null ? "null" : "present");
            Row("q1.neverpatched.prefixes", Prefixes(M("NeverPatched")));

            Row("q2.control.listed", Listed(M("GateControl")) ? 1 : 0);
            Row("q2.cancel.listed", Listed(M("GateCancel")) ? 1 : 0);
            Row("q2.neverpatched.listed", Listed(M("NeverPatched")) ? 1 : 0);
            Row("q2.detourmethod.listed", Listed(detourMethod) ? 1 : 0);
            Row("q2.total", Harmony.GetAllPatchedMethods().Count());
        }

        private static void RealMethod()
        {
            Hook.mode = Mode.Off;
            var controlReplacement = H.Patch(M("GateControl"), prefix: Pre("Pre43"));
            Hook.mode = Mode.All;
            var cancelReplacement = H.Patch(M("GateCancel"), prefix: Pre("Pre43"));
            Hook.mode = Mode.Off;

            var controlOriginal = Harmony.GetOriginalMethod(controlReplacement);
            var cancelOriginal = Harmony.GetOriginalMethod(cancelReplacement);
            var unrelatedOriginal = Harmony.GetOriginalMethod(M("NeverPatched"));
            Row("q4.control.getoriginal", Hook.Describe(controlOriginal));
            Row("q4.cancel.getoriginal", Hook.Describe(cancelOriginal));
            Row("q4.unrelated.getoriginal", Hook.Describe(unrelatedOriginal));
            Row("q4.control.getoriginal.is.identity", ReferenceEquals(controlOriginal, controlReplacement) ? 1 : 0);
            Row("q4.cancel.getoriginal.is.identity", ReferenceEquals(cancelOriginal, cancelReplacement) ? 1 : 0);
            Row("q4.unrelated.getoriginal.is.identity", ReferenceEquals(unrelatedOriginal, M("NeverPatched")) ? 1 : 0);
            Row("q4.cancel.getoriginal.is.target", ReferenceEquals(cancelOriginal, M("GateCancel")) ? 1 : 0);

            if (getRealMethod != null)
            {
                var controlReal = (MethodBase)getRealMethod.Invoke(null, new object[] { controlReplacement, true });
                var cancelReal = (MethodBase)getRealMethod.Invoke(null, new object[] { cancelReplacement, true });
                var unrelatedReal = (MethodBase)getRealMethod.Invoke(null, new object[] { M("NeverPatched"), true });
                Row("q4.control.getrealmethod", Hook.Describe(controlReal));
                Row("q4.cancel.getrealmethod", Hook.Describe(cancelReal));
                Row("q4.unrelated.getrealmethod", Hook.Describe(unrelatedReal));
                Row("q4.control.getrealmethod.is.identity", ReferenceEquals(controlReal, controlReplacement) ? 1 : 0);
                Row("q4.cancel.getrealmethod.is.identity", ReferenceEquals(cancelReal, cancelReplacement) ? 1 : 0);
                Row("q4.unrelated.getrealmethod.is.identity", ReferenceEquals(unrelatedReal, M("NeverPatched")) ? 1 : 0);
            }

            Row("q4.control.behaviour", Targets.GateControl());
            Row("q4.cancel.behaviour", Targets.GateCancel());
        }

        private static void Unpatching()
        {
            Hook.mode = Mode.Off;
            H.Patch(M("UnpatchControl"), prefix: Pre("Pre42"));
            Row("q3.control.patched.behaviour", Targets.UnpatchControl());
            Row("q3.control.patched.detour", detours.Contains(M("UnpatchControl")) ? 1 : 0);
            var beforeId = DetourId(M("UnpatchControl"));
            H.Unpatch(M("UnpatchControl"), HarmonyPatchType.All, Id);
            Row("q3.control.unpatched.behaviour", Targets.UnpatchControl());
            Row("q3.control.unpatched.prefixes", Prefixes(M("UnpatchControl")));
            Row("q3.control.unpatched.listed", Listed(M("UnpatchControl")) ? 1 : 0);
            Row("q3.control.unpatched.detour", detours.Contains(M("UnpatchControl")) ? 1 : 0);
            Row("q3.control.unpatched.detour.replaced", DetourId(M("UnpatchControl")) == beforeId ? 0 : 1);

            Hook.mode = Mode.All;
            H.Patch(M("UnpatchCancel"), prefix: Pre("Pre42"));
            Row("q3.cancel.patched.behaviour", Targets.UnpatchCancel());
            Row("q3.cancel.patched.detour", detours.Contains(M("UnpatchCancel")) ? 1 : 0);
            var cancelsBefore = Hook.Cancels;
            H.Unpatch(M("UnpatchCancel"), HarmonyPatchType.All, Id);
            Row("q3.cancel.unpatch.cancels.delta", Hook.Cancels - cancelsBefore);
            Hook.mode = Mode.Off;
            Row("q3.cancel.unpatched.behaviour", Targets.UnpatchCancel());
            Row("q3.cancel.unpatched.prefixes", Prefixes(M("UnpatchCancel")));
            Row("q3.cancel.unpatched.listed", Listed(M("UnpatchCancel")) ? 1 : 0);
            Row("q3.cancel.unpatched.detour", detours.Contains(M("UnpatchCancel")) ? 1 : 0);

            Hook.mode = Mode.All;
            H.Patch(M("UnpatchCancelThenOff"), prefix: Pre("Pre42"));
            Hook.mode = Mode.Off;
            Row("q3.thenoff.patched.behaviour", Targets.UnpatchCancelThenOff());
            Row("q3.thenoff.patched.detour", detours.Contains(M("UnpatchCancelThenOff")) ? 1 : 0);
            H.Unpatch(M("UnpatchCancelThenOff"), HarmonyPatchType.All, Id);
            Row("q3.thenoff.unpatched.behaviour", Targets.UnpatchCancelThenOff());
            Row("q3.thenoff.unpatched.detour", detours.Contains(M("UnpatchCancelThenOff")) ? 1 : 0);
        }

        private static void ReversePatches()
        {
            Hook.mode = Mode.Off;
            H.CreateReversePatcher(Src("RevControl"), new HarmonyMethod(Standin("Control"))).Patch();
            Row("q5.control.standin", Standins.Control());
            Row("q5.control.standin.detour", detours.Contains(Standin("Control")) ? 1 : 0);
            Row("q5.control.source.listed", Listed(Src("RevControl")) ? 1 : 0);

            Hook.mode = Mode.All;
            H.CreateReversePatcher(Src("RevBlanketBare"), new HarmonyMethod(Standin("BlanketBare"))).Patch();
            H.CreateClassProcessor(typeof(AttrDrivenReverse)).Patch();
            Hook.mode = Mode.Off;
            Row("q5.blanket.standin.bare", Standins.BlanketBare());
            Row("q5.blanket.standin.attrdriven", AttrDrivenReverse.Copy());

            Hook.mode = Mode.AttributeOnly;
            H.CreateReversePatcher(Src("RevAttrOnlyAttributed"), new HarmonyMethod(Standin("AttrOnlyAttributed"))).Patch();
            H.CreateReversePatcher(Src("RevAttrOnlyBare"), new HarmonyMethod(Standin("AttrOnlyBare"))).Patch();
            H.Patch(M("ForeignTarget"), prefix: Pre("Pre42"));
            Hook.mode = Mode.Off;
            Row("q5.attronly.standin.attributed", Standins.AttrOnlyAttributed());
            Row("q5.attronly.standin.bare", Standins.AttrOnlyBare());
            Row("q5.attronly.updatewrapper.target", Targets.ForeignTarget());

            Hook.Owned.Add(M("OwnedTarget"));
            Hook.Owned.Add(Src("RevOwnedBare"));
            Hook.mode = Mode.Owned;
            H.CreateReversePatcher(Src("RevOwnedBare"), new HarmonyMethod(Standin("OwnedBare"))).Patch();
            H.Patch(M("OwnedTarget"), prefix: Pre("Pre42"));
            Hook.mode = Mode.Off;
            Row("q5.owned.standin", Standins.OwnedBare());
            Row("q5.owned.updatewrapper.target", Targets.OwnedTarget());

            Row("q5.owned.standin.detour", detours.Contains(Standin("OwnedBare")) ? 1 : 0);
            Row("q5.owned.target.detour", detours.Contains(M("OwnedTarget")) ? 1 : 0);
        }

        private static void Frames()
        {
            var updateWrapperFrames = 0;
            var reversePatchFrames = 0;
            var unknownFrames = 0;
            foreach (var row in Hook.Trace)
            {
                var cols = row.Split('|');
                if (cols[3].EndsWith(".UpdateWrapper", StringComparison.Ordinal))
                {
                    updateWrapperFrames++;
                }
                else if (cols[3].EndsWith(".ReversePatch", StringComparison.Ordinal))
                {
                    reversePatchFrames++;
                }
                else
                {
                    unknownFrames++;
                }
            }

            Row("q5.frames.updatewrapper", updateWrapperFrames);
            Row("q5.frames.reversepatch", reversePatchFrames);
            Row("q5.frames.unresolved", unknownFrames);
            Row("q5.frames.total", Hook.Trace.Count);
        }

        private static void Leak()
        {
            Hook.mode = Mode.Off;
            H.Patch(M("LeakControl"), prefix: Pre("Pre42"));
            Row("q6.control.p1.behaviour", Targets.LeakControl());
            new Harmony(Id + ".control.second").Patch(M("LeakControl"), postfix: Pre("Post100"));
            Row("q6.control.p2.behaviour", Targets.LeakControl());
            Row("q6.control.p2.prefixes", Prefixes(M("LeakControl")));
            Row("q6.control.p2.postfixes", Postfixes(M("LeakControl")));

            H.Patch(M("Leak"), prefix: Pre("Pre42"));
            Row("q6.p1.behaviour", Targets.Leak());
            Row("q6.p1.detour", detours.Contains(M("Leak")) ? 1 : 0);
            var firstId = DetourId(M("Leak"));
            Row("q6.p1.detour.id.nonzero", firstId == 0 ? 0 : 1);
            Row("q6.p1.detours.count", detours.Count);

            Hook.mode = Mode.All;
            var second = new Harmony(Id + ".second");
            second.Patch(M("Leak"), postfix: Pre("Post100"));
            Hook.mode = Mode.Off;
            Row("q6.p2.behaviour", Targets.Leak());
            Row("q6.p2.prefixes", Prefixes(M("Leak")));
            Row("q6.p2.postfixes", Postfixes(M("Leak")));
            Row("q6.p2.owners", Owners(M("Leak")));
            Row("q6.p2.detour.id.unchanged", DetourId(M("Leak")) == firstId ? 1 : 0);
            Row("q6.p2.detours.count", detours.Count);

            var stale = detours[M("Leak")];
            Row("q6.stale.is.disposable", stale is IDisposable ? 1 : 0);
            ((IDisposable)stale).Dispose();
            Row("q6.after.dispose.behaviour", Targets.Leak());
            Row("q6.after.dispose.detour.key", detours.Contains(M("Leak")) ? 1 : 0);
            Row("q6.after.dispose.detour.id.unchanged", DetourId(M("Leak")) == firstId ? 1 : 0);
            Row("q6.after.dispose.prefixes", Prefixes(M("Leak")));
            Row("q6.after.dispose.postfixes", Postfixes(M("Leak")));

            try
            {
                var third = new Harmony(Id + ".third");
                third.Patch(M("Leak"), prefix: Pre("Pre42"));
                Row("q6.repatch.threw", "none");
                Row("q6.repatch.behaviour", Targets.Leak());
                Row("q6.repatch.detour.id.changed", DetourId(M("Leak")) == firstId ? 0 : 1);
            }
            catch (Exception ex)
            {
                Row("q6.repatch.threw", ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ').Replace('\t', ' '));
                Row("q6.repatch.behaviour", Targets.Leak());
                Row("q6.repatch.detour.id.changed", DetourId(M("Leak")) == firstId ? 0 : 1);
            }
        }

        private static int Main(string[] args)
        {
            var failure = Setup();
            if (failure != null)
            {
                Row("setup.failure", failure);
                return 3;
            }

            Gates();
            Bookkeeping();
            RealMethod();
            Unpatching();
            ReversePatches();
            Leak();
            Frames();

            Row("trace.rows", Hook.Trace.Count);
            for (var i = 0; i < Hook.Trace.Count; i++)
            {
                Row("trace." + i.ToString("D2"), Hook.Trace[i]);
            }

            Row("hook.hits.total", Hook.Hits);
            Row("hook.cancels.total", Hook.Cancels);
            Row("probe.complete", 1);
            return 0;
        }
    }
}
