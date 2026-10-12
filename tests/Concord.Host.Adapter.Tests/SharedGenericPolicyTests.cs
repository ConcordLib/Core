using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class Box<T> {
    public Box() {
    }

    public int Get() {
        return 1;
    }

    public static int Stat() {
        return 1;
    }

    public int Pick<TOther>() {
        return 1;
    }

    public List<T> Make() {
        return new List<T>();
    }
}

public struct Slot<T> {
    public int Get() {
        return 1;
    }
}

public class Holder<T> {
    public class Inner {
        public int Foo() {
            return 1;
        }
    }
}

public class Ungeneric {
    public int Body() {
        return 1;
    }

    public int Pick<TOther>() {
        return 1;
    }
}

public class SharedGenericPolicyTests {
    private static MethodBase Closed<T>(string name) {
        return typeof(Box<T>).GetMethod(name)!;
    }

    [Fact]
    public void ANonGenericTargetIsReplaced() {
        TargetVerdict verdict = SharedGenericPolicy.Judge(typeof(Ungeneric).GetMethod(nameof(Ungeneric.Body))!);

        Assert.Equal(TargetHandling.Replace, verdict.Handling);
        Assert.Equal(string.Empty, verdict.Message);
    }

    [Theory]
    [MemberData(nameof(ClosedInstantiations))]
    public void AClosedInstantiationFallsBackToTheDetour(string label, MethodBase target) {
        TargetVerdict verdict = SharedGenericPolicy.Judge(target);

        Assert.Equal(TargetHandling.Detour, verdict.Handling);
        Assert.StartsWith(TargetEligibility.FallbackMarker, verdict.Message, StringComparison.Ordinal);
        Assert.Contains("shares one IL body", verdict.Message, StringComparison.Ordinal);
        Assert.Contains(SharedGenericPolicy.TypicalDefinition(target).Name, verdict.Message, StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, label);
    }

    public static TheoryData<string, MethodBase> ClosedInstantiations() {
        return new TheoryData<string, MethodBase> {
            { "reference instantiation", Closed<string>(nameof(Box<object>.Get)) },
            { "value instantiation", Closed<int>(nameof(Box<object>.Get)) },
            { "static on a generic type", Closed<int>(nameof(Box<object>.Stat)) },
            { "constructor on a generic type", typeof(Box<int>).GetConstructors()[0] },
            { "generic method on a plain type", typeof(Ungeneric).GetMethod(nameof(Ungeneric.Pick))!.MakeGenericMethod(typeof(int)) },
            { "generic method on a generic type", typeof(Box<int>).GetMethod(nameof(Box<object>.Pick))!.MakeGenericMethod(typeof(string)) },
            { "struct receiver", typeof(Slot<int>).GetMethod(nameof(Slot<int>.Get))! },
            { "plain type nested in a generic", typeof(Holder<int>.Inner).GetMethod(nameof(Holder<int>.Inner.Foo))! },
        };
    }

    [Theory]
    [MemberData(nameof(OpenDefinitions))]
    public void AnOpenDefinitionIsRefused(string label, MethodBase target) {
        TargetVerdict verdict = SharedGenericPolicy.Judge(target);

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Equal(SharedGenericPolicy.OpenDefinitionCode, verdict.Code);
        Assert.Contains("no instantiation was named", verdict.Message, StringComparison.Ordinal);

        ConcordEmitException thrown = Assert.Throws<ConcordEmitException>(verdict.ThrowIfRefused);
        Assert.Equal(SharedGenericPolicy.OpenDefinitionCode, thrown.Code);
        Assert.NotEqual(string.Empty, label);
    }

    public static TheoryData<string, MethodBase> OpenDefinitions() {
        return new TheoryData<string, MethodBase> {
            { "open generic type", typeof(Box<>).GetMethod(nameof(Box<object>.Get))! },
            { "generic method definition", typeof(Ungeneric).GetMethod(nameof(Ungeneric.Pick))! },
            { "both open", typeof(Box<>).GetMethod(nameof(Box<object>.Pick))! },
            { "plain method on an open outer", typeof(Holder<>).GetNestedType(nameof(Holder<int>.Inner))!.GetMethod(nameof(Holder<int>.Inner.Foo))! },
        };
    }

    [Fact]
    public void TheTypicalDefinitionCollapsesEveryInstantiation() {
        MethodBase text = SharedGenericPolicy.TypicalDefinition(Closed<string>(nameof(Box<object>.Get)));
        MethodBase number = SharedGenericPolicy.TypicalDefinition(Closed<int>(nameof(Box<object>.Get)));
        MethodBase open = SharedGenericPolicy.TypicalDefinition(typeof(Box<>).GetMethod(nameof(Box<object>.Get))!);

        Assert.Equal(open, text);
        Assert.Equal(open, number);
        Assert.True(open.ContainsGenericParameters);
    }

    [Fact]
    public void TheTypicalDefinitionCollapsesGenericMethodsOnGenericTypes() {
        MethodBase one = SharedGenericPolicy.TypicalDefinition(
            typeof(Box<int>).GetMethod(nameof(Box<object>.Pick))!.MakeGenericMethod(typeof(string)));
        MethodBase other = SharedGenericPolicy.TypicalDefinition(
            typeof(Box<long>).GetMethod(nameof(Box<object>.Pick))!.MakeGenericMethod(typeof(int)));

        Assert.Equal(one, other);
        Assert.Equal(typeof(Box<>).GetMethod(nameof(Box<object>.Pick))!, one);
    }

    [Fact]
    public void ADynamicMethodHasNoTypicalDefinitionToResolve() {
        DynamicMethod minted = new DynamicMethod("minted", typeof(int), Type.EmptyTypes);
        ILGenerator il = minted.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        Assert.Same(minted, SharedGenericPolicy.TypicalDefinition(minted));
        Assert.Equal(TargetHandling.Replace, SharedGenericPolicy.Judge(minted).Handling);
    }

    [Fact]
    public void SharedBodyKeyIsFinerThanTheBodySlotItWouldWriteTo() {
        MethodBase number = Closed<int>(nameof(Box<object>.Get));
        MethodBase wider = Closed<long>(nameof(Box<object>.Get));

        Assert.NotEqual(MethodIdentity.SharedBodyKey(number), MethodIdentity.SharedBodyKey(wider));
        Assert.Equal(SharedGenericPolicy.TypicalDefinition(number), SharedGenericPolicy.TypicalDefinition(wider));
    }

    [Fact]
    public void ACaseTheCoreGuardCoversStillFallsBack() {
        MethodBase text = Closed<string>(nameof(Box<object>.Get));

        Assert.True(WrapperComposer.SharesGenericBody(text));
        Assert.Equal(TargetHandling.Detour, SharedGenericPolicy.Judge(text).Handling);
    }

    [Fact]
    public void ACaseTheCoreGuardSeesNoHazardInStillSharesTheBodySlot() {
        MethodBase number = Closed<int>(nameof(Box<object>.Get));
        MethodBase text = Closed<string>(nameof(Box<object>.Get));

        Assert.False(WrapperComposer.SharesGenericBody(number));
        Assert.Equal(SharedGenericPolicy.TypicalDefinition(text), SharedGenericPolicy.TypicalDefinition(number));
        Assert.Equal(TargetHandling.Detour, SharedGenericPolicy.Judge(number).Handling);
    }

    [Fact]
    public void ABodyThatNamesItsTypeArgumentResolvesDifferentlyPerInstantiation() {
        MethodInfo text = (MethodInfo)Closed<string>(nameof(Box<object>.Make));
        MethodInfo number = (MethodInfo)Closed<int>(nameof(Box<object>.Make));

        Assert.Equal(typeof(List<string>), text.ReturnType);
        Assert.Equal(typeof(List<int>), number.ReturnType);
        Assert.False(WrapperComposer.SharesGenericBody(text));
    }
}
