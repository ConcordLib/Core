using System;
using System.Collections.Generic;
using System.Reflection;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    using CodeInstruction = HarmonyLib.CodeInstruction;

    public static class CtorAroundLog
    {
        public static List<string> Entries = new List<string>();
    }

    public class CtorAroundBase
    {
        protected CtorAroundBase(int seed)
        {
            CtorAroundLog.Entries.Add("base:" + seed);
        }
    }

    public sealed class CtorAroundTarget : CtorAroundBase
    {
        public int Seed;

        public CtorAroundTarget(int seed) : base(seed)
        {
            Seed = seed;
            CtorAroundLog.Entries.Add("body:" + seed);
        }
    }

    public sealed class CtorAroundPlainTarget
    {
        public int Seed;

        public CtorAroundPlainTarget(int seed)
        {
            Seed = seed;
            CtorAroundLog.Entries.Add("plainbody:" + seed);
        }
    }

    public sealed class CtorAroundSkipTarget
    {
        public int Seed;

        public CtorAroundSkipTarget(int seed)
        {
            Seed = seed;
            CtorAroundLog.Entries.Add("skipbody:" + seed);
        }
    }

#pragma warning disable CS0649
    public abstract class CtorAroundInstanceInjection
    {
        [InjectField("Seed")]
        private int seed;

        public void Wrap(int value, VoidOperation<int> original)
        {
            CtorAroundLog.Entries.Add("preSeed:" + seed);
            original.Invoke(value);
            CtorAroundLog.Entries.Add("postSeed:" + seed);
        }
    }
#pragma warning restore CS0649

    public sealed class CtorAroundGenericTarget<T>
    {
        public T Seed;

        public CtorAroundGenericTarget(T seed)
        {
            Seed = seed;
            CtorAroundLog.Entries.Add("genericbody:" + seed);
        }
    }

    public struct CtorAroundStructTarget
    {
        public int Seed;

        public CtorAroundStructTarget(int seed)
        {
            Seed = seed;
            CtorAroundLog.Entries.Add("structbody:" + seed);
        }
    }

    public static class CtorAroundForeignTranspiler
    {
        public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            ConstructorInfo baseCtor = typeof(CtorAroundBase).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == System.Reflection.Emit.OpCodes.Call
                    && ReferenceEquals(list[i].operand, baseCtor)
                    && i > 0)
                {
                    list[i - 1] = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_I4, 99);
                    break;
                }
            }

            return list;
        }
    }

    public static class CtorAroundGate
    {
        public static bool RunInvoke;
    }

    public sealed class CtorAroundGuardedTarget
    {
        public int Seed;

        public CtorAroundGuardedTarget(int seed)
        {
            Seed = seed;
            CtorAroundLog.Entries.Add("guardedbody:" + seed);
        }
    }

    public sealed class CtorAroundCctorTarget
    {
        public static int Value = 1;
    }

    public static class CtorAroundMods
    {
        public static void Wrap(int seed, VoidOperation<int> original)
        {
            CtorAroundLog.Entries.Add("pre");
            original.Invoke(seed);
            CtorAroundLog.Entries.Add("post");
        }

        public static void WrapMutating(int seed, VoidOperation<int> original)
        {
            CtorAroundLog.Entries.Add("pre");
            original.Invoke(seed * 2);
            CtorAroundLog.Entries.Add("post");
        }

        public static void ForeignPostfix()
        {
            CtorAroundLog.Entries.Add("foreignPostfix");
        }

        public static void WrapZeroInvoke(int seed, VoidOperation<int> original)
        {
            CtorAroundLog.Entries.Add("neverInvokes");
        }

        public static void WrapLoopInvoke(int seed, VoidOperation<int> original)
        {
            for (int i = 0; i < 2; i++)
            {
                original.Invoke(seed + i);
            }
        }

        public static void WrapGuardedInvoke(int seed, VoidOperation<int> original)
        {
            if (CtorAroundGate.RunInvoke)
            {
                original.Invoke(seed);
            }
        }

        public static void WrapCctor(Operation original)
        {
            original.Invoke();
        }

        public static bool ForeignPrefixSkip()
        {
            CtorAroundLog.Entries.Add("foreignPrefixSkip");
            return false;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class CtorAroundCoexistenceTests
    {
        private static void RouteRaw(MethodBase target, Injection injection, HarmonyLib.Harmony harmony)
        {
            TranspilerParticipant.Registry.Add(target, new[] { injection });
            harmony.Patch(target, transpiler: new HarmonyMethod(TranspilerParticipant.TranspileMethod) { priority = Priority.Last });
        }

        [Fact]
        public void SupportMatrix_NoLongerRejectsAroundOnConstructor()
        {
            ConstructorInfo target = typeof(CtorAroundTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.Wrap));
            Injection around = new Injection(wrapMethod, new InjectAt.Around(), "test.concord.ctoraround", 0);

            string reason = SupportMatrix.Validate(target, new[] { around }, null);

            Assert.Null(reason);
        }

        [Fact]
        public void Around_OnConstructorWithBaseCall_ComposesOnHarmonyStream_AndConstructsOnce()
        {
            ConstructorInfo target = typeof(CtorAroundTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.Wrap));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.raw");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.ForeignPostfix))));
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.ctoraround", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                CtorAroundTarget instance = new CtorAroundTarget(5);

                Assert.Equal(5, instance.Seed);
                Assert.Equal(new List<string> { "pre", "base:5", "body:5", "post", "foreignPostfix" }, CtorAroundLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.raw");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnPlainConstructor_ComposesOnHarmonyStream_MutatedArgumentReachesBody()
        {
            ConstructorInfo target = typeof(CtorAroundPlainTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.WrapMutating));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.plain");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.ctoraround.plain", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                CtorAroundPlainTarget instance = new CtorAroundPlainTarget(5);

                Assert.Equal(10, instance.Seed);
                Assert.Equal(new List<string> { "pre", "plainbody:10", "post" }, CtorAroundLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.plain");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnConstructor_WithForeignPrefixSkippingOriginal_LeavesObjectUninitialized()
        {
            ConstructorInfo target = typeof(CtorAroundSkipTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.Wrap));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.skip");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.ForeignPrefixSkip))));
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.ctoraround.skip", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                CtorAroundSkipTarget instance = new CtorAroundSkipTarget(5);

                Assert.Equal(new List<string> { "foreignPrefixSkip" }, CtorAroundLog.Entries);
                Assert.Equal(0, instance.Seed);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.skip");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnConstructor_ZeroInvokeSites_StreamRejectsWithConc112()
        {
            ConstructorInfo target = typeof(CtorAroundGuardedTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.WrapZeroInvoke));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.zero");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.zero", 0), harmony);

                ConcordEmitException failure = Assert.IsType<ConcordEmitException>(TranspilerParticipant.LastStreamFailure);
                Assert.Equal("CONC112", failure.Code);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.zero");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnConstructor_InvokeInLoop_StreamRejectsWithConc113()
        {
            ConstructorInfo target = typeof(CtorAroundGuardedTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.WrapLoopInvoke));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.loop");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.loop", 0), harmony);

                ConcordEmitException failure = Assert.IsType<ConcordEmitException>(TranspilerParticipant.LastStreamFailure);
                Assert.Equal("CONC113", failure.Code);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.loop");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnStaticConstructor_StreamRejectsWithConc114()
        {
            ConstructorInfo target = typeof(CtorAroundCctorTarget).TypeInitializer;
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.WrapCctor));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.cctor");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.cctor", 0), harmony);

                ConcordEmitException failure = Assert.IsType<ConcordEmitException>(TranspilerParticipant.LastStreamFailure);
                Assert.Equal("CONC114", failure.Code);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.cctor");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnConstructor_RuntimeSkippedInvoke_ThrowsInsteadOfHalfBuiltObject()
        {
            ConstructorInfo target = typeof(CtorAroundGuardedTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.WrapGuardedInvoke));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.guarded");
            TranspilerParticipant.LastStreamFailure = null;
            CtorAroundGate.RunInvoke = false;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.guarded", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                Assert.ThrowsAny<Exception>(() => new CtorAroundGuardedTarget(3));
                Assert.Empty(CtorAroundLog.Entries);
            }
            finally
            {
                CtorAroundGate.RunInvoke = false;
                TestUnpatch.Own(harmony, "test.ctoraround.guarded");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnConstructor_InstanceInjectionReadsThis_SeesZeroedFieldsBeforeInvoke()
        {
            ConstructorInfo target = typeof(CtorAroundPlainTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundInstanceInjection).GetMethod(nameof(CtorAroundInstanceInjection.Wrap));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.instance");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.instance", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                CtorAroundPlainTarget instance = new CtorAroundPlainTarget(11);

                Assert.Equal(11, instance.Seed);
                Assert.Equal(new List<string> { "preSeed:0", "plainbody:11", "postSeed:11" }, CtorAroundLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.instance");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnValueTypeGenericConstructor_ComposesOnHarmonyStream()
        {
            ConstructorInfo target = typeof(CtorAroundGenericTarget<int>).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.Wrap));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.generic");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.generic", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                CtorAroundGenericTarget<int> instance = new CtorAroundGenericTarget<int>(4);

                Assert.Equal(4, instance.Seed);
                Assert.Equal(new List<string> { "pre", "genericbody:4", "post" }, CtorAroundLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.generic");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnStructConstructor_ComposesOnHarmonyStream()
        {
            ConstructorInfo target = typeof(CtorAroundStructTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.Wrap));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.struct");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.struct", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                CtorAroundStructTarget instance = new CtorAroundStructTarget(6);

                Assert.Equal(6, instance.Seed);
                Assert.Equal(new List<string> { "pre", "structbody:6", "post" }, CtorAroundLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.struct");
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void Around_OnConstructor_ForeignTranspilerRewritesBaseCtorArgument_AroundStillWrapsBody()
        {
            ConstructorInfo target = typeof(CtorAroundTarget).GetConstructor(new[] { typeof(int) });
            MethodInfo wrapMethod = typeof(CtorAroundMods).GetMethod(nameof(CtorAroundMods.Wrap));
            MethodInfo foreignTranspiler = typeof(CtorAroundForeignTranspiler).GetMethod(nameof(CtorAroundForeignTranspiler.Rewrite));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.ctoraround.foreigntranspiler");
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(foreignTranspiler) { priority = Priority.High });
                RouteRaw(target, new Injection(wrapMethod, new InjectAt.Around(), "test.concord.foreigntranspiler", 0), harmony);

                Assert.Null(TranspilerParticipant.LastStreamFailure);

                CtorAroundLog.Entries.Clear();
                CtorAroundTarget instance = new CtorAroundTarget(5);

                Assert.Equal(5, instance.Seed);
                Assert.Equal(new List<string> { "pre", "base:99", "body:5", "post" }, CtorAroundLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.ctoraround.foreigntranspiler");
                TranspilerParticipant.Registry.Clear(target);
            }
        }
    }
}
