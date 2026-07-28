#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    using CodeInstruction = HarmonyLib.CodeInstruction;

    public class SupportMatrixTestTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SimpleTarget()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static async void AsyncTarget()
        {
            await System.Threading.Tasks.Task.Delay(0);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerator<int> IteratorTarget()
        {
            yield return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void GetExecutingAssemblyCaller()
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
        }
    }

    public static class SupportMatrixTestInjections
    {
        public static void SimplePrefix()
        {
        }

        public static void GetExecutingAssemblyInjection()
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
        }
    }

    internal sealed class GenericContainer<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute()
        {
            return 1;
        }
    }

    public sealed class SupportMatrixCtorTests
    {
        private static MethodBase CtorMethod()
        {
            return typeof(object).GetConstructor(Type.EmptyTypes);
        }

        [Fact]
        public void RejectsConstructorWithAround()
        {
            MethodBase target = CtorMethod();
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Around(), "test", 0)
            };

            string reason = SupportMatrix.Validate(target, injections, null);
            Assert.NotNull(reason);
            Assert.Contains("Constructor", reason);
            Assert.Contains("Around", reason);
        }

        [Fact]
        public void AllowsConstructorWithHead()
        {
            MethodBase target = CtorMethod();
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            };

            string reason = SupportMatrix.Validate(target, injections, null);
            Assert.Null(reason);
        }
    }

    public sealed class SupportMatrixInnerPatchesTests
    {
        [Fact]
        public void HasInnerPatchesReturnsFalseForNull()
        {
            bool result = SupportMatrix.HasInnerPatches(null);
            Assert.False(result);
        }

        [Fact]
        public void HasInnerPatchesFalseForNormalTarget()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.SimpleTarget));
            Patches patchInfo = PatchProcessor.GetPatchInfo(target);

            string reason = SupportMatrix.Validate(target, new Injection[0], patchInfo);
            Assert.Null(reason);

            bool hasInner = SupportMatrix.HasInnerPatches(patchInfo);
            Assert.False(hasInner);
        }
    }

    public sealed class SupportMatrixStateMachineTests
    {
        [Fact]
        public void RejectsAsyncEntryMethod()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.AsyncTarget));
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            };

            string reason = SupportMatrix.Validate(target, injections, null);
            Assert.NotNull(reason);
            Assert.Contains("async", reason.ToLower());
        }
    }

    public sealed class SupportMatrixGetExecutingAssemblyTests
    {
        [Fact]
        public void CallsGetExecutingAssemblyDetectsCall()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.GetExecutingAssemblyCaller));
            bool calls = SupportMatrix.CallsGetExecutingAssembly(target);
            Assert.True(calls);
        }

        [Fact]
        public void CallsGetExecutingAssemblyDetectsNonCall()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.SimpleTarget));
            bool calls = SupportMatrix.CallsGetExecutingAssembly(target);
            Assert.False(calls);
        }

        [Fact]
        public void RejectsInjectionCallingGetExecutingAssembly()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.SimpleTarget));
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.GetExecutingAssemblyInjection));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            };

            string reason = SupportMatrix.Validate(target, injections, null);
            Assert.NotNull(reason);
            Assert.Contains("GetExecutingAssembly", reason);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class SupportMatrixUnknownOpCodeTests
    {
        [Fact]
        public void CallsGetExecutingAssemblyFailsClosedOnUnknownOpCode()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.SimpleTarget));
            FieldInfo tableField = typeof(SupportMatrix).GetField("OpCodesByValue", BindingFlags.NonPublic | BindingFlags.Static);
            Dictionary<short, OpCode> table = (Dictionary<short, OpCode>)tableField.GetValue(null);

            short retValue = OpCodes.Ret.Value;
            OpCode removed = table[retValue];
            table.Remove(retValue);

            try
            {
                bool calls = SupportMatrix.CallsGetExecutingAssembly(target);
                Assert.True(calls);
            }
            finally
            {
                table[retValue] = removed;
            }
        }
    }

    [Collection("HarmonySerial")]
    public sealed class SupportMatrixSharedGenericTests
    {
        [Fact]
        public void TryRouteRejectsSharedReferenceTypeGenericInstantiation()
        {
            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            MethodBase target = typeof(GenericContainer<string>).GetMethod(nameof(GenericContainer<string>.Compute));
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            };

            ForeignRouteResult result = bridge.TryRoute(target, injections, forceRoute: true);

            Assert.Equal(ForeignRouteKind.Rejected, result.Kind);
            Assert.Contains("generic", result.Reason.ToLower());
        }

        [Fact]
        public void ValidateRejectsSharedReferenceTypeGenericInstantiation()
        {
            MethodBase target = typeof(GenericContainer<string>).GetMethod(nameof(GenericContainer<string>.Compute));
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            };

            string reason = SupportMatrix.Validate(target, injections, null);

            Assert.NotNull(reason);
            Assert.Contains("generic", reason.ToLower());
        }
    }

    [Collection("HarmonySerial")]
    public sealed class SupportMatrixRoutingTests
    {
        [Fact]
        public void TryRouteRejectsConstructorAround()
        {
            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            MethodBase target = typeof(object).GetConstructor(Type.EmptyTypes);
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Around(), "test", 0)
            };

            ForeignRouteResult result = bridge.TryRoute(target, injections, forceRoute: true);
            Assert.Equal(ForeignRouteKind.Rejected, result.Kind);
            Assert.Contains("Constructor", result.Reason);
        }

        [Fact]
        public void ApplyToRoutedRejectsConstructorAround()
        {
            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            MethodBase target = typeof(object).GetConstructor(Type.EmptyTypes);
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));

            bridge.TryRoute(target, new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            }, forceRoute: true);

            Injection[] additionalInjections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Around(), "test", 0)
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyToRouted(target, additionalInjections));
            Assert.Contains("Constructor", ex.Message);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class SupportMatrixStreamRejectionTests
    {
        [Fact]
        public void StreamRejectionPassesThroughInstructions()
        {
            List<string> logs = new List<string>();
            TranspilerParticipant.Log = msg => logs.Add(msg);
            TranspilerParticipant.LastStreamFailure = null;

            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.SimpleTarget));
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            TranspilerParticipant.Registry.Add(target, new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            });

            System.Reflection.Emit.ILGenerator gen = new System.Reflection.Emit.DynamicMethod("t", typeof(void), System.Type.EmptyTypes).GetILGenerator();

            List<CodeInstruction> instructions = new List<CodeInstruction>
            {
                new CodeInstruction(System.Reflection.Emit.OpCodes.Ldstr) { operand = new System.Text.StringBuilder() }
            };
            List<CodeInstruction> originalInstructions = new List<CodeInstruction>(instructions);

            IEnumerable<CodeInstruction> result = TranspilerParticipant.Transpile(instructions, target, gen);
            List<CodeInstruction> resultList = new List<CodeInstruction>(result);

            Assert.Equal(originalInstructions.Count, resultList.Count);
            for (int i = 0; i < originalInstructions.Count; i++)
            {
                Assert.Equal(originalInstructions[i].opcode, resultList[i].opcode);
            }

            Assert.NotNull(TranspilerParticipant.LastStreamFailure);
            Assert.Contains(CoexistenceLogMarkers.StreamRejected, logs[0]);

            TranspilerParticipant.Log = null;
            TranspilerParticipant.LastStreamFailure = null;
            TranspilerParticipant.Registry.Clear(target);
        }
    }

    public sealed class SupportMatrixInnerPatchesPresentTests
    {
        [Fact]
        public void HasInnerPatchesReturnsTrueWhenInnerPrefixesNonEmpty()
        {
            MethodInfo innerPatchMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Patch innerPatch = new Patch(innerPatchMethod, 0, "test.inner.owner", 0, null, null, false);
            Patches patchInfo = new Patches(null, null, null, null, new Patch[] { innerPatch }, null);

            bool hasInner = SupportMatrix.HasInnerPatches(patchInfo);
            Assert.True(hasInner);

            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.SimpleTarget));
            string reason = SupportMatrix.Validate(target, new Injection[0], patchInfo);
            Assert.NotNull(reason);
            Assert.Contains("inner", reason.ToLower());
        }

        [Fact]
        public void HasInnerPatchesReturnsTrueWhenInnerPostfixesNonEmpty()
        {
            MethodInfo innerPatchMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Patch innerPatch = new Patch(innerPatchMethod, 0, "test.inner.owner", 0, null, null, false);
            Patches patchInfo = new Patches(null, null, null, null, null, new Patch[] { innerPatch });

            bool hasInner = SupportMatrix.HasInnerPatches(patchInfo);
            Assert.True(hasInner);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class SupportMatrixApplyToRoutedGetExecutingAssemblyTests
    {
        [Fact]
        public void ApplyToRoutedRejectsInjectionCallingGetExecutingAssembly()
        {
            MethodInfo target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.SimpleTarget));
            MethodInfo headMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            MethodInfo getExecutingAssemblyMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.GetExecutingAssemblyInjection));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new Injection[]
                {
                    new Injection(headMethod, new InjectAt.Head(), "test.route.first", 0)
                }, forceRoute: true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                Injection[] additionalInjections = new Injection[]
                {
                    new Injection(getExecutingAssemblyMethod, new InjectAt.Head(), "test.route.second", 0)
                };

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    bridge.ApplyToRouted(target, additionalInjections));
                Assert.Contains("GetExecutingAssembly", ex.Message);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }
    }
}
