using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class ContractTests {
    [Fact]
    public void ComposedBodyExposesExactlyTheFourDocumentedMembers() {
        string[] actual = typeof(ComposedBody)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Clauses", "InitLocals", "Instructions", "Locals" }, actual);
    }

    [Fact]
    public void ClauseKindHasTheFourDocumentedKinds() {
        string[] actual = Enum.GetNames(typeof(ClauseKind))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Catch", "Fault", "Filter", "Finally" }, actual);
    }

    [Fact]
    public void ComposedInstructionCarriesInstructionAndBothOriginChannels() {
        Assert.Equal(typeof(CodeInstruction), typeof(ComposedInstruction).GetProperty("Instruction")!.PropertyType);
        Assert.Equal(typeof(int?), typeof(ComposedInstruction).GetProperty("OriginOffset")!.PropertyType);
        Assert.Equal(typeof(MethodBase), typeof(ComposedInstruction).GetProperty("OriginInjection")!.PropertyType);
    }

    [Fact]
    public void ComposedLocalCarriesTypeAndPinnedFlag() {
        Assert.Equal(typeof(Type), typeof(ComposedLocal).GetProperty("Type")!.PropertyType);
        Assert.Equal(typeof(bool), typeof(ComposedLocal).GetProperty("IsPinned")!.PropertyType);
    }

    [Fact]
    public void ComposedClauseBoundariesAreInstructionIndices() {
        foreach (string name in new[] { "TryStart", "TryEnd", "HandlerStart", "HandlerEnd", "FilterStart" }) {
            Assert.Equal(typeof(int), typeof(ComposedClause).GetProperty(name)!.PropertyType);
        }

        Assert.Equal(typeof(Type), typeof(ComposedClause).GetProperty("CatchType")!.PropertyType);
        Assert.Equal(typeof(ClauseKind), typeof(ComposedClause).GetProperty("Kind")!.PropertyType);
    }

    [Fact]
    public void ComposeResultCarriesBodyAndRejection() {
        Assert.Equal(typeof(ComposedBody), typeof(ComposeResult).GetProperty("Body")!.PropertyType);
        Assert.Equal(typeof(string), typeof(ComposeResult).GetProperty("BodyRejection")!.PropertyType);
    }

    [Fact]
    public void CaptureGateIsAStaticBoolThatSurvivesARoundTrip() {
        bool previous = WrapperComposer.CaptureComposedBody;
        try {
            WrapperComposer.CaptureComposedBody = true;
            Assert.True(WrapperComposer.CaptureComposedBody);
            WrapperComposer.CaptureComposedBody = false;
            Assert.False(WrapperComposer.CaptureComposedBody);
        }
        finally {
            WrapperComposer.CaptureComposedBody = previous;
        }
    }

    [Fact]
    public void MetadataEncoderIsAvailableWithoutAPackageReference() {
        BlobBuilder builder = new BlobBuilder();
        InstructionEncoder encoder = new InstructionEncoder(builder, new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);

        Assert.Equal(new byte[] { 0x2a }, builder.ToArray());
    }
}
