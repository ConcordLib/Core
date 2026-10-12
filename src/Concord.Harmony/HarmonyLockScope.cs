using System;
using System.Reflection;
using System.Threading;

namespace Concord.Harmony;

internal sealed class HarmonyLockScope : IDisposable
{
    private static readonly object? Locker = ResolveLocker();

    private HarmonyLockScope()
    {
    }

    internal static bool Available { get; } = Locker != null;

    internal static bool Held => Locker != null && Monitor.IsEntered(Locker);

    public void Dispose()
    {
        Monitor.Exit(Locker!);
    }

    internal static IDisposable Enter()
    {
        Monitor.Enter(Locker!);
        return new HarmonyLockScope();
    }

    private static object? ResolveLocker()
    {
        FieldInfo? field = typeof(HarmonyLib.PatchProcessor).GetField("locker", BindingFlags.NonPublic | BindingFlags.Static);
        if (field == null)
        {
            return null;
        }

        return field.GetValue(null);
    }
}
