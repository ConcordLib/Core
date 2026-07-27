using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Harmony.Tests;

public class BridgeTargetRegistryTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StaticTarget()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OtherTarget()
    {
    }

    private static MethodBase TargetMethod()
    {
        return typeof(BridgeTargetRegistryTests).GetMethod(
            nameof(StaticTarget),
            BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static Injection MakeInjection(string owner, int priority = 0, IReadOnlyList<string> beforeOwners = null)
    {
        return new Injection(TargetMethod(), new InjectAt.Head(), owner, priority)
        {
            BeforeOwners = beforeOwners ?? [],
        };
    }

    [Fact]
    public void Add_ReturnsOneSeqPerInjection()
    {
        BridgeTargetRegistry registry = new BridgeTargetRegistry();
        MethodBase target = TargetMethod();
        Injection[] added = { MakeInjection("a"), MakeInjection("b") };

        long[] seqs = registry.Add(target, added);

        Assert.Equal(2, seqs.Length);
        Assert.NotEqual(seqs[0], seqs[1]);
    }

    [Fact]
    public void OrderedSnapshot_ReflectsPriorityAndBeforeOwnersOrder()
    {
        BridgeTargetRegistry registry = new BridgeTargetRegistry();
        MethodBase target = TargetMethod();

        Injection first = MakeInjection("first", beforeOwners: new[] { "second" });
        registry.Add(target, new[] { first });

        Injection second = MakeInjection("second");
        registry.Add(target, new[] { second });

        Injection[] snapshot = registry.OrderedSnapshot(target);

        Assert.Equal(2, snapshot.Length);
        Assert.Equal("second", snapshot[0].Owner);
        Assert.Equal("first", snapshot[1].Owner);
    }

    [Fact]
    public void DuplicateValueEqualInjections_RemovingOneCallsSeqsLeavesExactlyOneLiveRegistration()
    {
        BridgeTargetRegistry registry = new BridgeTargetRegistry();
        MethodBase target = TargetMethod();
        Injection injection = MakeInjection("dup");

        long[] firstSeqs = registry.Add(target, new[] { injection });
        registry.Add(target, new[] { injection });

        Assert.Equal(2, registry.OrderedSnapshot(target).Length);

        registry.Remove(target, firstSeqs);

        Assert.Single(registry.OrderedSnapshot(target));
    }

    [Fact]
    public void Remove_ReturnsRemovedPairs_RestoreReinstatesThemInOriginalOrder()
    {
        BridgeTargetRegistry registry = new BridgeTargetRegistry();
        MethodBase target = TargetMethod();

        Injection first = MakeInjection("first");
        Injection second = MakeInjection("second", beforeOwners: new[] { "first" });
        long[] seqs = registry.Add(target, new[] { first, second });

        Injection[] before = registry.OrderedSnapshot(target);

        (long Seq, Injection Injection)[] removed = registry.Remove(target, seqs);
        Assert.Equal(2, removed.Length);
        Assert.Empty(registry.OrderedSnapshot(target));

        registry.Restore(target, removed);

        Injection[] after = registry.OrderedSnapshot(target);
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].Owner, after[i].Owner);
        }
    }

    [Fact]
    public void OrderedSnapshot_IsolatesCallerFromLaterMutation()
    {
        BridgeTargetRegistry registry = new BridgeTargetRegistry();
        MethodBase target = TargetMethod();
        registry.Add(target, new[] { MakeInjection("a") });

        Injection[] snapshot = registry.OrderedSnapshot(target);
        registry.Add(target, new[] { MakeInjection("b") });

        Assert.Single(snapshot);
    }

    [Fact]
    public void Normalization_DistinctReflectedMethodInfoInstancesLandInOneEntry()
    {
        BridgeTargetRegistry registry = new BridgeTargetRegistry();
        MethodInfo canonical = TargetMethod() as MethodInfo;
        MethodBase distinctWrapper = new ProxyMethodInfo(canonical);

        Assert.NotSame(canonical, distinctWrapper);

        registry.Add(canonical, new[] { MakeInjection("a") });
        registry.Add(distinctWrapper, new[] { MakeInjection("b") });

        Assert.Equal(2, registry.OrderedSnapshot(canonical).Length);
        Assert.Equal(2, registry.OrderedSnapshot(distinctWrapper).Length);
        Assert.True(registry.HasInjections(canonical));
    }

    [Fact]
    public async Task ParallelHammer_AddSnapshotRemove_NoExceptionAndConsistentAfterward()
    {
        BridgeTargetRegistry registry = new BridgeTargetRegistry();
        MethodBase target = TargetMethod();

        const int threadCount = 8;
        const int iterations = 100;

        Task[] tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    Injection injection = MakeInjection("owner-" + threadIndex + "-" + i);
                    long[] seqs = registry.Add(target, new[] { injection });
                    Injection[] snapshot = registry.OrderedSnapshot(target);
                    Assert.NotNull(snapshot);
                    registry.Remove(target, seqs);
                }
            });
        }

        await Task.WhenAll(tasks);

        Assert.Empty(registry.OrderedSnapshot(target));
    }

    private sealed class ProxyMethodInfo : MethodInfo
    {
        private readonly MethodInfo inner;

        public ProxyMethodInfo(MethodInfo inner)
        {
            this.inner = inner;
        }

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => inner.ReturnTypeCustomAttributes;

        public override RuntimeMethodHandle MethodHandle => inner.MethodHandle;

        public override MethodAttributes Attributes => inner.Attributes;

        public override Type DeclaringType => inner.DeclaringType;

        public override string Name => inner.Name;

        public override Type ReflectedType => inner.ReflectedType;

        public override Type ReturnType => inner.ReturnType;

        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            return inner.GetMethodImplementationFlags();
        }

        public override ParameterInfo[] GetParameters()
        {
            return inner.GetParameters();
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            return inner.Invoke(obj, invokeAttr, binder, parameters, culture);
        }

        public override MethodInfo GetBaseDefinition()
        {
            return inner.GetBaseDefinition();
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            return inner.GetCustomAttributes(inherit);
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            return inner.GetCustomAttributes(attributeType, inherit);
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            return inner.IsDefined(attributeType, inherit);
        }
    }
}
