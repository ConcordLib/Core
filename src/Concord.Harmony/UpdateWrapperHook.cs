#nullable disable

using System;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

/// <summary>
///     Hooks Harmony so Concord hears about a patch before it lands, instead of noticing afterwards.
///     <c>PatchFunctions.UpdateWrapper</c> is the one place every Harmony path funnels through:
///     <c>Patch</c>, <c>PatchAll</c>, <c>PatchClassProcessor</c>, <c>Unpatch</c> and <c>UnpatchAll</c> all
///     call it, so a single hook has no coverage gaps and catches unpatch for free.
/// </summary>
internal static class UpdateWrapperHook
{
    private static readonly MethodInfo NotifyMethod =
        typeof(UpdateWrapperHook).GetMethod(nameof(Notify), BindingFlags.NonPublic | BindingFlags.Static);

    // Concord's own rebuilds re-enter UpdateWrapper (TryRoute -> harmony.Patch -> UpdateWrapper). At that
    // moment the route state is still Raw, because Bridge is only set once TryRoute returns, so a guard on
    // the router side would recurse forever. It has to live here.
    [ThreadStatic]
    private static int suppressDepth;

    private static IForeignPatchObserver observer;

    private static IDetourHandle hookHandle;

    /// <summary>
    ///     Installs the notifier. Safe to call more than once; only the first call installs.
    /// </summary>
    /// <param name="target">The observer to notify before each Harmony rebuild.</param>
    /// <param name="rawBackend">The plain backend, never the routing one.</param>
    /// <param name="log">Writes coexistence messages to the host game's log.</param>
    /// <returns>True when the hook is installed.</returns>
    internal static bool TryInstall(IForeignPatchObserver target, IDetourBackend rawBackend, Action<string> log)
    {
        if (hookHandle != null)
        {
            return true;
        }

        MethodInfo updateWrapper = ResolveUpdateWrapper();
        if (updateWrapper == null)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " HarmonyLib.PatchFunctions.UpdateWrapper was not found; late contention stays unrecoverable");
            return false;
        }

        if (NotifyMethod == null)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " the notify injection method was not found");
            return false;
        }

        observer = target;

        try
        {
            Injection notify = new Injection(NotifyMethod, new InjectAt.Head(), "concord.hook", 0);
            hookHandle = rawBackend.ApplyComposed(updateWrapper, new[] { notify });
        }
        catch (Exception ex)
        {
            observer = null;
            log(CoexistenceLogMarkers.HookUnavailable + " " + ex.Message);
            return false;
        }

        log(CoexistenceLogMarkers.HookInstalled + " " + typeof(HarmonyLib.Harmony).Assembly.GetName().Version);
        return true;
    }

    /// <summary>
    ///     Suppresses the notifier on this thread for the duration of the scope, so Concord's own calls
    ///     into Harmony do not report themselves as foreign patches.
    /// </summary>
    /// <returns>A scope that restores notification when disposed.</returns>
    internal static IDisposable Suppress()
    {
        suppressDepth++;
        return new SuppressScope();
    }

    internal static void Uninstall()
    {
        hookHandle?.Dispose();
        hookHandle = null;
        observer = null;
    }

    private static MethodInfo ResolveUpdateWrapper()
    {
        Type patchFunctions = typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchFunctions");
        return patchFunctions?.GetMethod("UpdateWrapper", BindingFlags.NonPublic | BindingFlags.Static);
    }

    // Mirrors internal static MethodInfo PatchFunctions.UpdateWrapper(MethodBase, PatchInfo). Concord binds
    // target parameters positionally after the ControlHandle, so the full arity has to be declared even
    // though only `original` is read.
    private static void Notify(ControlHandle<MethodInfo> ch, MethodBase original, PatchInfo patchInfo)
    {
        if (suppressDepth > 0 || original == null)
        {
            return;
        }

        IForeignPatchObserver current = observer;
        current?.OnForeignPatchPending(original, patchInfo);
    }

    private sealed class SuppressScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            suppressDepth--;
        }
    }
}
