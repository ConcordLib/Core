using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

internal static class ReversePatchHook
{
    internal const string LiveMarker = "[Concord.Coex] reverse-snapshot-live";

    private static readonly MethodInfo? NotifyMethod =
        typeof(ReversePatchHook).GetMethod(nameof(Notify), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly object Gate = new object();

    private static readonly Dictionary<MethodBase, MethodInfo> Trampolines = new Dictionary<MethodBase, MethodInfo>();

    private static readonly ModuleBuilder Module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("Concord.Harmony.ReversePatchHook"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Concord.Harmony.ReversePatchHook");

    private static Action<string>? logger;

    private static IDetourHandle? hookHandle;

    private static IOwnsBodyQuery? ownsBodyBackend;

    private static MethodInfo? reversePatch;

    internal static Func<MethodBase, bool>? OwnsBodyOverride { get; set; }

    internal static bool TryInstall(IDetourBackend rawBackend, Action<string> log)
    {
        logger = log;
        ownsBodyBackend = rawBackend as IOwnsBodyQuery;

        if (hookHandle != null)
        {
            return true;
        }

        if (ownsBodyBackend == null && OwnsBodyOverride == null)
        {
            logger = null;
            log(CoexistenceLogMarkers.HookUnavailable + " reverse patch: " + rawBackend.GetType().Name + " does not implement Concord.Detour.IOwnsBodyQuery, so nothing can answer which methods Concord owns");
            return false;
        }

        reversePatch = typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchFunctions")
            ?.GetMethod("ReversePatch", BindingFlags.NonPublic | BindingFlags.Static);
        if (reversePatch == null || NotifyMethod == null)
        {
            logger = null;
            log(CoexistenceLogMarkers.HookUnavailable + " reverse patch: HarmonyLib.PatchFunctions.ReversePatch was not found, so a Snapshot reverse patch of a Concord-owned method reads stale IL");
            return false;
        }

        try
        {
            Injection notify = new Injection(NotifyMethod, new InjectAt.Head(), "concord.hook", 0);
            hookHandle = rawBackend.ApplyComposed(reversePatch, new[] { notify });
        }
        catch (Exception ex)
        {
            logger = null;
            log(CoexistenceLogMarkers.HookUnavailable + " reverse patch: " + ExceptionReason.Of(ex));
            return false;
        }

        return true;
    }

    internal static void Detach()
    {
        logger = null;
        OwnsBodyOverride = null;
        ownsBodyBackend = null;
    }

    private static void Notify(ControlHandle<MethodInfo> ch, HarmonyMethod standin, MethodBase original, MethodInfo postTranspiler)
    {
        if (standin == null || original == null || standin.reversePatchType != HarmonyReversePatchType.Snapshot || !OwnsBody(original))
        {
            return;
        }

        if (postTranspiler != null)
        {
            throw new NotSupportedException(
                "Concord owns " + Describe(original) + ", so its live body is only reachable as a call, not as an instruction stream. " +
                "The transpiler declared inside " + Describe(standin.method) + " would run over that call and edit nothing. " +
                "Drop the stub's transpiler, or reverse patch with HarmonyReversePatchType.Original to edit the unpatched IL.");
        }

        logger?.Invoke(LiveMarker + " " + Describe(original) + " into " + Describe(standin.method));
        standin.reversePatchType = HarmonyReversePatchType.Original;
        try
        {
            ch.ReturnValue = (MethodInfo)reversePatch!.Invoke(null, new object?[] { standin, TrampolineFor(original), postTranspiler })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
        finally
        {
            standin.reversePatchType = HarmonyReversePatchType.Snapshot;
        }

        ch.Cancel();
    }

    private static bool OwnsBody(MethodBase method)
    {
        Func<MethodBase, bool>? supplied = OwnsBodyOverride;
        if (supplied != null)
        {
            return supplied(method);
        }

        return ownsBodyBackend != null && ownsBodyBackend.OwnsBody(method);
    }

    private static MethodInfo TrampolineFor(MethodBase original)
    {
        MethodBase key = MethodIdentity.Normalize(original);
        lock (Gate)
        {
            if (!Trampolines.TryGetValue(key, out MethodInfo? trampoline))
            {
                trampoline = Mint(key, Trampolines.Count);
                Trampolines[key] = trampoline;
            }

            return trampoline;
        }
    }

    private static MethodInfo Mint(MethodBase original, int ordinal)
    {
        ParameterInfo[] parameters = original.GetParameters();
        int offset = original.IsStatic ? 0 : 1;
        Type[] parameterTypes = new Type[parameters.Length + offset];
        if (offset == 1)
        {
            Type declaring = original.DeclaringType!;
            parameterTypes[0] = declaring.IsValueType ? declaring.MakeByRefType() : declaring;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            parameterTypes[i + offset] = parameters[i].ParameterType;
        }

        Type returnType = original is MethodInfo method ? method.ReturnType : typeof(void);
        string name = (original.DeclaringType?.FullName ?? "GLOBALTYPE") + "." + original.Name + "_ConcordLive";

        TypeBuilder type = Module.DefineType("Concord.Harmony.ReversePatchHook.Live" + ordinal, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder trampoline = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, parameterTypes);
        if (offset == 1)
        {
            trampoline.DefineParameter(1, ParameterAttributes.None, "this");
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            trampoline.DefineParameter(i + offset + 1, parameters[i].Attributes, parameters[i].Name);
        }

        ILGenerator il = trampoline.GetILGenerator();
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, (short)i);
        }

        if (original is ConstructorInfo ctor)
        {
            il.Emit(OpCodes.Call, ctor);
        }
        else
        {
            il.Emit(OpCodes.Call, (MethodInfo)original);
        }

        il.Emit(OpCodes.Ret);

        Type created = type.CreateTypeInfo()!.AsType();
        return created.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
    }

    private static string Describe(MethodBase method)
    {
        return (method.DeclaringType?.FullName ?? "<module>") + "." + method.Name;
    }
}
