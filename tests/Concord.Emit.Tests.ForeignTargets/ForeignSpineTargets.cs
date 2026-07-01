namespace Concord.Emit.Tests.ForeignTargets;

/// <summary>
///     Spine targets defined in a SEPARATE assembly from the emit test project. Composing these
///     forces the spine copy to carry cross-module <c>ParameterDefinition</c> operands for args
///     beyond arg3, reproducing the foreign-module raw-operand path that crashes the JIT.
/// </summary>
public static class ForeignSpineTargets {
    /// <summary>Gets or sets the number of times a spine body actually ran.</summary>
    public static int SpineRuns { get; set; }

    /// <summary>A five-argument static method whose fifth arg forces an explicit arg-load operand.</summary>
    public static int CombineFive(int a, int b, int c, int d, int e) {
        SpineRuns++;
        return (a * 10000) + (b * 1000) + (c * 100) + (d * 10) + e;
    }

    /// <summary>A five-argument static method that reassigns its fifth arg (explicit starg operand).</summary>
    public static int ReassignHighArg(int a, int b, int c, int d, int e) {
        SpineRuns++;
        e = (e * 2) + a;
        return (a * 10000) + (b * 1000) + (c * 100) + (d * 10) + e;
    }
}

/// <summary>An instance spine target in a separate assembly with five explicit arguments.</summary>
public sealed class ForeignInstanceSpineTarget {
    /// <summary>Gets or sets a seed value mixed into the result.</summary>
    public int Value { get; set; }

    /// <summary>Combines five arguments with the instance seed.</summary>
    public int CombineFive(int a, int b, int c, int d, int e) {
        return Value + (a * 10000) + (b * 1000) + (c * 100) + (d * 10) + e;
    }
}
