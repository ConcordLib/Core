using System.Reflection;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter.Tests;

internal static class Shape {
    internal static Label Mark(int id) {
        return (Label)Activator.CreateInstance(typeof(Label), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { id }, null)!;
    }

    internal static LocalRef Slot(int index, Type type) {
        return (LocalRef)Activator.CreateInstance(typeof(LocalRef), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { index, type }, null)!;
    }

    internal static CodeInstruction Step(OpCode opcode, object? operand = null, params Label[] labels) {
        return new CodeInstruction(opcode, operand).WithLabels(labels);
    }

    internal static ComposedBody Body(CodeInstruction[] stream, ComposedClause[] clauses, ComposedLocal[] locals, bool initLocals) {
        return new ComposedBody(Bodies.Wrap(stream), locals, clauses, initLocals);
    }
}
