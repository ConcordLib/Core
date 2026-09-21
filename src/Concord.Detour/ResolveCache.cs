using System.Collections;
using System.Reflection;
using MonoMod.Utils;

namespace Concord.Detour;

/// <summary>
///     Drops MonoMod's cached reflection lookups that point into reflection-only assemblies.
/// </summary>
/// <remarks>
///     A host that swaps a game assembly for a rewritten copy (RimWorld's Prepatcher) marks the old copy
///     reflection-only and loads the new one under the same name. Concord's runtime survives that swap, so
///     MonoMod's static resolve cache still holds members of the old copy. Its cache key for a method on
///     a generic instance type carries no assembly hash, so the next compose gets a stale
///     <see cref="MethodInfo" /> and the runtime rejects the body. Call this once the swap is done.
/// </remarks>
public static class ResolveCache {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "MonoMod's resolve caches are private statics, and clearing them after an assembly swap needs them.")]
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>
    ///     Removes every cached member and assembly that lives in a reflection-only assembly.
    /// </summary>
    /// <returns>The number of entries removed, or -1 when MonoMod's caches could not be found.</returns>
    public static int ForgetReflectionOnly() {
        Type helper = typeof(ReflectionHelper);
        if (helper.GetField("ResolveReflectionCache", Hidden)?.GetValue(null) is not IDictionary members ||
            helper.GetField("AssembliesCache", Hidden)?.GetValue(null) is not IDictionary assemblySets ||
            helper.GetField("AssemblyCache", Hidden)?.GetValue(null) is not IDictionary assemblies) {
            return -1;
        }

        int removed = 0;
        removed += Purge(members, value => value is WeakReference r && r.Target is MemberInfo m && IsStale(m));
        removed += Purge(assemblies, value => value is WeakReference r && r.Target is Assembly a && a.ReflectionOnly);
        removed += Purge(assemblySets, value => value is WeakReference[] refs && Array.Exists(refs, r => r.Target is Assembly a && a.ReflectionOnly));
        return removed;
    }

    // A List<Ability>.GetEnumerator lives in mscorlib. Only its generic argument is the stale part.
    private static bool IsStale(MemberInfo member) {
        if (member.Module.Assembly.ReflectionOnly) {
            return true;
        }

        if (member is Type type) {
            return IsStale(type);
        }

        if (member.DeclaringType is not null && IsStale(member.DeclaringType)) {
            return true;
        }

        return member is MethodInfo { IsGenericMethod: true } method && Array.Exists(method.GetGenericArguments(), IsStale);
    }

    private static bool IsStale(Type type) {
        if (type.Assembly.ReflectionOnly) {
            return true;
        }

        if (type.HasElementType && type.GetElementType() is Type element) {
            return IsStale(element);
        }

        return type.IsGenericType && !type.IsGenericTypeDefinition && Array.Exists(type.GetGenericArguments(), IsStale);
    }

    private static int Purge(IDictionary cache, Func<object?, bool> stale) {
        List<object> keys = new List<object>();
        lock (cache) {
            foreach (DictionaryEntry entry in cache) {
                if (stale(entry.Value)) {
                    keys.Add(entry.Key);
                }
            }

            foreach (object key in keys) {
                cache.Remove(key);
            }
        }

        return keys.Count;
    }
}
