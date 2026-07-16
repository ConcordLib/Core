namespace Concord.Emit;

/// <summary>
/// Distinguishes the kind of value held by a <see cref="NeutralOperand"/>.
/// </summary>
public enum NeutralOperandKind {
    /// <summary>No operand.</summary>
    None,

    /// <summary>32-bit signed integer.</summary>
    Int32,

    /// <summary>64-bit signed integer.</summary>
    Int64,

    /// <summary>Single-precision floating-point number.</summary>
    Single,

    /// <summary>Double-precision floating-point number.</summary>
    Double,

    /// <summary>String value.</summary>
    String,

    /// <summary>Method argument by slot number.</summary>
    Argument,

    /// <summary>Local variable by id.</summary>
    Local,

    /// <summary>Label by id.</summary>
    Label,

    /// <summary>Array of label ids for switch targets.</summary>
    SwitchLabels,

    /// <summary>Type reference.</summary>
    Type,

    /// <summary>Field reference.</summary>
    Field,

    /// <summary>Method reference.</summary>
    Method,
}
