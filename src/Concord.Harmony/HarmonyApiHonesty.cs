using System;
using System.Reflection;
using HarmonyLib;

namespace Concord.Harmony;

internal enum SharedStateWrite
{
    Unknown,
    AfterUpdateWrapper,
    InsideUpdateWrapper,
}

internal static class HarmonyApiHonesty
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly Assembly Loaded = typeof(HarmonyLib.Harmony).Assembly;

    private static readonly Replication LoadedReplication = Resolve(Loaded);

    internal static SharedStateWrite Classify()
    {
        return Classify(Loaded);
    }

    internal static SharedStateWrite Classify(Assembly harmony)
    {
        MethodInfo? updateWrapper = harmony.GetType("HarmonyLib.PatchFunctions")?.GetMethod("UpdateWrapper", Hidden);
        Type? sharedState = harmony.GetType("HarmonyLib.HarmonySharedState");
        if (updateWrapper == null || sharedState == null || sharedState.GetMember("UpdatePatchInfo", Hidden).Length == 0)
        {
            return SharedStateWrite.Unknown;
        }

        bool inside = SupportMatrix.BodyCalls(
            updateWrapper,
            called => called.DeclaringType == sharedState && called.Name == "UpdatePatchInfo");

        return inside ? SharedStateWrite.InsideUpdateWrapper : SharedStateWrite.AfterUpdateWrapper;
    }

    internal static string? SuppressionReason(SharedStateWrite write)
    {
        return SuppressionReason(write, Loaded);
    }

    internal static string? SuppressionReason(SharedStateWrite write, Assembly harmony)
    {
        switch (write)
        {
            case SharedStateWrite.AfterUpdateWrapper:
                return null;
            case SharedStateWrite.InsideUpdateWrapper:
                string? missing = (harmony == Loaded ? LoadedReplication : Resolve(harmony)).Missing;
                if (missing == null)
                {
                    return null;
                }

                return "Harmony " + harmony.GetName().Version + " writes its shared patch state inside PatchFunctions.UpdateWrapper and Concord could not find " +
                       missing + " to replicate that write, so Harmony keeps building this method";
            default:
                return "PatchFunctions.UpdateWrapper or HarmonySharedState.UpdatePatchInfo was not found, so where Harmony writes its shared patch state could not be measured. The hook stays asleep";
        }
    }

    internal static void ValidateSurvivingMetadata(PatchInfo patchInfo)
    {
        LoadedReplication.ValidateSurvivingMetadata!.Invoke(patchInfo, null);
    }

    internal static void ReplicateSharedStateWrite(MethodBase original, MethodInfo replacement, PatchInfo patchInfo)
    {
        Replication r = LoadedReplication;
        r.VersionCount!.SetValue(patchInfo, (int)r.VersionCount.GetValue(patchInfo)! + 1);
        byte[] bytes = (byte[])r.SerializeValidated!.Invoke(null, new object[] { patchInfo })!;
        r.UpdatePatchInfo!.Invoke(null, new object[] { original, replacement, bytes });
    }

    private static Replication Resolve(Assembly harmony)
    {
        Type? patchInfo = harmony.GetType("HarmonyLib.PatchInfo");
        return new Replication(
            patchInfo?.GetMethod("ValidateSurvivingMetadata", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null),
            patchInfo?.GetField("VersionCount", BindingFlags.Public | BindingFlags.Instance),
            patchInfo == null ? null : harmony.GetType("HarmonyLib.PatchInfoSerialization")?.GetMethod("SerializeValidated", Hidden, null, new[] { patchInfo }, null),
            harmony.GetType("HarmonyLib.HarmonySharedState")?.GetMethod("UpdatePatchInfo", Hidden, null, new[] { typeof(MethodBase), typeof(MethodInfo), typeof(byte[]) }, null));
    }

    private readonly struct Replication
    {
        internal readonly MethodInfo? ValidateSurvivingMetadata;

        internal readonly FieldInfo? VersionCount;

        internal readonly MethodInfo? SerializeValidated;

        internal readonly MethodInfo? UpdatePatchInfo;

        internal Replication(MethodInfo? validateSurvivingMetadata, FieldInfo? versionCount, MethodInfo? serializeValidated, MethodInfo? updatePatchInfo)
        {
            ValidateSurvivingMetadata = validateSurvivingMetadata;
            VersionCount = versionCount;
            SerializeValidated = serializeValidated;
            UpdatePatchInfo = updatePatchInfo;
        }

        internal string? Missing =>
            ValidateSurvivingMetadata == null ? "PatchInfo.ValidateSurvivingMetadata"
            : VersionCount == null ? "PatchInfo.VersionCount"
            : SerializeValidated == null ? "PatchInfoSerialization.SerializeValidated(PatchInfo)"
            : UpdatePatchInfo == null ? "HarmonySharedState.UpdatePatchInfo(MethodBase, MethodInfo, byte[])"
            : null;
    }
}
