using System.Reflection;

namespace Concord.Emit;

/// <summary>
/// A union type representing the operand of an IL instruction in a language-neutral form.
/// </summary>
public sealed class NeutralOperand {
    /// <summary>
    /// A singleton operand representing no value.
    /// </summary>
    public static readonly NeutralOperand None = new NeutralOperand(NeutralOperandKind.None, null);

    private NeutralOperand(NeutralOperandKind kind, object? value) {
        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// The kind of operand.
    /// </summary>
    public NeutralOperandKind Kind { get; }

    /// <summary>
    /// The raw value of the operand.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Creates an operand holding a 32-bit signed integer.
    /// </summary>
    /// <param name="value">The 32-bit signed integer value.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfInt32(int value) {
        return new NeutralOperand(NeutralOperandKind.Int32, value);
    }

    /// <summary>
    /// Creates an operand holding a 64-bit signed integer.
    /// </summary>
    /// <param name="value">The 64-bit signed integer value.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfInt64(long value) {
        return new NeutralOperand(NeutralOperandKind.Int64, value);
    }

    /// <summary>
    /// Creates an operand holding a single-precision floating-point number.
    /// </summary>
    /// <param name="value">The single-precision floating-point value.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfSingle(float value) {
        return new NeutralOperand(NeutralOperandKind.Single, value);
    }

    /// <summary>
    /// Creates an operand holding a double-precision floating-point number.
    /// </summary>
    /// <param name="value">The double-precision floating-point value.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfDouble(double value) {
        return new NeutralOperand(NeutralOperandKind.Double, value);
    }

    /// <summary>
    /// Creates an operand holding a string value.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfString(string value) {
        return new NeutralOperand(NeutralOperandKind.String, value);
    }

    /// <summary>
    /// Creates an operand referring to a method argument by slot number.
    /// </summary>
    /// <param name="slot">The argument slot.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfArgument(int slot) {
        return new NeutralOperand(NeutralOperandKind.Argument, slot);
    }

    /// <summary>
    /// Creates an operand referring to a local variable by id.
    /// </summary>
    /// <param name="localId">The local variable id.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfLocal(int localId) {
        return new NeutralOperand(NeutralOperandKind.Local, localId);
    }

    /// <summary>
    /// Creates an operand referring to a label by id.
    /// </summary>
    /// <param name="labelId">The label id.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfLabel(int labelId) {
        return new NeutralOperand(NeutralOperandKind.Label, labelId);
    }

    /// <summary>
    /// Creates an operand holding an array of label ids for switch targets.
    /// </summary>
    /// <param name="labelIds">The array of label ids.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfSwitchLabels(int[] labelIds) {
        return new NeutralOperand(NeutralOperandKind.SwitchLabels, labelIds);
    }

    /// <summary>
    /// Creates an operand referring to a type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfType(Type type) {
        return new NeutralOperand(NeutralOperandKind.Type, type);
    }

    /// <summary>
    /// Creates an operand referring to a field.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfField(FieldInfo field) {
        return new NeutralOperand(NeutralOperandKind.Field, field);
    }

    /// <summary>
    /// Creates an operand referring to a method.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>A new operand.</returns>
    public static NeutralOperand OfMethod(MethodBase method) {
        return new NeutralOperand(NeutralOperandKind.Method, method);
    }

    /// <summary>
    /// Extracts a 32-bit signed integer value.
    /// </summary>
    /// <returns>The 32-bit signed integer.</returns>
    public int AsInt32() {
        return (int)Value!;
    }

    /// <summary>
    /// Extracts a 64-bit signed integer value.
    /// </summary>
    /// <returns>The 64-bit signed integer.</returns>
    public long AsInt64() {
        return (long)Value!;
    }

    /// <summary>
    /// Extracts a single-precision floating-point value.
    /// </summary>
    /// <returns>The single-precision floating-point value.</returns>
    public float AsSingle() {
        return (float)Value!;
    }

    /// <summary>
    /// Extracts a double-precision floating-point value.
    /// </summary>
    /// <returns>The double-precision floating-point value.</returns>
    public double AsDouble() {
        return (double)Value!;
    }

    /// <summary>
    /// Extracts a string value.
    /// </summary>
    /// <returns>The string.</returns>
    public string AsString() {
        return (string)Value!;
    }

    /// <summary>
    /// Extracts an argument slot number.
    /// </summary>
    /// <returns>The argument slot.</returns>
    public int AsArgumentSlot() {
        return (int)Value!;
    }

    /// <summary>
    /// Extracts a local variable id.
    /// </summary>
    /// <returns>The local variable id.</returns>
    public int AsLocalId() {
        return (int)Value!;
    }

    /// <summary>
    /// Extracts a label id.
    /// </summary>
    /// <returns>The label id.</returns>
    public int AsLabelId() {
        return (int)Value!;
    }

    /// <summary>
    /// Extracts an array of label ids.
    /// </summary>
    /// <returns>The array of label ids.</returns>
    public int[] AsSwitchLabelIds() {
        return (int[])Value!;
    }

    /// <summary>
    /// Extracts a type reference.
    /// </summary>
    /// <returns>The type.</returns>
    public Type AsType() {
        return (Type)Value!;
    }

    /// <summary>
    /// Extracts a field reference.
    /// </summary>
    /// <returns>The field.</returns>
    public FieldInfo AsField() {
        return (FieldInfo)Value!;
    }

    /// <summary>
    /// Extracts a method reference.
    /// </summary>
    /// <returns>The method.</returns>
    public MethodBase AsMethod() {
        return (MethodBase)Value!;
    }
}
