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
    }

    public static class SupportMatrixTestInjections
    {
        public static void SimplePrefix()
        {
        }
    }

    internal sealed class GenericContainer<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Describe()
        {
            return typeof(T).Name;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int StaticCompute()
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
        public void AllowsConstructorWithAround()
        {
            MethodBase target = CtorMethod();
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Around(), "test", 0)
            };

            string reason = SupportMatrix.Validate(target, injections, null);
            Assert.Null(reason);
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

    public sealed class SupportMatrixStateMachineTests
    {
        // CollectingPatchApplier resolves a StateMachine injection to MoveNext before anything reaches
        // the bridge, so the declared method never arrives here carrying one. Nothing rejects the shape.
        [Fact]
        public void AcceptsAsyncEntryMethodWhenInjectionSelectsStateMachineBody()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.AsyncTarget));
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0) { Body = PatchBody.StateMachine }
            };

            string reason = SupportMatrix.Validate(target, injections, null);
            Assert.Null(reason);
        }

        // A Declared injection composes onto the same method Harmony streamed, so coexistence works.
        [Fact]
        public void AllowsAsyncEntryMethodWhenInjectionSelectsDeclaredBody()
        {
            MethodBase target = typeof(SupportMatrixTestTargets).GetMethod(nameof(SupportMatrixTestTargets.AsyncTarget));
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            Injection[] injections = new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            };

            string reason = SupportMatrix.Validate(target, injections, null);
            Assert.Null(reason);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class SupportMatrixSharedGenericTests
    {
        private static Injection[] HeadInjection()
        {
            MethodBase injectionMethod = typeof(SupportMatrixTestInjections).GetMethod(nameof(SupportMatrixTestInjections.SimplePrefix));
            return new Injection[]
            {
                new Injection(injectionMethod, new InjectAt.Head(), "test", 0)
            };
        }

        [Fact]
        public void TryRouteRejectsSharedReferenceTypeGenericInstantiationWithGenericContextInBody()
        {
            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            MethodBase target = typeof(GenericContainer<string>).GetMethod(nameof(GenericContainer<string>.Describe));

            ForeignRouteResult result = bridge.TryRoute(target, HeadInjection(), forceRoute: true);

            Assert.Equal(ForeignRouteKind.Rejected, result.Kind);
            Assert.Contains("generic", result.Reason.ToLower());
        }

        [Fact]
        public void ValidateRejectsSharedReferenceTypeGenericInstantiationWithGenericContextInBody()
        {
            MethodBase target = typeof(GenericContainer<string>).GetMethod(nameof(GenericContainer<string>.Describe));

            string reason = SupportMatrix.Validate(target, HeadInjection(), null);

            Assert.NotNull(reason);
            Assert.Contains("generic", reason.ToLower());
        }

        [Fact]
        public void ValidateRejectsSharedReferenceTypeGenericStaticMethod()
        {
            MethodBase target = typeof(GenericContainer<string>).GetMethod(nameof(GenericContainer<string>.StaticCompute));

            string reason = SupportMatrix.Validate(target, HeadInjection(), null);

            Assert.NotNull(reason);
            Assert.Contains("generic", reason.ToLower());
        }

        [Fact]
        public void ValidateAcceptsGuardableSharedReferenceTypeGenericInstantiation()
        {
            if (TestRuntime.IsNetFramework)
            {
                return;
            }

            MethodBase target = typeof(GenericContainer<string>).GetMethod(nameof(GenericContainer<string>.Compute));

            string reason = SupportMatrix.Validate(target, HeadInjection(), null);

            Assert.Null(reason);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class SupportMatrixRoutingTests
    {
        [Fact]
        public void TryRouteRejectsMisshapedConstructorAroundAtEmitLayer()
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
            Assert.Contains("CONC111", result.Reason);
        }

        [Fact]
        public void ApplyToRoutedRejectsMisshapedConstructorAroundAtEmitLayer()
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
            Assert.Contains("CONC111", ex.Message);
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
}
