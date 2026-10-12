using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Harmony.Tests;

public static class StubStaticTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Sum(int left, string right)
    {
        return left + right.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Other(int left, string right)
    {
        return left - right.Length;
    }
}

public sealed class StubInstanceTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Bump(int by)
    {
        return by + 1;
    }
}

public static class StubRefTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Swap(ref int slot, out string label)
    {
        slot++;
        label = "x";
    }
}

public static class StubVoidTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Ping()
    {
    }
}

public static class StubGenericTarget<T>
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Echo(T value)
    {
        return value;
    }
}

public sealed class StubCtorTarget
{
    public StubCtorTarget(int seed)
    {
        Seed = seed;
    }

    public int Seed { get; }
}

public sealed class OriginalStubTests
{
    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static ConstructorInfo Ctor(Type type, Type[] parameters)
    {
        return type.GetConstructor(parameters) ?? throw new InvalidOperationException(type.Name + " constructor not found");
    }

    [Fact]
    public void For_TwoDifferentOriginals_MintTwoDifferentStubObjects()
    {
        MethodInfo first = Method(typeof(StubStaticTarget), nameof(StubStaticTarget.Sum));
        MethodInfo second = Method(typeof(StubStaticTarget), nameof(StubStaticTarget.Other));

        MethodInfo firstStub = OriginalStubs.For(first);
        MethodInfo secondStub = OriginalStubs.For(second);

        Assert.NotSame(firstStub, secondStub);
        Assert.NotEqual(firstStub.DeclaringType, secondStub.DeclaringType);
    }

    [Fact]
    public void For_SameOriginalTwice_ReturnsTheSameStubObject()
    {
        MethodInfo original = Method(typeof(StubStaticTarget), nameof(StubStaticTarget.Sum));

        MethodInfo firstAsk = OriginalStubs.For(original);
        MethodInfo secondAsk = OriginalStubs.For(Method(typeof(StubStaticTarget), nameof(StubStaticTarget.Sum)));

        Assert.Same(firstAsk, secondAsk);
    }

    [Fact]
    public void For_StaticTarget_SignatureMatchesParameterByParameter()
    {
        MethodInfo original = Method(typeof(StubStaticTarget), nameof(StubStaticTarget.Sum));

        MethodInfo stub = OriginalStubs.For(original);
        ParameterInfo[] parameters = stub.GetParameters();

        Assert.Equal(typeof(int), stub.ReturnType);
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.True(stub.IsStatic);
    }

    [Fact]
    public void For_EveryTargetShape_StubReflectedTypeEqualsDeclaringType()
    {
        MethodBase[] originals =
        {
            Method(typeof(StubStaticTarget), nameof(StubStaticTarget.Sum)),
            Method(typeof(StubInstanceTarget), nameof(StubInstanceTarget.Bump)),
            Method(typeof(StubRefTarget), nameof(StubRefTarget.Swap)),
            Method(typeof(StubVoidTarget), nameof(StubVoidTarget.Ping)),
            Method(typeof(StubGenericTarget<string>), nameof(StubGenericTarget<string>.Echo)),
            Ctor(typeof(StubCtorTarget), new[] { typeof(int) }),
        };

        foreach (MethodBase original in originals)
        {
            MethodInfo stub = OriginalStubs.For(original);
            Assert.Same(stub.DeclaringType, stub.ReflectedType);
        }
    }

    [Fact]
    public void For_InstanceTarget_StubTakesTheInstanceAsItsFirstParameter()
    {
        MethodInfo original = Method(typeof(StubInstanceTarget), nameof(StubInstanceTarget.Bump));

        MethodInfo stub = OriginalStubs.For(original);
        ParameterInfo[] parameters = stub.GetParameters();

        Assert.Equal(typeof(int), stub.ReturnType);
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(StubInstanceTarget), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
    }

    [Fact]
    public void For_RefAndOutTarget_StubKeepsBothByRef()
    {
        MethodInfo original = Method(typeof(StubRefTarget), nameof(StubRefTarget.Swap));

        MethodInfo stub = OriginalStubs.For(original);
        ParameterInfo[] parameters = stub.GetParameters();

        Assert.Equal(typeof(void), stub.ReturnType);
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int).MakeByRefType(), parameters[0].ParameterType);
        Assert.Equal(typeof(string).MakeByRefType(), parameters[1].ParameterType);
        Assert.False(parameters[0].IsOut);
        Assert.True(parameters[1].IsOut);
    }

    [Fact]
    public void For_VoidTarget_StubReturnsVoidWithNoParameters()
    {
        MethodInfo original = Method(typeof(StubVoidTarget), nameof(StubVoidTarget.Ping));

        MethodInfo stub = OriginalStubs.For(original);

        Assert.Equal(typeof(void), stub.ReturnType);
        Assert.Empty(stub.GetParameters());
    }

    [Fact]
    public void For_ClosedGenericTarget_StubUsesTheClosedArgument()
    {
        MethodInfo closed = Method(typeof(StubGenericTarget<string>), nameof(StubGenericTarget<string>.Echo));
        MethodInfo otherClosed = Method(typeof(StubGenericTarget<int>), nameof(StubGenericTarget<int>.Echo));

        MethodInfo stub = OriginalStubs.For(closed);
        MethodInfo otherStub = OriginalStubs.For(otherClosed);
        ParameterInfo[] parameters = stub.GetParameters();

        Assert.Equal(typeof(string), stub.ReturnType);
        ParameterInfo only = Assert.Single(parameters);
        Assert.Equal(typeof(string), only.ParameterType);

        Assert.NotSame(stub, otherStub);
        Assert.Equal(typeof(int), otherStub.ReturnType);
    }

    [Fact]
    public void For_ConstructorTarget_StubReturnsVoidAndTakesTheInstance()
    {
        ConstructorInfo original = Ctor(typeof(StubCtorTarget), new[] { typeof(int) });

        MethodInfo stub = OriginalStubs.For(original);
        ParameterInfo[] parameters = stub.GetParameters();

        Assert.Equal(typeof(void), stub.ReturnType);
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(StubCtorTarget), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
    }

    [Fact]
    public void For_InvokedStub_ThrowsNotSupportedNamingTheOriginal()
    {
        MethodInfo original = Method(typeof(StubVoidTarget), nameof(StubVoidTarget.Ping));

        MethodInfo stub = OriginalStubs.For(original);
        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(
            () => stub.Invoke(null, Array.Empty<object>()));

        NotSupportedException inner = Assert.IsType<NotSupportedException>(thrown.InnerException);
        Assert.Contains("StubVoidTarget.Ping", inner.Message);
    }
}
