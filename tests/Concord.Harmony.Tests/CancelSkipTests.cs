using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class CancelSkipLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class CancelSkipTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Mixed(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ConcordOnly(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Observed(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Cancelling(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ReturnSetting(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ByRefArg(ref int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int BoundLabel(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int BoundType(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int StringArg(string s)
        {
            CancelSkipLog.Entries.Add("body");
            return s.Length * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ConcordSkipThenObserver(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }
    }

    public sealed class CancelSkipInstanceTarget
    {
        public int Value = 7;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int BoundLabel(int x)
        {
            CancelSkipLog.Entries.Add("body");
            return x * 2;
        }
    }

    public abstract class CancelSkipInstanceHead
    {
        [InjectInstance]
        protected abstract CancelSkipInstanceTarget Self { get; }

        public void ReadsBoundLabel(ControlHandle<int> control, [Bound] string label)
        {
            CancelSkipLog.Entries.Add("instance:" + label + ":" + Self.Value);
        }
    }

    public static class CancelSkipHarmonyMethods
    {
        public static void Observe()
        {
            CancelSkipLog.Entries.Add("foreign");
        }

        public static bool SkipWithResult(ref int __result)
        {
            CancelSkipLog.Entries.Add("foreign");
            __result = -100;
            return false;
        }
    }

    public static class CancelSkipConcordMethods
    {
        public static void CancelPlain(ControlHandle<int> control)
        {
            CancelSkipLog.Entries.Add("concordA");
            control.ReturnValue = 5;
            control.Cancel();
        }

        public static void CancelSkipping(ControlHandle<int> control)
        {
            CancelSkipLog.Entries.Add("concordA");
            control.ReturnValue = 5;
            control.Cancel(true);
        }

        public static void CancelSkippingWithoutReturn(ControlHandle<int> control)
        {
            CancelSkipLog.Entries.Add("concordA");
            control.Cancel(true);
        }

        public static void Second(ControlHandle<int> control)
        {
            CancelSkipLog.Entries.Add("concordB");
        }

        public static void Observer(ControlHandle<int> control)
        {
            CancelSkipLog.Entries.Add("observer");
        }

        public static void SetsReturn(ControlHandle<int> control)
        {
            CancelSkipLog.Entries.Add("setter");
            control.ReturnValue = 9;
        }

        public static void ReadsByRefArg(ref int x)
        {
            CancelSkipLog.Entries.Add("byref:" + x);
        }

        public static void ReadsBoundLabel(ControlHandle<int> control, [Bound] string label)
        {
            CancelSkipLog.Entries.Add("bound:" + label);
        }

        public static void ReadsBoundType(ControlHandle<int> control, [Bound] Type kind)
        {
            CancelSkipLog.Entries.Add("boundtype:" + kind.Name);
        }

        public static void ReadsStringArg(string s)
        {
            CancelSkipLog.Entries.Add("string:" + s);
        }
    }

    public delegate int ByRefArgCall(ref int x);

    [Collection("HarmonySerial")]
    public sealed class CancelSkipTests
    {
        private const int RunsFirst = 200;
        private const int RunsSecond = 600;

        [Fact]
        public void PlainCancel_WithForeignHeadPresent_LaterConcordHeadStillRuns()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.Mixed));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, Find(typeof(CancelSkipHarmonyMethods), nameof(CancelSkipHarmonyMethods.Observe)), "test.foreign")),
                (1, Head(nameof(CancelSkipConcordMethods.CancelPlain), "test.a", RunsFirst)),
                (2, Head(nameof(CancelSkipConcordMethods.Second), "test.b", RunsSecond)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            List<string> entries = Record(wrapper);

            Assert.Contains("concordA", entries);
            Assert.Contains("concordB", entries);
            Assert.DoesNotContain("body", entries);
        }

        [Fact]
        public void CancelSkippingHeads_WithForeignHeadPresent_LaterConcordHeadIsSkipped()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.Mixed));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, Find(typeof(CancelSkipHarmonyMethods), nameof(CancelSkipHarmonyMethods.Observe)), "test.foreign")),
                (1, Head(nameof(CancelSkipConcordMethods.CancelSkipping), "test.a", RunsFirst)),
                (2, Head(nameof(CancelSkipConcordMethods.Second), "test.b", RunsSecond)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            List<string> entries = Record(wrapper);

            Assert.Contains("concordA", entries);
            Assert.DoesNotContain("concordB", entries);
            Assert.DoesNotContain("body", entries);
        }

        [Fact]
        public void CancelSkippingHeads_WithNoForeignHead_LaterConcordHeadIsSkipped()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.ConcordOnly));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, Head(nameof(CancelSkipConcordMethods.CancelSkipping), "test.a", RunsFirst)),
                (1, Head(nameof(CancelSkipConcordMethods.Second), "test.b", RunsSecond)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            List<string> entries = Record(wrapper);

            Assert.Contains("concordA", entries);
            Assert.DoesNotContain("concordB", entries);
            Assert.DoesNotContain("body", entries);
        }

        [Theory]
        [InlineData("tail")]
        [InlineData("return")]
        [InlineData("catch")]
        public void CancelSkippingHeads_OutsideHead_ThrowsCONC171(string position)
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.ConcordOnly));
            InjectAt at = position switch
            {
                "tail" => new InjectAt.Tail(),
                "return" => new InjectAt.Return(0),
                _ => new InjectAt.Catch(),
            };
            Injection injection = new Injection(
                Find(typeof(CancelSkipConcordMethods), nameof(CancelSkipConcordMethods.CancelSkipping)), at, "test.a", 0);

            ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, new[] { injection }));

            Assert.Equal("CONC171", error.Code);
        }

        [Fact]
        public void CancelSkippingHeads_AtFinally_IsRefused()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.ConcordOnly));
            Injection injection = new Injection(
                Find(typeof(CancelSkipConcordMethods), nameof(CancelSkipConcordMethods.CancelSkipping)), new InjectAt.Finally(), "test.a", 0);

            ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, new[] { injection }));

            Assert.Equal("CONC138", error.Code);
        }

        [Fact]
        public void CancelSkippingHeads_OnNonVoidWithoutReturnValue_ThrowsCONC012()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.ConcordOnly));
            Injection injection = Head(nameof(CancelSkipConcordMethods.CancelSkippingWithoutReturn), "test.a", 0);

            ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, new[] { injection }));

            Assert.Equal("CONC012", error.Code);
        }

        [Fact]
        public void SkippingForeignPrefix_ObserverOnlyConcordHead_StillRuns()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.Observed));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, Head(nameof(CancelSkipConcordMethods.Observer), "test.a", RunsSecond)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            CancelSkipLog.Entries.Clear();
            int value = wrapper(4);

            Assert.Equal(-100, value);
            Assert.Contains("observer", CancelSkipLog.Entries);
            Assert.DoesNotContain("body", CancelSkipLog.Entries);
        }

        [Fact]
        public void SkippingForeignPrefix_CancellingConcordHead_IsSkipped()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.Cancelling));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, Head(nameof(CancelSkipConcordMethods.CancelPlain), "test.a", RunsSecond)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            List<string> entries = Record(wrapper);

            Assert.DoesNotContain("concordA", entries);
            Assert.DoesNotContain("body", entries);
        }

        [Fact]
        public void SkippingForeignPrefix_ReturnValueOnlyConcordHead_IsSkipped()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.ReturnSetting));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, Head(nameof(CancelSkipConcordMethods.SetsReturn), "test.a", RunsSecond)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            CancelSkipLog.Entries.Clear();
            int value = wrapper(4);

            Assert.Equal(-100, value);
            Assert.DoesNotContain("setter", CancelSkipLog.Entries);
            Assert.DoesNotContain("body", CancelSkipLog.Entries);
        }

        [Fact]
        public void SkippingForeignPrefix_ByRefArgConcordHead_IsSkipped()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.ByRefArg));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, Head(nameof(CancelSkipConcordMethods.ReadsByRefArg), "test.a", RunsSecond)),
            };

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            ByRefArgCall wrapper = (ByRefArgCall)composed.Wrapper.CreateDelegate(typeof(ByRefArgCall));

            CancelSkipLog.Entries.Clear();
            int argument = 4;
            int value = wrapper(ref argument);

            Assert.Equal(-100, value);
            Assert.DoesNotContain("byref:4", CancelSkipLog.Entries);
            Assert.DoesNotContain("body", CancelSkipLog.Entries);
        }

        [Fact]
        public void ConcordCancelSkip_ObserverOnlyLaterHead_IsSkipped()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.ConcordSkipThenObserver));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, Head(nameof(CancelSkipConcordMethods.CancelSkipping), "test.a", RunsFirst)),
                (1, Head(nameof(CancelSkipConcordMethods.Observer), "test.b", RunsSecond)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            List<string> entries = Record(wrapper);

            Assert.Contains("concordA", entries);
            Assert.DoesNotContain("observer", entries);
            Assert.DoesNotContain("body", entries);
        }

        [Fact]
        public void SkippingForeignPrefix_BoundStringConcordHead_StillRuns()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.BoundLabel));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, BoundHead(nameof(CancelSkipConcordMethods.ReadsBoundLabel), "test.a", "label", "tag")),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            CancelSkipLog.Entries.Clear();
            int value = wrapper(4);

            Assert.Equal(-100, value);
            Assert.Contains("bound:tag", CancelSkipLog.Entries);
            Assert.DoesNotContain("body", CancelSkipLog.Entries);
        }

        [Fact]
        public void SkippingForeignPrefix_BoundTypeConcordHead_StillRuns()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.BoundType));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, BoundHead(nameof(CancelSkipConcordMethods.ReadsBoundType), "test.a", "kind", typeof(CancelSkipLog))),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            CancelSkipLog.Entries.Clear();
            int value = wrapper(4);

            Assert.Equal(-100, value);
            Assert.Contains("boundtype:CancelSkipLog", CancelSkipLog.Entries);
            Assert.DoesNotContain("body", CancelSkipLog.Entries);
        }

        [Fact]
        public void SkippingForeignPrefix_BoundInstanceConcordHead_StillRuns()
        {
            MethodInfo target = Find(typeof(CancelSkipInstanceTarget), nameof(CancelSkipInstanceTarget.BoundLabel));
            MethodInfo head = Find(typeof(CancelSkipInstanceHead), nameof(CancelSkipInstanceHead.ReadsBoundLabel));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, new Injection(head, new InjectAt.Head(), "test.a", RunsSecond)
                {
                    BoundArguments = new Dictionary<string, object?> { ["label"] = "inst" },
                }),
            };

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Func<CancelSkipInstanceTarget, int, int> wrapper =
                (Func<CancelSkipInstanceTarget, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<CancelSkipInstanceTarget, int, int>));

            CancelSkipLog.Entries.Clear();
            int value = wrapper(new CancelSkipInstanceTarget(), 4);

            Assert.Equal(-100, value);
            Assert.Contains("instance:inst:7", CancelSkipLog.Entries);
            Assert.DoesNotContain("body", CancelSkipLog.Entries);
        }

        [Fact]
        public void SkippingForeignPrefix_ReferenceTargetArgConcordHead_IsSkipped()
        {
            MethodInfo target = Find(typeof(CancelSkipTargets), nameof(CancelSkipTargets.StringArg));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, SkippingPrefix(target)),
                (1, Head(nameof(CancelSkipConcordMethods.ReadsStringArg), "test.a", RunsSecond)),
            };

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Func<string, int> wrapper = (Func<string, int>)composed.Wrapper.CreateDelegate(typeof(Func<string, int>));

            CancelSkipLog.Entries.Clear();
            int value = wrapper("abcd");

            Assert.Equal(-100, value);
            Assert.DoesNotContain("string:abcd", CancelSkipLog.Entries);
            Assert.DoesNotContain("body", CancelSkipLog.Entries);
        }

        private static Injection BoundHead(string name, string owner, string parameter, object? value)
        {
            return new Injection(Find(typeof(CancelSkipConcordMethods), name), new InjectAt.Head(), owner, RunsSecond)
            {
                BoundArguments = new Dictionary<string, object?> { [parameter] = value },
            };
        }

        private static Injection SkippingPrefix(MethodInfo target)
        {
            return PrefixMapping.Map(target, Find(typeof(CancelSkipHarmonyMethods), nameof(CancelSkipHarmonyMethods.SkipWithResult)), "test.foreign");
        }

        private static Injection Head(string name, string owner, int priority)
        {
            return new Injection(Find(typeof(CancelSkipConcordMethods), name), new InjectAt.Head(), owner, priority);
        }

        private static MethodInfo Find(Type owner, string name)
        {
            MethodInfo? found = owner.GetMethod(name);
            Assert.NotNull(found);
            return found;
        }

        private static Func<int, int> ComposeInt(MethodInfo target, List<(long Seq, Injection Injection)> live)
        {
            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            return (Func<int, int>)composed.Wrapper.CreateDelegate(typeof(Func<int, int>));
        }

        private static List<string> Record(Func<int, int> wrapper)
        {
            CancelSkipLog.Entries.Clear();
            wrapper(4);
            return new List<string>(CancelSkipLog.Entries);
        }
    }
}
