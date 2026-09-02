using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MonoMod.Core.Platforms;

namespace Concord.Detour;

/// <summary>
///     Resolves a method to one canonical <see cref="MethodBase" /> so the same target reached through
///     different reflection paths compares and hashes as a single key, and maps a composed wrapper that
///     is running on the stack back to the target it replaced.
/// </summary>
public static class MethodIdentity {
    private static readonly Dictionary<IntPtr, MethodBase> Wrappers = new Dictionary<IntPtr, MethodBase>();
    private static readonly object WrappersGate = new object();

    /// <summary>
    ///     Returns the canonical <see cref="MethodBase" /> for a target, or the target itself when it has no
    ///     canonical form (open generics, module-level methods, runtimes without handle resolution).
    /// </summary>
    /// <param name="target">The method to normalize.</param>
    /// <returns>The canonical instance to use as a routing key.</returns>
    public static MethodBase Normalize(MethodBase target) {
        if (target.DeclaringType == null || target.DeclaringType.IsGenericTypeDefinition || target.IsGenericMethodDefinition) {
            return target;
        }

        try {
            return MethodBase.GetMethodFromHandle(target.MethodHandle, target.DeclaringType.TypeHandle) ?? target;
        } catch (NotSupportedException) {
            return target;
        }
    }

    /// <summary>
    ///     Maps a composed wrapper back to the method it replaced. A detoured target does not appear on the
    ///     stack under its own name: the frame reports Concord's generated wrapper instead, so anything that
    ///     reads a stack frame has to come back through here before asking a question about the target.
    /// </summary>
    /// <param name="method">
    ///     A method read off a stack frame, or any other <see cref="MethodBase" /> to test.
    /// </param>
    /// <returns>
    ///     The target the wrapper replaced, or <see langword="null" /> when the method is not a live Concord
    ///     wrapper. A plain unpatched method, a wrapper another patch library composed, and a wrapper whose
    ///     patches have since been reverted all return <see langword="null" />.
    /// </returns>
    public static MethodBase? ResolveOriginal(MethodBase method) {
        if (method is null) {
            throw new ArgumentNullException(nameof(method));
        }

        if (!TryKeysOf(method, out IntPtr handle, out IntPtr entryPoint)) {
            return null;
        }

        lock (WrappersGate) {
            if (Wrappers.TryGetValue(handle, out MethodBase? byHandle)) {
                return byHandle;
            }

            return Wrappers.TryGetValue(entryPoint, out MethodBase? byEntryPoint) ? byEntryPoint : null;
        }
    }

    internal static void Remember(MethodInfo wrapper, MethodBase target, List<IntPtr> keys) {
        if (!TryKeysOf(wrapper, out IntPtr handle, out IntPtr entryPoint)) {
            return;
        }

        lock (WrappersGate) {
            Wrappers[handle] = target;
            Wrappers[entryPoint] = target;
        }

        keys.Add(handle);
        keys.Add(entryPoint);
    }

    internal static void Forget(List<IntPtr> keys) {
        if (keys.Count == 0) {
            return;
        }

        lock (WrappersGate) {
            foreach (IntPtr key in keys) {
                Wrappers.Remove(key);
            }
        }

        keys.Clear();
    }

    // A wrapper is a DynamicMethod, so the stack frame reports a different MethodBase instance than the one
    // composition produced. The runtime handle and the compiled entry point are the two identities both views
    // agree on, and which of them survives depends on the runtime, so key on each.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Attribution is diagnostic only: any runtime that will not hand over a handle degrades to an unresolved frame rather than throwing at a caller who is already reporting an error.")]
    [SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "Attribution is diagnostic only: any runtime that will not hand over a handle degrades to an unresolved frame rather than throwing at a caller who is already reporting an error.")]
    private static bool TryKeysOf(MethodBase method, out IntPtr handle, out IntPtr entryPoint) {
        try {
            PlatformTriple triple = PlatformTriple.Current;
            RuntimeMethodHandle resolved = triple.Runtime.GetMethodHandle(triple.GetIdentifiable(method));
            handle = resolved.Value;
            entryPoint = resolved.GetFunctionPointer();
            return handle != IntPtr.Zero || entryPoint != IntPtr.Zero;
        } catch (Exception) {
            handle = IntPtr.Zero;
            entryPoint = IntPtr.Zero;
            return false;
        }
    }
}
