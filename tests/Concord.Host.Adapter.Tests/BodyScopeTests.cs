using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class Crate<T> {
    public Crate() {
    }

    public int Get() {
        return 1;
    }

    public int Get(int n) {
        return n;
    }

    public static int Stat() {
        return 1;
    }

    public int Pick<TOther>() {
        return 1;
    }
}

public struct Cell<T> {
    public int Get() {
        return 1;
    }
}

public class Nest<T> {
    public class Inner {
        public int Foo() {
            return 1;
        }
    }
}

public class Plain {
    public int Body() {
        return 1;
    }

    public int Other() {
        return 1;
    }

    public int Pick<TOther>() {
        return 1;
    }
}

public class Root {
    public virtual int Inherited() {
        return 1;
    }
}

public class Leaf : Root {
    public override int Inherited() {
        return 2;
    }
}

public class Bystander : Root {
}

public class BodyScopeTests {
    private static MethodBase Get<T>() {
        return typeof(Crate<T>).GetMethod(nameof(Crate<object>.Get), Type.EmptyTypes)!;
    }

    [Theory]
    [MemberData(nameof(OneBody))]
    public void EveryShapeThatSharesOneBodyLandsOnOneKey(string label, MethodBase[] shapes) {
        MethodBase first = BodyScope.KeyOf(shapes[0]);

        for (int i = 1; i < shapes.Length; i++) {
            Assert.Equal(first, BodyScope.KeyOf(shapes[i]));
        }

        Assert.NotEqual(string.Empty, label);
    }

    public static TheoryData<string, MethodBase[]> OneBody() {
        return new TheoryData<string, MethodBase[]> {
            {
                "generic type, reference and value and open",
                [Get<string>(), Get<int>(), typeof(Crate<>).GetMethod(nameof(Crate<object>.Get), Type.EmptyTypes)!]
            },
            {
                "static on a generic type",
                [
                    typeof(Crate<int>).GetMethod(nameof(Crate<object>.Stat))!,
                    typeof(Crate<string>).GetMethod(nameof(Crate<object>.Stat))!,
                ]
            },
            {
                "constructor on a generic type",
                [typeof(Crate<int>).GetConstructors()[0], typeof(Crate<string>).GetConstructors()[0]]
            },
            {
                "generic method on a plain type",
                [
                    typeof(Plain).GetMethod(nameof(Plain.Pick))!.MakeGenericMethod(typeof(int)),
                    typeof(Plain).GetMethod(nameof(Plain.Pick))!.MakeGenericMethod(typeof(string)),
                    typeof(Plain).GetMethod(nameof(Plain.Pick))!,
                ]
            },
            {
                "generic method on a generic type",
                [
                    typeof(Crate<int>).GetMethod(nameof(Crate<object>.Pick))!.MakeGenericMethod(typeof(string)),
                    typeof(Crate<long>).GetMethod(nameof(Crate<object>.Pick))!.MakeGenericMethod(typeof(int)),
                    typeof(Crate<>).GetMethod(nameof(Crate<object>.Pick))!,
                ]
            },
            {
                "nested generic arguments",
                [
                    typeof(Crate<List<int>>).GetMethod(nameof(Crate<object>.Pick))!
                        .MakeGenericMethod(typeof(Dictionary<string, List<int>>)),
                    typeof(Crate<int>).GetMethod(nameof(Crate<object>.Pick))!.MakeGenericMethod(typeof(string)),
                ]
            },
            {
                "plain type nested in a generic outer",
                [
                    typeof(Nest<int>.Inner).GetMethod(nameof(Nest<int>.Inner.Foo))!,
                    typeof(Nest<string>.Inner).GetMethod(nameof(Nest<int>.Inner.Foo))!,
                ]
            },
            {
                "struct receiver",
                [typeof(Cell<int>).GetMethod(nameof(Cell<int>.Get))!, typeof(Cell<string>).GetMethod(nameof(Cell<int>.Get))!]
            },
            {
                "a generic type outside this assembly",
                [typeof(List<int>).GetMethod(nameof(List<int>.Add))!, typeof(List<string>).GetMethod(nameof(List<int>.Add))!]
            },
            {
                "one method reached through two reflected types",
                [typeof(Bystander).GetMethod(nameof(Root.Inherited))!, typeof(Root).GetMethod(nameof(Root.Inherited))!]
            },
        };
    }

    [Theory]
    [MemberData(nameof(SeparateBodies))]
    public void ShapesWithTheirOwnBodyKeepTheirOwnKey(string label, MethodBase left, MethodBase right) {
        Assert.NotEqual(BodyScope.KeyOf(left), BodyScope.KeyOf(right));
        Assert.NotEqual(string.Empty, label);
    }

    public static TheoryData<string, MethodBase, MethodBase> SeparateBodies() {
        return new TheoryData<string, MethodBase, MethodBase> {
            { "two methods on one generic type", Get<int>(), typeof(Crate<int>).GetMethod(nameof(Crate<object>.Stat))! },
            { "overloads", Get<int>(), typeof(Crate<int>).GetMethod(nameof(Crate<object>.Get), [typeof(int)])! },
            { "a constructor and a method", typeof(Crate<int>).GetConstructors()[0], Get<int>() },
            { "the same name on two generic types", Get<int>(), typeof(Cell<int>).GetMethod(nameof(Cell<int>.Get))! },
            { "an override and the method it overrides", typeof(Leaf).GetMethod(nameof(Root.Inherited))!, typeof(Root).GetMethod(nameof(Root.Inherited))! },
            { "two plain methods on one type", typeof(Plain).GetMethod(nameof(Plain.Body))!, typeof(Plain).GetMethod(nameof(Plain.Other))! },
            { "a generic method and a plain one beside it", typeof(Plain).GetMethod(nameof(Plain.Pick))!.MakeGenericMethod(typeof(int)), typeof(Plain).GetMethod(nameof(Plain.Body))! },
            { "a plain method and a generic instantiation", typeof(Plain).GetMethod(nameof(Plain.Body))!, Get<int>() },
        };
    }

    [Fact]
    public void TheKeyIsWiderThanTheOneCoreRoutesDetoursBy() {
        MethodBase number = Get<int>();
        MethodBase text = Get<string>();

        Assert.NotEqual(MethodIdentity.SharedBodyKey(number), MethodIdentity.SharedBodyKey(text));
        Assert.Equal(BodyScope.KeyOf(number), BodyScope.KeyOf(text));
    }

    [Theory]
    [MemberData(nameof(CoreKeepsApart))]
    public void CoreKeepsApartEveryShapeOneReplacementReaches(string label, MethodBase left, MethodBase right) {
        Assert.NotEqual(MethodIdentity.SharedBodyKey(left), MethodIdentity.SharedBodyKey(right));
        Assert.Equal(BodyScope.KeyOf(left), BodyScope.KeyOf(right));
        Assert.NotEqual(string.Empty, label);
    }

    public static TheoryData<string, MethodBase, MethodBase> CoreKeepsApart() {
        return new TheoryData<string, MethodBase, MethodBase> {
            { "two value instantiations", Get<int>(), Get<long>() },
            { "a value and a reference instantiation", Get<int>(), Get<string>() },
            { "a static on two instantiations", typeof(Crate<int>).GetMethod(nameof(Crate<object>.Stat))!, typeof(Crate<string>).GetMethod(nameof(Crate<object>.Stat))! },
            { "two generic method instantiations", typeof(Plain).GetMethod(nameof(Plain.Pick))!.MakeGenericMethod(typeof(int)), typeof(Plain).GetMethod(nameof(Plain.Pick))!.MakeGenericMethod(typeof(string)) },
            { "a struct receiver on two instantiations", typeof(Cell<int>).GetMethod(nameof(Cell<int>.Get))!, typeof(Cell<string>).GetMethod(nameof(Cell<int>.Get))! },
            { "a generic type outside this assembly", typeof(List<int>).GetMethod(nameof(List<int>.Add))!, typeof(List<string>).GetMethod(nameof(List<int>.Add))! },
        };
    }

    [Fact]
    public void AMethodReachedThroughADerivedTypeIsNotAlreadyEqualToItself() {
        MethodBase viaDerived = typeof(Bystander).GetMethod(nameof(Root.Inherited))!;
        MethodBase viaDeclaring = typeof(Root).GetMethod(nameof(Root.Inherited))!;

        Assert.NotEqual(viaDerived, viaDeclaring);
        Assert.Equal(typeof(Bystander), viaDerived.ReflectedType);
        Assert.Equal(BodyScope.KeyOf(viaDerived), BodyScope.KeyOf(viaDeclaring));
    }

    [Fact]
    public void AKeyedTableHoldsOneEntryForEveryInstantiation() {
        Dictionary<MethodBase, int> table = new Dictionary<MethodBase, int>();
        table[BodyScope.KeyOf(Get<int>())] = 1;
        table[BodyScope.KeyOf(Get<string>())] = 2;

        Assert.Single(table);
        Assert.Equal(2, table[BodyScope.KeyOf(typeof(Crate<>).GetMethod(nameof(Crate<object>.Get), Type.EmptyTypes)!)]);
    }

    [Fact]
    public void AMintedMethodHasNoDefinitionToCollapseTo() {
        DynamicMethod minted = new DynamicMethod("minted", typeof(int), Type.EmptyTypes);
        ILGenerator il = minted.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        Assert.Same(minted, BodyScope.KeyOf(minted));
    }

    [Fact]
    public void ANullTargetIsRefused() {
        Assert.Throws<ArgumentNullException>(() => BodyScope.KeyOf(null!));
    }
}
