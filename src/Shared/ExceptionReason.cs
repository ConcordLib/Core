using System;
using System.Reflection;

namespace Concord;

internal static class ExceptionReason {
    public static string Of(Exception error) {
        Exception current = error;
        while (current is TargetInvocationException invocation && invocation.InnerException != null) {
            current = invocation.InnerException;
        }

        return current.GetType().Name + ": " + current.Message;
    }
}
