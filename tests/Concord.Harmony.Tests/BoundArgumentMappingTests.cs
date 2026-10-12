using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class BoundArgLog
    {
        public static List<string> Entries = new List<string>();

        public static int Seen;
    }

    public sealed class BoundArgTarget
    {
        public int Value = 7;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PrefixArgInt(int x)
        {
            BoundArgLog.Entries.Add("body");
            return (Value * 100) + x;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PostfixArgInt(int x)
        {
            BoundArgLog.Entries.Add("body");
            return (Value * 100) + x;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int InstancePrefixInt(int x)
        {
            BoundArgLog.Entries.Add("body");
            return (Value * 100) + x;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int InstancePostfixInt(int x)
        {
            BoundArgLog.Entries.Add("body");
            return (Value * 100) + x;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InstancePrefixVoid(int x)
        {
            BoundArgLog.Entries.Add("body");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string RefTypeArg(string s)
        {
            BoundArgLog.Entries.Add("body");
            return s + "b";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int RefusedShapes(int x)
        {
            BoundArgLog.Entries.Add("body");
            return (Value * 100) + x;
        }
    }

    public static class BoundArgStaticTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Twice(int x)
        {
            BoundArgLog.Entries.Add("body");
            return x * 2;
        }
    }

    public static class BoundArgPatches
    {
        public static void ReadsTargetArg(int x)
        {
            BoundArgLog.Seen = x;
            BoundArgLog.Entries.Add("pre");
        }

        public static void PostfixReadsTargetArg(int x)
        {
            BoundArgLog.Seen = x;
            BoundArgLog.Entries.Add("post");
        }

        public static void ReadsInstance(BoundArgTarget __instance)
        {
            BoundArgLog.Seen = __instance.Value;
            BoundArgLog.Entries.Add("pre");
        }

        public static void ReadsInstanceAndResult(BoundArgTarget __instance, ref int __result)
        {
            BoundArgLog.Seen = __instance.Value;
            BoundArgLog.Entries.Add("post:" + __result);
            __result += 1;
        }

        public static void ReadsInstanceVoidTarget(BoundArgTarget __instance)
        {
            BoundArgLog.Seen = __instance.Value;
            BoundArgLog.Entries.Add("pre");
        }

        public static void ReadsTargetArgAndResult(int x, ref int __result)
        {
            BoundArgLog.Seen = x;
            BoundArgLog.Entries.Add("post");
        }

        public static void ReadsTargetArgByRef(ref int x)
        {
            BoundArgLog.Seen = x;
            BoundArgLog.Entries.Add("pre");
        }

        public static void ReadsTargetArgWidened(object x)
        {
            BoundArgLog.Entries.Add("pre");
        }

        public static void ReadsRenamedTargetArg(int notTheTargetName)
        {
            BoundArgLog.Seen = notTheTargetName;
            BoundArgLog.Entries.Add("pre");
        }

        public static void ReadsRefTypeArg(string s)
        {
            BoundArgLog.Entries.Add("post:" + s);
        }

        public static void TakesState(ref object __state)
        {
            BoundArgLog.Entries.Add("state");
        }

        public static void TakesField(int ___Value)
        {
            BoundArgLog.Entries.Add("field");
        }

        public static void TakesArgs(object[] __args)
        {
            BoundArgLog.Entries.Add("args");
        }

        public static void TakesOriginalMethod(MethodBase __originalMethod)
        {
            BoundArgLog.Entries.Add("original");
        }

        public static void TakesRunOriginal(bool __runOriginal)
        {
            BoundArgLog.Entries.Add("runOriginal");
        }

        public static void TakesPositional(int __0)
        {
            BoundArgLog.Entries.Add("positional");
        }

        public static void TakesInstanceByRef(ref BoundArgTarget __instance)
        {
            BoundArgLog.Entries.Add("byref");
        }

        public static bool BoolPrefixReadingTargetArg(int x)
        {
            BoundArgLog.Seen = x;
            return true;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class BoundArgumentMappingTests
    {
        [Fact]
        public void Prefix_ReadingTargetArgument_MatchesLiveHarmony()
        {
            MethodInfo target = Method(typeof(BoundArgTarget), nameof(BoundArgTarget.PrefixArgInt));
            MethodInfo prefix = Patch(nameof(BoundArgPatches.ReadsTargetArg));

            Assert.Null(PrefixMapping.Validate(target, prefix));

            string viaHarmony = RunHarmonyInt(target, prefix, null, t => t.PrefixArgInt(4));
            string viaConcord = RunConcordInt(target, PrefixMapping.Map(target, prefix, "test.a"));

            Assert.Equal("ok:704|pre,body|seen=4", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void Postfix_ReadingTargetArgument_MatchesLiveHarmony()
        {
            MethodInfo target = Method(typeof(BoundArgTarget), nameof(BoundArgTarget.PostfixArgInt));
            MethodInfo postfix = Patch(nameof(BoundArgPatches.PostfixReadsTargetArg));

            Assert.Null(PostfixMapping.Validate(target, postfix));

            string viaHarmony = RunHarmonyInt(target, null, postfix, t => t.PostfixArgInt(4));
            string viaConcord = RunConcordInt(target, PostfixMapping.Map(target, postfix, "test.a", 400)[0]);

            Assert.Equal("ok:704|body,post|seen=4", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void Prefix_ReadingInstance_MatchesLiveHarmony()
        {
            MethodInfo target = Method(typeof(BoundArgTarget), nameof(BoundArgTarget.InstancePrefixInt));
            MethodInfo prefix = Patch(nameof(BoundArgPatches.ReadsInstance));

            Assert.Null(PrefixMapping.Validate(target, prefix));

            string viaHarmony = RunHarmonyInt(target, prefix, null, t => t.InstancePrefixInt(4));
            string viaConcord = RunConcordInt(target, PrefixMapping.Map(target, prefix, "test.a"));

            Assert.Equal("ok:704|pre,body|seen=7", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void Postfix_ReadingInstanceAndResult_MatchesLiveHarmony()
        {
            MethodInfo target = Method(typeof(BoundArgTarget), nameof(BoundArgTarget.InstancePostfixInt));
            MethodInfo postfix = Patch(nameof(BoundArgPatches.ReadsInstanceAndResult));

            Assert.Null(PostfixMapping.Validate(target, postfix));

            string viaHarmony = RunHarmonyInt(target, null, postfix, t => t.InstancePostfixInt(4));
            string viaConcord = RunConcordInt(target, PostfixMapping.Map(target, postfix, "test.a", 400)[0]);

            Assert.Equal("ok:705|body,post:704|seen=7", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void Prefix_ReadingInstance_OnVoidTarget_MatchesLiveHarmony()
        {
            MethodInfo target = Method(typeof(BoundArgTarget), nameof(BoundArgTarget.InstancePrefixVoid));
            MethodInfo prefix = Patch(nameof(BoundArgPatches.ReadsInstanceVoidTarget));

            Assert.Null(PrefixMapping.Validate(target, prefix));

            string viaHarmony = RunHarmonyVoid(target, prefix, t => t.InstancePrefixVoid(4));
            string viaConcord = RunConcordVoid(target, PrefixMapping.Map(target, prefix, "test.a"));

            Assert.Equal("ok|pre,body|seen=7", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void Prefix_TakingTargetArgumentByRef_IsRefused()
        {
            string? reason = PrefixMapping.Validate(Refused(), Patch(nameof(BoundArgPatches.ReadsTargetArgByRef)));

            Assert.Contains("'ref Int32 x'", reason);
            Assert.Contains("declares no parameter of that name and type", reason);
            Assert.Contains("by-ref-adapted", reason);
        }

        [Fact]
        public void Prefix_TakingReferenceTypeTargetArgument_IsRefusedAsAffectingTheOriginal()
        {
            MethodInfo target = Method(typeof(BoundArgTarget), nameof(BoundArgTarget.RefTypeArg));
            string? reason = PrefixMapping.Validate(target, Patch(nameof(BoundArgPatches.ReadsRefTypeArg)));

            Assert.Contains("affecting the original", reason);
            Assert.Contains("copied body runs unconditionally", reason);
        }

        [Fact]
        public void Postfix_TakingReferenceTypeTargetArgument_MatchesLiveHarmony()
        {
            MethodInfo target = Method(typeof(BoundArgTarget), nameof(BoundArgTarget.RefTypeArg));
            MethodInfo postfix = Patch(nameof(BoundArgPatches.ReadsRefTypeArg));

            Assert.Null(PostfixMapping.Validate(target, postfix));

            string viaHarmony = RunHarmonyRefType(target, postfix);
            string viaConcord = RunConcordRefType(target, PostfixMapping.Map(target, postfix, "test.a", 400)[0]);

            Assert.Equal("ok:ab|body,post:a|seen=0", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void Prefix_TakingWidenedTargetArgument_IsRefused()
        {
            string? reason = PrefixMapping.Validate(Refused(), Patch(nameof(BoundArgPatches.ReadsTargetArgWidened)));

            Assert.Contains("declares no parameter of that name and type", reason);
            Assert.Contains("widened", reason);
        }

        [Fact]
        public void Prefix_TakingRenamedTargetArgument_IsRefused()
        {
            string? reason = PrefixMapping.Validate(Refused(), Patch(nameof(BoundArgPatches.ReadsRenamedTargetArg)));

            Assert.Contains("declares no parameter of that name and type", reason);
        }

        [Fact]
        public void Postfix_TakingTargetArgumentAndResult_IsRefused()
        {
            MethodInfo postfix = Patch(nameof(BoundArgPatches.ReadsTargetArgAndResult));
            string? reason = PostfixMapping.Validate(Refused(), postfix);

            Assert.Contains("__result, the target's return value", reason);
            Assert.Contains("only to the target's own parameters", reason);
        }

        [Fact]
        public void Prefix_ReturningBoolWithTargetArgument_IsRefused()
        {
            string? reason = PrefixMapping.Validate(Refused(), Patch(nameof(BoundArgPatches.BoolPrefixReadingTargetArg)));

            Assert.Contains("returns 'Boolean'", reason);
        }

        [Fact]
        public void Prefix_TakingInstanceOnStaticTarget_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundArgStaticTarget), nameof(BoundArgStaticTarget.Twice));
            string? reason = PrefixMapping.Validate(target, Patch(nameof(BoundArgPatches.ReadsInstance)));

            Assert.Contains("is static, so there is no instance to bind", reason);
        }

        [Fact]
        public void Prefix_TakingInstanceByRef_IsRefused()
        {
            string? reason = PrefixMapping.Validate(Refused(), Patch(nameof(BoundArgPatches.TakesInstanceByRef)));

            Assert.Contains("__instance by reference", reason);
        }

        [Theory]
        [InlineData(nameof(BoundArgPatches.TakesState), "per-call slot shared with the same patch class")]
        [InlineData(nameof(BoundArgPatches.TakesField), "a field on the target's declaring type")]
        [InlineData(nameof(BoundArgPatches.TakesArgs), "every argument boxed into an array")]
        [InlineData(nameof(BoundArgPatches.TakesOriginalMethod), "the patched method itself")]
        [InlineData(nameof(BoundArgPatches.TakesRunOriginal), "whether a prefix has cancelled")]
        [InlineData(nameof(BoundArgPatches.TakesPositional), "a positional __N argument")]
        public void UnbindableParameter_IsRefusedWithItsOwnReason(string patchName, string expected)
        {
            MethodInfo patch = Patch(patchName);

            string? prefixReason = PrefixMapping.Validate(Refused(), patch);
            string? postfixReason = PostfixMapping.Validate(Refused(), patch);

            Assert.Contains(expected, prefixReason);
            Assert.Contains(expected, postfixReason);
            Assert.StartsWith("Prefix '", prefixReason);
            Assert.StartsWith("Postfix '", postfixReason);
        }

        [Fact]
        public void NoRefusal_NamesAComponentThatDoesNotExist()
        {
            string[] patchNames =
            {
                nameof(BoundArgPatches.ReadsTargetArgByRef),
                nameof(BoundArgPatches.ReadsTargetArgWidened),
                nameof(BoundArgPatches.ReadsRenamedTargetArg),
                nameof(BoundArgPatches.ReadsTargetArgAndResult),
                nameof(BoundArgPatches.TakesState),
                nameof(BoundArgPatches.TakesField),
                nameof(BoundArgPatches.TakesArgs),
                nameof(BoundArgPatches.TakesOriginalMethod),
                nameof(BoundArgPatches.TakesRunOriginal),
                nameof(BoundArgPatches.TakesPositional),
                nameof(BoundArgPatches.TakesInstanceByRef),
            };

            foreach (string patchName in patchNames)
            {
                MethodInfo patch = Patch(patchName);
                string? prefixReason = PrefixMapping.Validate(Refused(), patch);
                string? postfixReason = PostfixMapping.Validate(Refused(), patch);

                Assert.NotNull(prefixReason);
                Assert.NotNull(postfixReason);
                Assert.DoesNotContain("argument binder", prefixReason);
                Assert.DoesNotContain("argument binder", postfixReason);
            }
        }

        private static string RunHarmonyRefType(MethodInfo target, MethodInfo postfix)
        {
            string harmonyId = "test.boundarg." + target.Name;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                BoundArgTarget instance = new BoundArgTarget();
                return Record(() => "ok:" + instance.RefTypeArg("a"));
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static string RunConcordRefType(MethodInfo target, Injection injection)
        {
            Func<BoundArgTarget, string, string> wrapper = (Func<BoundArgTarget, string, string>)Compose(target, injection)
                .CreateDelegate(typeof(Func<BoundArgTarget, string, string>));
            BoundArgTarget instance = new BoundArgTarget();
            return Record(() => "ok:" + wrapper(instance, "a"));
        }

        private static MethodInfo Method(Type type, string name)
        {
            MethodInfo? found = type.GetMethod(name);
            Assert.NotNull(found);
            return found;
        }

        private static MethodInfo Refused()
        {
            return Method(typeof(BoundArgTarget), nameof(BoundArgTarget.RefusedShapes));
        }

        private static MethodInfo Patch(string name)
        {
            return Method(typeof(BoundArgPatches), name);
        }

        private static string RunHarmonyInt(MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, Func<BoundArgTarget, int> call)
        {
            string harmonyId = "test.boundarg." + target.Name;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(
                    target,
                    prefix: prefix == null ? null : new HarmonyMethod(prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(postfix));
                BoundArgTarget instance = new BoundArgTarget();
                return Record(() => "ok:" + call(instance));
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static string RunHarmonyVoid(MethodInfo target, MethodInfo prefix, Action<BoundArgTarget> call)
        {
            string harmonyId = "test.boundarg." + target.Name;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                BoundArgTarget instance = new BoundArgTarget();
                return Record(() =>
                {
                    call(instance);
                    return "ok";
                });
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static string RunConcordInt(MethodInfo target, Injection injection)
        {
            Func<BoundArgTarget, int, int> wrapper = (Func<BoundArgTarget, int, int>)Compose(target, injection)
                .CreateDelegate(typeof(Func<BoundArgTarget, int, int>));
            BoundArgTarget instance = new BoundArgTarget();
            return Record(() => "ok:" + wrapper(instance, 4));
        }

        private static string RunConcordVoid(MethodInfo target, Injection injection)
        {
            Action<BoundArgTarget, int> wrapper = (Action<BoundArgTarget, int>)Compose(target, injection)
                .CreateDelegate(typeof(Action<BoundArgTarget, int>));
            BoundArgTarget instance = new BoundArgTarget();
            return Record(() =>
            {
                wrapper(instance, 4);
                return "ok";
            });
        }

        private static MethodInfo Compose(MethodInfo target, Injection injection)
        {
            Injection[] ordered = InjectionOrderer.OrderForComposition(
                new List<(long Seq, Injection Injection)> { (0, injection) });
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            return composed.Wrapper;
        }

        private static string Record(Func<string> call)
        {
            BoundArgLog.Entries.Clear();
            BoundArgLog.Seen = 0;
            string outcome;
            try
            {
                outcome = call();
            }
            catch (Exception error)
            {
                outcome = "threw:" + error.GetType().Name + ":" + error.Message;
            }

            return outcome + "|" + string.Join(",", BoundArgLog.Entries) + "|seen=" + BoundArgLog.Seen;
        }
    }
}
