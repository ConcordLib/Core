namespace Concord.Emit;

internal static class FlowTerminators {
    internal static readonly string[] Catch = ["leave", "leave.s", "throw", "rethrow"];

    internal static readonly string[] Finally = ["endfinally", "endfault", "throw", "rethrow"];
}
