namespace Concord.Harmony.Tests;

internal static class TestUnpatch
{
    internal static void Own(global::HarmonyLib.Harmony harmony, string id)
    {
        harmony.UnpatchAll(id);
    }
}
