using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class BinderLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class BinderTargets
    {
        public static int Field;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TwoInts(int first, int second)
        {
            BinderLog.Entries.Add("body:" + first + ":" + second);
            return first + second;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TakesText(string text)
        {
            BinderLog.Entries.Add("body:" + text);
            return text.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ByRefArg(ref int count)
        {
            BinderLog.Entries.Add("body:" + count);
            count += 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TakesBox(BinderBox box)
        {
            BinderLog.Entries.Add("body:" + box.Value);
        }
    }

    public struct BinderBox
    {
        public int Value;

        public void Bump()
        {
            Value += 1;
        }
    }

    public static class BinderPatches
    {
        public static void StaticCallOnOwnTypeWritesIt(int second)
        {
            int.TryParse("7", out second);
        }

        public static void ReadsSecond(int second)
        {
            BinderLog.Entries.Add("patch:" + second);
        }

        public static void ReadsRenamed(int other)
        {
            BinderLog.Entries.Add("patch:" + other);
        }

        public static void ReadsText(string text)
        {
            BinderLog.Entries.Add("patch:" + text);
        }

        public static void ReadsBothByRef(ref int count)
        {
            BinderLog.Entries.Add("patch:" + count);
            count += 10;
        }

        public static void WidenedArgument(object text)
        {
            BinderLog.Entries.Add("patch:" + text);
        }

        public static void ByRefAgainstByValue(ref int second)
        {
            second = 0;
        }

        public static void MutatesAStructArgument(BinderBox box)
        {
            box.Bump();
        }

        public static void AssignsItsOwnArgument(int second)
        {
            second = -1;
            BinderLog.Entries.Add("patch:" + second);
        }

        public static void PassesItsOwnArgumentByRefToAHelper(int second)
        {
            Zero(ref second);
        }

        private static void Zero(ref int value)
        {
            value = 0;
        }

        public static void TakesInstance(object __instance)
        {
        }

        public static void TakesResult(int __result)
        {
        }

        public static void TakesState(object __state)
        {
        }

        public static void TakesField(int ___Field)
        {
        }

        public static void TakesPositional(int __0)
        {
        }

        public static void TakesArgs(object[] __args)
        {
        }

        public static void TakesOriginalMethod(MethodBase __originalMethod)
        {
        }

        public static void TakesRunOriginal(bool __runOriginal)
        {
        }

        public static void TakesException(Exception __exception)
        {
        }

        public static void TakesResultRef(int __resultRef)
        {
        }

        public static void TakesHarmonyArgument([HarmonyArgument("second")] int renamed)
        {
        }

        [HarmonyArgument("second", "renamed")]
        public static void RenamedForTheWholeMethod(int renamed)
        {
        }

        public static bool CancellingPrefix(int second)
        {
            return false;
        }

        public static int PassThroughPostfix(int second)
        {
            return second;
        }

        public static void NoParameters()
        {
        }
    }

    public sealed class BinderInstancePatch
    {
        public void InstanceMethod(int second)
        {
        }
    }

    [HarmonyArgument("second", "renamed")]
    public static class BinderClassLevelHarmonyArgumentPatches
    {
        public static void RenamedByTheClass(int renamed)
        {
        }
    }

    [Collection("HarmonySerial")]
    public sealed class HarmonyArgumentBinderTests
    {
        [Fact]
        public void TargetArgumentByNameAndType_BindsTheCallersValue()
        {
            MethodInfo target = Target(nameof(BinderTargets.TwoInts));
            MethodInfo patch = Patch(nameof(BinderPatches.ReadsSecond));

            Assert.True(HarmonyArgumentBinder.CanCopyBody(target, patch, HarmonyPatchRole.Postfix, out string? refusal), refusal);

            Func<int, int, int> wrapper = ComposeTwoInts(target, patch, new InjectAt.Return(0));

            BinderLog.Entries.Clear();
            int result = wrapper(3, 40);

            Assert.Equal(43, result);
            Assert.Equal("body:3:40,patch:40", string.Join(",", BinderLog.Entries));
        }

        [Fact]
        public void UnmatchedParameterName_ReadsTheWrongArgument_WhichIsWhyItIsRefused()
        {
            MethodInfo target = Target(nameof(BinderTargets.TwoInts));
            MethodInfo patch = Patch(nameof(BinderPatches.ReadsRenamed));

            Assert.False(HarmonyArgumentBinder.CanCopyBody(target, patch, HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("declares no parameter of that name and type", refusal);

            Func<int, int, int> wrapper = ComposeTwoInts(target, patch, new InjectAt.Return(0));

            BinderLog.Entries.Clear();
            wrapper(3, 40);

            Assert.Equal("body:3:40,patch:3", string.Join(",", BinderLog.Entries));
        }

        [Fact]
        public void ReferenceTypeArgument_BindsOnAPostfix()
        {
            MethodInfo target = Target(nameof(BinderTargets.TakesText));
            MethodInfo patch = Patch(nameof(BinderPatches.ReadsText));

            Assert.True(HarmonyArgumentBinder.CanCopyBody(target, patch, HarmonyPatchRole.Postfix, out string? refusal), refusal);

            ComposeResult composed = WrapperComposer.Compose(target, new[] { Direct(patch, new InjectAt.Return(0)) });
            Assert.Empty(composed.Rejected);
            Func<string, int> wrapper = (Func<string, int>)composed.Wrapper.CreateDelegate(typeof(Func<string, int>));

            BinderLog.Entries.Clear();
            int result = wrapper("abcd");

            Assert.Equal(4, result);
            Assert.Equal("body:abcd,patch:abcd", string.Join(",", BinderLog.Entries));
        }

        [Fact]
        public void ByRefArgumentMatchingByRefTarget_BindsAndWritesThrough()
        {
            MethodInfo target = Target(nameof(BinderTargets.ByRefArg));
            MethodInfo patch = Patch(nameof(BinderPatches.ReadsBothByRef));

            Assert.True(HarmonyArgumentBinder.CanCopyBody(target, patch, HarmonyPatchRole.Postfix, out string? refusal), refusal);

            ComposeResult composed = WrapperComposer.Compose(target, new[] { Direct(patch, new InjectAt.Return(0)) });
            Assert.Empty(composed.Rejected);
            ByRefCall wrapper = (ByRefCall)composed.Wrapper.CreateDelegate(typeof(ByRefCall));

            BinderLog.Entries.Clear();
            int count = 5;
            wrapper(ref count);

            Assert.Equal(16, count);
            Assert.Equal("body:5,patch:6", string.Join(",", BinderLog.Entries));
        }

        [Fact]
        public void ValueTypeArgumentOnAVoidPrefix_Binds()
        {
            MethodInfo target = Target(nameof(BinderTargets.TwoInts));
            MethodInfo patch = Patch(nameof(BinderPatches.ReadsSecond));

            Assert.True(HarmonyArgumentBinder.CanCopyBody(target, patch, HarmonyPatchRole.Prefix, out string? refusal), refusal);

            Func<int, int, int> wrapper = ComposeTwoInts(target, patch, new InjectAt.Head());

            BinderLog.Entries.Clear();
            wrapper(3, 40);

            Assert.Equal("patch:40,body:3:40", string.Join(",", BinderLog.Entries));
        }

        [Fact]
        public void ReferenceTypeArgumentOnAPrefix_IsRefusedBecauseHarmonySkipsItAfterACancel()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TakesText)), Patch(nameof(BinderPatches.ReadsText)), HarmonyPatchRole.Prefix, out string? refusal));
            Assert.Contains("skips it once an earlier prefix has cancelled", refusal);
        }

        [Fact]
        public void WidenedArgumentType_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TakesText)), Patch(nameof(BinderPatches.WidenedArgument)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("declares no parameter of that name and type", refusal);
        }

        [Fact]
        public void ByRefAgainstAByValueTarget_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.ByRefAgainstByValue)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("declares no parameter of that name and type", refusal);
        }

        [Fact]
        public void AssigningAByValueArgument_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.AssignsItsOwnArgument)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("assigns to or takes the address of 'second'", refusal);
        }

        [Fact]
        public void TakingTheAddressOfAPrimitiveArgumentToPassItByRef_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.PassesItsOwnArgumentByRefToAHelper)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("assigns to or takes the address of 'second'", refusal);
        }

        [Fact]
        public void AStaticCallOnTheArgumentsOwnTypeThatWritesIt_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.StaticCallOnOwnTypeWritesIt)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("assigns to or takes the address of 'second'", refusal);
        }

        [Fact]
        public void MethodLevelHarmonyArgument_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.RenamedForTheWholeMethod)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("[HarmonyArgument] is refused", refusal);
        }

        [Fact]
        public void ClassLevelHarmonyArgument_IsRefused()
        {
            MethodInfo patch = Method(typeof(BinderClassLevelHarmonyArgumentPatches), nameof(BinderClassLevelHarmonyArgumentPatches.RenamedByTheClass));
            Assert.False(HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), patch, HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("[HarmonyArgument] is refused", refusal);
        }

        [Fact]
        public void TakingTheAddressOfAStructArgument_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TakesBox)), Patch(nameof(BinderPatches.MutatesAStructArgument)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("assigns to or takes the address of 'box'", refusal);
        }

        [Fact]
        public void CancellingPrefix_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.CancellingPrefix)), HarmonyPatchRole.Prefix, out string? refusal));
            Assert.Contains("returns 'Boolean'", refusal);
        }

        [Fact]
        public void PassThroughPostfix_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.PassThroughPostfix)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("returns 'Int32'", refusal);
        }

        [Fact]
        public void Finalizer_IsRefused()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.ReadsSecond)), HarmonyPatchRole.Finalizer, out string? refusal));
            Assert.Contains("catch protocol", refusal);
        }

        [Fact]
        public void NoParameters_IsLeftToTheShimMapping()
        {
            Assert.False(
                HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), Patch(nameof(BinderPatches.NoParameters)), HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("takes no parameters", refusal);
        }

        [Fact]
        public void InstanceMethod_IsRefused()
        {
            MethodInfo patch = Method(typeof(BinderInstancePatch), nameof(BinderInstancePatch.InstanceMethod));

            Assert.False(HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), patch, HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains("is an instance method", refusal);
        }

        [Theory]
        [InlineData(nameof(BinderPatches.TakesInstance), "Instance", "__instance")]
        [InlineData(nameof(BinderPatches.TakesResult), "Result", "__result")]
        [InlineData(nameof(BinderPatches.TakesResultRef), "ResultRef", "__resultRef")]
        [InlineData(nameof(BinderPatches.TakesState), "State", "__state")]
        [InlineData(nameof(BinderPatches.TakesException), "Exception", "__exception")]
        [InlineData(nameof(BinderPatches.TakesField), "Field", "___field")]
        [InlineData(nameof(BinderPatches.TakesPositional), "Positional", "__N")]
        [InlineData(nameof(BinderPatches.TakesArgs), "ArgsArray", "__args")]
        [InlineData(nameof(BinderPatches.TakesOriginalMethod), "OriginalMethod", "__originalMethod")]
        [InlineData(nameof(BinderPatches.TakesRunOriginal), "RunOriginal", "__runOriginal")]
        [InlineData(nameof(BinderPatches.TakesHarmonyArgument), "Renamed", "[HarmonyArgument]")]
        public void EveryKindConcordCannotBind_IsClassifiedAndNamedInTheRefusal(string patchName, string kind, string token)
        {
            MethodInfo patch = Patch(patchName);

            Assert.Equal(kind, HarmonyArgumentBinder.KindOf(patch.GetParameters()[0]).ToString());

            Assert.False(HarmonyArgumentBinder.CanCopyBody(Target(nameof(BinderTargets.TwoInts)), patch, HarmonyPatchRole.Postfix, out string? refusal));
            Assert.Contains(token + " is refused", refusal);
        }

        [Fact]
        public void TargetArgumentIsTheOnlyKindClassifiedAsBindable()
        {
            MethodInfo patch = Patch(nameof(BinderPatches.ReadsSecond));

            Assert.Equal("TargetArgument", HarmonyArgumentBinder.KindOf(patch.GetParameters()[0]).ToString());
        }

        [Fact]
        public void HarmonyBindsTheSameArgumentValue()
        {
            MethodInfo target = Target(nameof(BinderTargets.TwoInts));
            string id = "test.binder.parity";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(id);
            BinderLog.Entries.Clear();
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(Patch(nameof(BinderPatches.ReadsSecond))));
                BinderTargets.TwoInts(3, 40);
            }
            finally
            {
                TestUnpatch.Own(harmony, id);
            }

            Assert.Equal("body:3:40,patch:40", string.Join(",", BinderLog.Entries));
        }

        private delegate void ByRefCall(ref int count);

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        private static MethodInfo Target(string name)
        {
            return Method(typeof(BinderTargets), name);
        }

        private static MethodInfo Patch(string name)
        {
            return Method(typeof(BinderPatches), name);
        }

        private static Injection Direct(MethodInfo patch, InjectAt at)
        {
            return new Injection(patch, at, "test.binder", 0) { ForeignOrigin = true };
        }

        private static Func<int, int, int> ComposeTwoInts(MethodInfo target, MethodInfo patch, InjectAt at)
        {
            ComposeResult composed = WrapperComposer.Compose(target, new[] { Direct(patch, at) });
            Assert.Empty(composed.Rejected);
            return (Func<int, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<int, int, int>));
        }
    }
}
