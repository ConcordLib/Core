using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;

namespace Concord.Host.Adapter.Tests;

internal sealed class EmittedMethod {
    internal EmittedMethod(byte[] image) {
        Image = image;
    }

    internal byte[] Image { get; }

    internal int HeaderMaxStack => ReadBody().MaxStack;

    internal bool HeaderIsFat {
        get {
            using PEReader reader = new PEReader(ImmutableArray.Create(Image));
            MetadataReader metadata = reader.GetMetadataReader();
            MethodDefinition definition = metadata.GetMethodDefinition(metadata.MethodDefinitions.Single());
            byte first = reader.GetSectionData(definition.RelativeVirtualAddress).GetReader().ReadByte();
            return (first & 0x3) == 0x3;
        }
    }

    internal object? Invoke() {
        Assembly assembly = Assembly.Load(Image);
        MethodInfo method = assembly.GetType("C")!.GetMethod("M")!;
        try {
            return method.Invoke(null, null);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null) {
            throw wrapped.InnerException;
        }
    }

    private MethodBodyBlock ReadBody() {
        using PEReader reader = new PEReader(ImmutableArray.Create(Image));
        MetadataReader metadata = reader.GetMetadataReader();
        MethodDefinition definition = metadata.GetMethodDefinition(metadata.MethodDefinitions.Single());
        return reader.GetMethodBody(definition.RelativeVirtualAddress);
    }
}

internal static class TestIl {
    internal static EmittedMethod Emit(ComposedBody body, int maxStack, bool usesLocalloc, bool returnsInt32) {
        string name = "p4t5_" + Guid.NewGuid().ToString("N");
        MetadataBuilder metadata = new MetadataBuilder();
        BlobBuilder ilStream = new BlobBuilder();
        MethodBodyStreamEncoder bodies = new MethodBodyStreamEncoder(ilStream);

        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            System.Reflection.AssemblyHashAlgorithm.Sha1);
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);

        AssemblyReferenceHandle corlib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(10, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A }),
            default,
            default);
        TypeReferenceHandle objectRef = metadata.AddTypeReference(corlib, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        TypeReferenceHandle exceptionRef = metadata.AddTypeReference(corlib, metadata.GetOrAddString("System"), metadata.GetOrAddString("Exception"));

        BlobBuilder code = new BlobBuilder();
        ControlFlowBuilder flow = new ControlFlowBuilder();
        InstructionEncoder il = new InstructionEncoder(code, flow);
        EncodeInstructions(body, il, flow, exceptionRef);

        StandaloneSignatureHandle localsSignature = default;
        if (body.Locals.Count > 0) {
            BlobBuilder localSig = new BlobBuilder();
            LocalVariablesEncoder locals = new BlobEncoder(localSig).LocalVariableSignature(body.Locals.Count);
            foreach (ComposedLocal local in body.Locals) {
                if (local.Type != typeof(int) || local.IsPinned) {
                    throw new NotSupportedException("the test emitter only encodes unpinned int32 locals, got " + local.Type + " pinned=" + local.IsPinned);
                }

                locals.AddVariable().Type().Int32();
            }

            localsSignature = metadata.AddStandaloneSignature(metadata.GetOrAddBlob(localSig));
        }

        MethodBodyAttributes attributes = body.InitLocals ? MethodBodyAttributes.InitLocals : MethodBodyAttributes.None;
        int bodyOffset = bodies.AddMethodBody(il, maxStack, localsSignature, attributes, usesLocalloc);

        BlobBuilder signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                0,
                returnType => {
                    if (returnsInt32) {
                        returnType.Type().Int32();
                    } else {
                        returnType.Void();
                    }
                },
                parameters => { });

        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset,
            default);

        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), method);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoLayout | TypeAttributes.BeforeFieldInit,
            default,
            metadata.GetOrAddString("C"),
            objectRef,
            MetadataTokens.FieldDefinitionHandle(1),
            method);

        ManagedPEBuilder pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), ilStream);
        BlobBuilder image = new BlobBuilder();
        pe.Serialize(image);
        return new EmittedMethod(image.ToArray());
    }

    private static void EncodeInstructions(ComposedBody body, InstructionEncoder il, ControlFlowBuilder flow, TypeReferenceHandle exceptionRef) {
        Dictionary<Label, LabelHandle> branchTargets = [];
        Dictionary<int, LabelHandle> indexLabels = [];

        LabelHandle BranchTarget(Label label) {
            if (!branchTargets.TryGetValue(label, out LabelHandle handle)) {
                handle = il.DefineLabel();
                branchTargets[label] = handle;
            }

            return handle;
        }

        LabelHandle IndexLabel(int index) {
            if (!indexLabels.TryGetValue(index, out LabelHandle handle)) {
                handle = il.DefineLabel();
                indexLabels[index] = handle;
            }

            return handle;
        }

        foreach (ComposedClause clause in body.Clauses) {
            IndexLabel(clause.TryStart);
            IndexLabel(clause.TryEnd);
            IndexLabel(clause.HandlerStart);
            IndexLabel(clause.HandlerEnd);
            if (clause.FilterStart >= 0) {
                IndexLabel(clause.FilterStart);
            }
        }

        for (int index = 0; index < body.Instructions.Count; index++) {
            CodeInstruction instruction = body.Instructions[index].Instruction;
            if (indexLabels.TryGetValue(index, out LabelHandle boundary)) {
                il.MarkLabel(boundary);
            }

            foreach (Label label in instruction.labels) {
                il.MarkLabel(BranchTarget(label));
            }

            ILOpCode opcode = (ILOpCode)(ushort)instruction.opcode.Value;
            switch (instruction.operand) {
                case null:
                    il.OpCode(opcode);
                    break;
                case Label target:
                    il.Branch(opcode, BranchTarget(target));
                    break;
                case int constant when instruction.opcode == OpCodes.Ldc_I4:
                    il.LoadConstantI4(constant);
                    break;
                default:
                    throw new NotSupportedException("the test emitter cannot encode " + instruction.opcode + " with operand " + instruction.operand);
            }
        }

        if (indexLabels.TryGetValue(body.Instructions.Count, out LabelHandle end)) {
            il.MarkLabel(end);
        }

        foreach (ComposedClause clause in body.Clauses) {
            switch (clause.Kind) {
                case ClauseKind.Catch:
                    if (clause.CatchType != typeof(Exception)) {
                        throw new NotSupportedException("the test emitter only encodes catch of System.Exception, got " + clause.CatchType);
                    }

                    flow.AddCatchRegion(IndexLabel(clause.TryStart), IndexLabel(clause.TryEnd), IndexLabel(clause.HandlerStart), IndexLabel(clause.HandlerEnd), exceptionRef);
                    break;
                case ClauseKind.Finally:
                    flow.AddFinallyRegion(IndexLabel(clause.TryStart), IndexLabel(clause.TryEnd), IndexLabel(clause.HandlerStart), IndexLabel(clause.HandlerEnd));
                    break;
                case ClauseKind.Filter:
                    flow.AddFilterRegion(IndexLabel(clause.TryStart), IndexLabel(clause.TryEnd), IndexLabel(clause.HandlerStart), IndexLabel(clause.HandlerEnd), IndexLabel(clause.FilterStart));
                    break;
                default:
                    flow.AddFaultRegion(IndexLabel(clause.TryStart), IndexLabel(clause.TryEnd), IndexLabel(clause.HandlerStart), IndexLabel(clause.HandlerEnd));
                    break;
            }
        }
    }
}
