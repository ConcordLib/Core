using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
#if NET10_0_OR_GREATER
using System.Reflection.Emit;
#endif
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Concord.Detour;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class ReentrantLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class ReentrantOuter
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            ReentrantLog.Entries.Add("outer-body");
            return 1;
        }
    }

    public static class ReentrantInner
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            ReentrantLog.Entries.Add("inner-body");
            return 2;
        }
    }

    public static class ReentrantBystander
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            return 3;
        }
    }

    public static class ReentrantHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Step(int x)
        {
            return x;
        }
    }

    public static class ReentrantIterator
    {
        public static IEnumerable<int> Items()
        {
            yield return ReentrantHelper.Step(1);
            yield return ReentrantHelper.Step(2);
        }
    }

    public static class ReentrantMods
    {
        public static Action? NestedPatch;

        public static IEnumerable<CodeInstruction> OuterTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            NestedPatch?.Invoke();
            return instructions;
        }

        public static void InnerPrefix()
        {
            ReentrantLog.Entries.Add("inner-prefix");
        }

        public static void BystanderPrefix()
        {
        }
    }

    [Collection("HarmonySerial")]
    public sealed class ReentrantBuildTests
    {
        private const string OuterId = "test.reentrant.outer";

        private const string InnerId = "test.reentrant.inner";

        private const string BystanderId = "test.reentrant.bystander";

        [Fact]
        public void HarmonyNestsASecondBuildOnOneThread_TheHookReportsBothAsIndependentBuilds()
        {
            MethodInfo outer = Fresh(typeof(ReentrantOuter), nameof(ReentrantOuter.Run));
            MethodInfo inner = Fresh(typeof(ReentrantInner), nameof(ReentrantInner.Run));
            HarmonyLib.Harmony outerMod = new HarmonyLib.Harmony(OuterId);
            HarmonyLib.Harmony innerMod = new HarmonyLib.Harmony(InnerId);
            Observer observer = new Observer(outer, inner);

            try
            {
                Assert.True(UpdateWrapperHook.TryInstall(observer, new MonoModDetourBackend(), observer.Record));
                ReentrantMods.NestedPatch = () => innerMod.Patch(inner, prefix: new HarmonyMethod(Method(nameof(ReentrantMods.InnerPrefix))));

                outerMod.Patch(outer, transpiler: new HarmonyMethod(Method(nameof(ReentrantMods.OuterTranspiler))));

                Assert.Equal(new[] { (MethodBase)outer, inner }, observer.Originals());
                Notice outerNotice = observer.Seen[0];
                Notice innerNotice = observer.Seen[1];
                Assert.Equal(Thread.CurrentThread.ManagedThreadId, outerNotice.Thread);
                Assert.Equal(Thread.CurrentThread.ManagedThreadId, innerNotice.Thread);
                Assert.True(outerNotice.HarmonyLockHeld);
                Assert.True(innerNotice.HarmonyLockHeld);
                Assert.False(outerNotice.OuterAlreadyRegistered);
                Assert.False(innerNotice.OuterAlreadyRegistered);

                Assert.Single(PatchProcessor.GetPatchInfo(outer).Transpilers);
                Assert.Single(PatchProcessor.GetPatchInfo(inner).Prefixes);

                ReentrantLog.Entries.Clear();
                Assert.Equal(1, ReentrantOuter.Run());
                Assert.Equal(2, ReentrantInner.Run());
                Assert.Equal(new[] { "outer-body", "inner-prefix", "inner-body" }, ReentrantLog.Entries);
            }
            finally
            {
                ReentrantMods.NestedPatch = null;
                UpdateWrapperHook.DetachObserver();
                TestUnpatch.Own(outerMod, OuterId);
                TestUnpatch.Own(innerMod, InnerId);
            }
        }

#if NET10_0_OR_GREATER
        [Fact]
        public void PersistentInfixOnADisposableIterator_NestsTheDisposeFinalizerBuild_AndTheHookHearsBoth()
        {
            Type stateMachine = IteratorStateMachine();
            MethodInfo moveNext = stateMachine.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new InvalidOperationException(stateMachine.Name + ".MoveNext not found");
            MethodInfo dispose = stateMachine.GetInterfaceMap(typeof(IDisposable)).TargetMethods[0];
            MethodInfo bystander = Fresh(typeof(ReentrantBystander), nameof(ReentrantBystander.Run));
            Assert.Null(PatchProcessor.GetPatchInfo(moveNext));
            Assert.Null(PatchProcessor.GetPatchInfo(dispose));

            HarmonyLib.Harmony mod = new HarmonyLib.Harmony(OuterId);
            HarmonyLib.Harmony competitorMod = new HarmonyLib.Harmony(BystanderId);
            Observer observer = new Observer(moveNext, dispose);
            Thread? competitor = null;
            bool competitorStarted = false;
            bool competitorDone = false;

            try
            {
                Assert.True(UpdateWrapperHook.TryInstall(observer, new MonoModDetourBackend(), observer.Record));
                observer.OnNested = () =>
                {
                    competitor = new Thread(() =>
                    {
                        Volatile.Write(ref competitorStarted, true);
                        competitorMod.Patch(bystander, prefix: new HarmonyMethod(Method(nameof(ReentrantMods.BystanderPrefix))));
                        Volatile.Write(ref competitorDone, true);
                    });
                    competitor.Start();
                    SpinWait.SpinUntil(() => Volatile.Read(ref competitorStarted), 5000);
                    Thread.Sleep(300);
                    return Volatile.Read(ref competitorDone);
                };

                PatchProcessor processor = mod.CreateProcessor(moveNext);
                MethodInfo step = typeof(ReentrantHelper).GetMethod(nameof(ReentrantHelper.Step)) ?? throw new InvalidOperationException(nameof(ReentrantHelper) + ".Step not found");
                AddInnerPrefix(processor, PersistentInfixPatch(), step);
                processor.Patch();

                Assert.Equal(new[] { (MethodBase)moveNext, dispose }, observer.Originals());
                Notice outerNotice = observer.Seen[0];
                Notice innerNotice = observer.Seen[1];
                Assert.Equal(Thread.CurrentThread.ManagedThreadId, innerNotice.Thread);
                Assert.True(outerNotice.HarmonyLockHeld);
                Assert.True(innerNotice.HarmonyLockHeld);
                Assert.False(innerNotice.OuterAlreadyRegistered);
                Assert.True(competitorStarted);
                Assert.False(innerNotice.CompetitorDoneWhenSeen);

                Patches disposePatches = PatchProcessor.GetPatchInfo(dispose);
                Assert.NotNull(disposePatches);
                Assert.True(disposePatches.Finalizers.Count == 1, Describe(disposePatches.Finalizers));
                Assert.Equal("Harmony.Infix.PersistentState", disposePatches.Finalizers[0].owner);

                Assert.NotNull(competitor);
                Assert.True(competitor.Join(10000));
                Assert.True(competitorDone);
                Assert.Single(PatchProcessor.GetPatchInfo(bystander).Prefixes);

                List<int> produced = new List<int>(ReentrantIterator.Items());
                Assert.Equal(new[] { 1, 2 }, produced);
            }
            finally
            {
                UpdateWrapperHook.DetachObserver();
                competitor?.Join(1000);
                TestUnpatch.Own(mod, OuterId);
                TestUnpatch.Own(competitorMod, BystanderId);
            }
        }
#endif

        [Fact]
        public void ConcordHarmony_NeverNamesPersistentState()
        {
            byte[] image = File.ReadAllBytes(typeof(HarmonyLockScope).Assembly.Location);

            Assert.False(Contains(image, Encoding.ASCII.GetBytes("PersistentState")), "Concord.Harmony references a PersistentState type by metadata");
            Assert.False(Contains(image, Encoding.Unicode.GetBytes("PersistentState")), "Concord.Harmony names PersistentState in a string literal");
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j])
                {
                    j++;
                }

                if (j == needle.Length)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(IReadOnlyList<Patch> patches)
        {
            StringBuilder text = new StringBuilder();
            foreach (Patch patch in patches)
            {
                text.Append(patch.owner).Append(':').Append(patch.PatchMethod.DeclaringType?.Name).Append('.').Append(patch.PatchMethod.Name).Append('#').Append(patch.index).Append(' ');
            }

            return text.ToString();
        }

        private static MethodInfo Fresh(Type owner, string name)
        {
            MethodInfo target = owner.GetMethod(name) ?? throw new InvalidOperationException(owner.Name + "." + name + " not found");
            Assert.Null(PatchProcessor.GetPatchInfo(target));
            return target;
        }

        private static MethodInfo Method(string name)
        {
            return typeof(ReentrantMods).GetMethod(name) ?? throw new InvalidOperationException(nameof(ReentrantMods) + "." + name + " not found");
        }

        private static Type IteratorStateMachine()
        {
            foreach (Type nested in typeof(ReentrantIterator).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (nested.Name.StartsWith("<Items>", StringComparison.Ordinal))
                {
                    return nested;
                }
            }

            throw new InvalidOperationException("the Items iterator has no compiler-generated state machine");
        }

#if NET10_0_OR_GREATER
        private static MethodInfo PersistentInfixPatch()
        {
            Assembly harmony = typeof(HarmonyLib.Harmony).Assembly;
            Type? argumentMode = harmony.GetType("HarmonyLib.ArgumentMode");
            Type? outerAttribute = harmony.GetType("HarmonyLib.HarmonyOuter");
            Assert.NotNull(argumentMode);
            Assert.NotNull(outerAttribute);
            ConstructorInfo? argumentCtor = typeof(HarmonyArgument).GetConstructor(new[] { typeof(string), argumentMode });
            Assert.NotNull(argumentCtor);

            string name = "Concord.Harmony.Tests.ReentrantInfix." + Guid.NewGuid().ToString("N");
            PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
            ModuleBuilder module = assembly.DefineDynamicModule(name);
            TypeBuilder type = module.DefineType("PersistentInfix", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            MethodBuilder method = type.DefineMethod("CountCalls", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] { typeof(int).MakeByRefType() });
            ParameterBuilder slot = method.DefineParameter(1, ParameterAttributes.None, "calls");
            ConstructorInfo outerCtor = outerAttribute.GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException("HarmonyOuter has no parameterless constructor");
            slot.SetCustomAttribute(new CustomAttributeBuilder(outerCtor, Array.Empty<object>()));
            slot.SetCustomAttribute(new CustomAttributeBuilder(argumentCtor, new[] { "calls", Enum.Parse(argumentMode, "Persistent") }));

            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldind_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stind_I4);
            il.Emit(OpCodes.Ret);
            type.CreateType();

            string path = Path.Combine(Path.GetTempPath(), name + ".dll");
            assembly.Save(path);
            Assembly loaded = Assembly.LoadFrom(path);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => args.Name.StartsWith(name, StringComparison.Ordinal) ? loaded : null;
            return loaded.GetType("PersistentInfix")?.GetMethod("CountCalls") ?? throw new InvalidOperationException("PersistentInfix.CountCalls not found");
        }

        private static void AddInnerPrefix(PatchProcessor processor, MethodInfo patch, MethodInfo innerCall)
        {
            Assembly harmony = typeof(HarmonyLib.Harmony).Assembly;
            Type? innerMethodType = harmony.GetType("HarmonyLib.InnerMethod");
            Assert.NotNull(innerMethodType);
            FieldInfo? innerMethodField = typeof(HarmonyMethod).GetField("innerMethod");
            Assert.NotNull(innerMethodField);
            MethodInfo? add = typeof(PatchProcessor).GetMethod("AddInnerPrefix", new[] { typeof(HarmonyMethod) });
            Assert.NotNull(add);

            HarmonyMethod infix = new HarmonyMethod(patch);
            innerMethodField.SetValue(infix, Activator.CreateInstance(innerMethodType, innerCall, Array.Empty<int>()));
            add.Invoke(processor, new object[] { infix });
        }
#endif

        private sealed class Notice
        {
            public Notice(MethodBase original)
            {
                Original = original;
            }

            public MethodBase Original { get; }

            public int Thread { get; set; }

            public bool HarmonyLockHeld { get; set; }

            public bool OuterAlreadyRegistered { get; set; }

            public bool CompetitorDoneWhenSeen { get; set; }
        }

        private sealed class Observer : IForeignPatchObserver
        {
            private readonly MethodBase outer;

            private readonly MethodBase nested;

            public Observer(MethodBase outer, MethodBase nested)
            {
                this.outer = outer;
                this.nested = nested;
            }

            public List<Notice> Seen { get; } = new List<Notice>();

            public List<string> Lines { get; } = new List<string>();

            public Func<bool>? OnNested { get; set; }

            public void Record(string line)
            {
                Lines.Add(line);
            }

            public MethodBase[] Originals()
            {
                MethodBase[] originals = new MethodBase[Seen.Count];
                for (int i = 0; i < originals.Length; i++)
                {
                    originals[i] = Seen[i].Original;
                }

                return originals;
            }

            public void OnForeignPatchPending(MethodBase target, object hostPatchState)
            {
                Notice notice = new Notice(target)
                {
                    Thread = System.Threading.Thread.CurrentThread.ManagedThreadId,
                    HarmonyLockHeld = HarmonyLockScope.Held,
                    OuterAlreadyRegistered = PatchProcessor.GetPatchInfo(outer) != null,
                };
                Seen.Add(notice);

                if (target.Equals(nested) && OnNested != null)
                {
                    notice.CompetitorDoneWhenSeen = OnNested();
                }
            }
        }
    }
}
