using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public static class TokenizableHost {
    public static int Counter;

    public static int Target(int seed) {
        return seed + Counter;
    }

    public static void Generic<T>() {
    }
}

public sealed class TokenizableGenericHost<T> {
    public T Value = default!;

    public static void Use(T value) {
    }
}

public sealed class TokenizableOperandsTests {
    private static readonly MethodBase TargetMethod = typeof(TokenizableHost).GetMethod(nameof(TokenizableHost.Target))!;

    [Fact]
    public void EncodableOperandKinds_AreAccepted() {
        List<CodeInstruction> stream = new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldstr, "text"),
            new CodeInstruction(OpCodes.Ldc_I4, 5),
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)3),
            new CodeInstruction(OpCodes.Ldc_I8, 7L),
            new CodeInstruction(OpCodes.Ldc_R4, 1.5f),
            new CodeInstruction(OpCodes.Ldc_R8, 2.5d),
            new CodeInstruction(OpCodes.Ldarg_S, (byte)1),
            new CodeInstruction(OpCodes.Ldloc, LocalRefFactory.Create(0, typeof(int))),
            new CodeInstruction(OpCodes.Br, LabelFactory.Create(1)),
            new CodeInstruction(OpCodes.Switch, new[] { LabelFactory.Create(1), LabelFactory.Create(2) }),
            new CodeInstruction(OpCodes.Call, TargetMethod),
            new CodeInstruction(OpCodes.Ldsfld, typeof(TokenizableHost).GetField(nameof(TokenizableHost.Counter))!),
            new CodeInstruction(OpCodes.Ldtoken, typeof(List<int>)),
            new CodeInstruction(OpCodes.Ret),
        };

        Assert.Null(TokenizableOperands.Reject(stream, TargetMethod));
    }

    [Fact]
    public void Calli_IsRefused() {
        string? rejection = Reject(new CodeInstruction(OpCodes.Calli, new CallSiteSignature(new object())));

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains("calli", rejection!);
        Assert.Contains("at instruction 1", rejection!);
        Assert.Contains("TokenizableHost.Target", rejection!);
    }

    [Fact]
    public void VarargCallSite_IsRefused() {
        MethodBase vararg = typeof(ForeignTargets.ForeignVarargTarget).GetMethod(nameof(ForeignTargets.ForeignVarargTarget.Vararg))!;
        Assert.Equal(CallingConventions.VarArgs, vararg.CallingConvention & CallingConventions.VarArgs);

        string? rejection = Reject(new CodeInstruction(OpCodes.Call, vararg));

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains("vararg", rejection!);
    }

    [Fact]
    public void Jmp_IsRefused() {
        string? rejection = Reject(new CodeInstruction(OpCodes.Jmp, TargetMethod));

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains("jmp", rejection!);
    }

    [Fact]
    public void DynamicMethodOperand_IsRefused() {
        DynamicMethod generated = new DynamicMethod("Dyn", typeof(void), Type.EmptyTypes);

        string? rejection = Reject(new CodeInstruction(OpCodes.Call, generated));

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains("dynamic method", rejection!);
    }

    [Fact]
    public void UnrecognizedOperand_IsRefused() {
        string? rejection = Reject(new CodeInstruction(OpCodes.Call, new object()));

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains("Object", rejection!);
    }

    [Fact]
    public void GenericInstantiationOverDynamicType_IsRefused() {
        Type dynamic = DynamicType();
        Type instantiation = typeof(List<>).MakeGenericType(dynamic);

        Assert.False(instantiation.Module.Assembly.IsDynamic);
        Assert.False(instantiation.Assembly.IsDynamic);

        string? rejection = Reject(new CodeInstruction(OpCodes.Ldtoken, instantiation));

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    [Fact]
    public void NestedGenericInstantiationOverDynamicType_IsRefused() {
        Type dynamic = DynamicType();
        Type nested = typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(List<>).MakeGenericType(dynamic).MakeArrayType());

        Assert.False(nested.Assembly.IsDynamic);

        string? rejection = Reject(new CodeInstruction(OpCodes.Ldtoken, nested));

        Assert.NotNull(rejection);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    [Fact]
    public void MethodOnGenericInstantiationOverDynamicType_IsRefused() {
        Type dynamic = DynamicType();
        Type instantiation = typeof(TokenizableGenericHost<>).MakeGenericType(dynamic);
        MethodBase method = instantiation.GetMethod(nameof(TokenizableGenericHost<int>.Use))!;

        Assert.False(method.Module.Assembly.IsDynamic);

        string? rejection = Reject(new CodeInstruction(OpCodes.Call, method));

        Assert.NotNull(rejection);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    [Fact]
    public void FieldOnGenericInstantiationOverDynamicType_IsRefused() {
        Type dynamic = DynamicType();
        Type instantiation = typeof(TokenizableGenericHost<>).MakeGenericType(dynamic);
        FieldInfo field = instantiation.GetField(nameof(TokenizableGenericHost<int>.Value))!;

        Assert.False(field.Module.Assembly.IsDynamic);

        string? rejection = Reject(new CodeInstruction(OpCodes.Ldfld, field));

        Assert.NotNull(rejection);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    [Fact]
    public void GenericMethodInstantiatedOverDynamicType_IsRefused() {
        Type dynamic = DynamicType();
        MethodBase method = typeof(TokenizableHost)
            .GetMethod(nameof(TokenizableHost.Generic))!
            .MakeGenericMethod(dynamic);

        Assert.False(method.Module.Assembly.IsDynamic);
        Assert.False(method.DeclaringType!.Assembly.IsDynamic);

        string? rejection = Reject(new CodeInstruction(OpCodes.Call, method));

        Assert.NotNull(rejection);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    [Fact]
    public void DeeplyNestedStaticGenerics_AreStillAccepted() {
        Type deep = typeof(int);
        for (int i = 0; i < 8; i++) {
            deep = typeof(List<>).MakeGenericType(deep);
        }

        Assert.Null(Reject(new CodeInstruction(OpCodes.Ldtoken, deep)));
    }

    [Fact]
    public void OpenGenericDefinition_IsStillAccepted() {
        Assert.Null(Reject(new CodeInstruction(OpCodes.Ldtoken, typeof(TokenizableGenericHost<>))));
        Assert.Null(Reject(new CodeInstruction(OpCodes.Ldtoken, typeof(List<>))));
    }

    [Fact]
    public void LocalTypedOverDynamicType_IsRefused() {
        Type dynamic = DynamicType();
        ComposedBody body = Body(
            new[] { new ComposedLocal(typeof(List<>).MakeGenericType(dynamic), false) },
            Array.Empty<ComposedClause>());

        Assert.Null(TokenizableOperands.Reject(InstructionsOf(body), TargetMethod));

        string? rejection = TokenizableOperands.Reject(body, TargetMethod);

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains("local 0", rejection!);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    [Fact]
    public void CatchTypeFromDynamicAssembly_IsRefused() {
        Type dynamic = DynamicType();
        ComposedBody body = Body(
            Array.Empty<ComposedLocal>(),
            new[] { new ComposedClause(ClauseKind.Catch, 0, 1, 1, 3, dynamic, -1) });

        Assert.Null(TokenizableOperands.Reject(InstructionsOf(body), TargetMethod));

        string? rejection = TokenizableOperands.Reject(body, TargetMethod);

        Assert.NotNull(rejection);
        Assert.StartsWith("CONC157: ", rejection!);
        Assert.Contains("clause 0", rejection!);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    [Fact]
    public void StaticLocalsAndClauses_AreAccepted() {
        ComposedBody body = Body(
            new[] { new ComposedLocal(typeof(Dictionary<string, List<int>>), false), new ComposedLocal(typeof(int).MakePointerType(), true) },
            new[] {
                new ComposedClause(ClauseKind.Catch, 0, 1, 1, 3, typeof(InvalidOperationException), -1),
                new ComposedClause(ClauseKind.Finally, 0, 1, 1, 3, null, -1),
            });

        Assert.Null(TokenizableOperands.Reject(body, TargetMethod));
    }

    [Fact]
    public void BodyOverload_StillRefusesOperands() {
        Type dynamic = DynamicType();
        ComposedBody body = new ComposedBody(
            new[] {
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldtoken, typeof(List<>).MakeGenericType(dynamic)), 0, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), 5, null),
            },
            Array.Empty<ComposedLocal>(),
            Array.Empty<ComposedClause>(),
            true);

        string? rejection = TokenizableOperands.Reject(body, TargetMethod);

        Assert.NotNull(rejection);
        Assert.Contains("at instruction 0", rejection!);
        Assert.Contains(dynamic.FullName!, rejection!);
    }

    private static ComposedBody Body(IReadOnlyList<ComposedLocal> locals, IReadOnlyList<ComposedClause> clauses) {
        return new ComposedBody(
            new[] {
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldarg_0), 0, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Pop), 1, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), 2, null),
            },
            locals,
            clauses,
            true);
    }

    private static List<CodeInstruction> InstructionsOf(ComposedBody body) {
        List<CodeInstruction> stream = new List<CodeInstruction>(body.Instructions.Count);
        foreach (ComposedInstruction instruction in body.Instructions) {
            stream.Add(instruction.Instruction);
        }

        return stream;
    }

    private static Type DynamicType() {
        AssemblyName name = new AssemblyName("ConcordTokenizableDynamic_" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(name.Name!);
        Type created = module.DefineType("DynamicOperandType", TypeAttributes.Public).CreateType()!;

        Assert.True(created.Assembly.IsDynamic);

        return created;
    }

    private static string? Reject(CodeInstruction offender) {
        List<CodeInstruction> stream = new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldarg_0),
            offender,
            new CodeInstruction(OpCodes.Ret),
        };

        return TokenizableOperands.Reject(stream, TargetMethod);
    }
}
