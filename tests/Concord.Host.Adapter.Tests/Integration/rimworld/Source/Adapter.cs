using System;
using System.Collections.Generic;
using System.Reflection;

namespace Concord.Waylith.RimProbe;

public static class Adapter {
    private static readonly Assembly Assembly = Find("Concord.Host.Adapter");

    private static readonly Type EntryPoint = Assembly?.GetType("Concord.Host.Adapter.AdapterEntryPoint");

    private static readonly Type Capability = Assembly?.GetType("Concord.Host.Adapter.RuntimeCapability");

    private static readonly Type Attribution = Assembly?.GetType("Concord.Host.Adapter.FrameAttribution");

    public static bool Present => Assembly != null;

    public static string Location => Assembly == null ? "<absent>" : (Assembly.Location ?? "<memory>");

    public static string Selected() {
        if (EntryPoint == null) {
            return "adapter-absent";
        }

        object current = EntryPoint.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return current == null ? "not-installed" : Describe(current);
    }

    public static string Support() {
        if (Capability == null) {
            return "adapter-absent";
        }

        object support = Capability.GetProperty("Support", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        string explained = (string)Capability
            .GetMethod("Explain", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
            .Invoke(null, null);
        return support + " explain=" + Report.Flatten(explained);
    }

    public static bool Replaced(MethodBase target) {
        return Owners(target).Count > 0;
    }

    public static IReadOnlyList<string> Owners(MethodBase target) {
        if (Attribution == null || target == null) {
            return Array.Empty<string>();
        }

        try {
            return (IReadOnlyList<string>)Attribution
                .GetMethod("OwnersRegisteredFor", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { target });
        } catch (Exception) {
            return Array.Empty<string>();
        }
    }

    private static string Describe(object selection) {
        if (selection == null) {
            return "<null>";
        }

        Type type = selection.GetType();
        bool waylith = (bool)type.GetProperty("Waylith").GetValue(selection);
        string reason = (string)type.GetProperty("Reason").GetValue(selection);
        return (waylith ? "body-replacement" : "native-detour") + " reason=" + Report.Flatten(reason);
    }

    private static Assembly Find(string name) {
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            if (SimpleName(loaded) == name) {
                return loaded;
            }
        }

        return null;
    }

    private static string SimpleName(Assembly assembly) {
        string full = assembly.FullName;
        if (full == null) {
            return null;
        }

        int comma = full.IndexOf(',');
        return comma < 0 ? full : full.Substring(0, comma);
    }
}
