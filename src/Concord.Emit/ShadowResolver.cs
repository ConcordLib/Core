using System.Reflection;
using Concord.AttachedData;

namespace Concord.Emit;

/// <summary>
///     Resolves declaration shadow fields to their matching fields on the real target type.
/// </summary>
internal static class ShadowResolver {
    /// <summary>
    ///     Builds a field remap from declaration field name to the corresponding target field, and allocates
    ///     a storage slot for every <c>[Attached]</c> declaration field.
    /// </summary>
    /// <param name="declarationType">The patch declaration type that declares shadow fields.</param>
    /// <param name="targetType">The target type whose real fields should be accessed.</param>
    /// <param name="attached">Receives the attached-field slots, keyed by declaration field name.</param>
    /// <returns>A map keyed by declaration field name, omitting fields absent on the target.</returns>
    /// <exception cref="ConcordEmitException">
    ///     Thrown with <c>CONC002</c> when a field matches by name but the signatures differ, or with
    ///     <c>CONC003</c> when a field matches nothing on the target and carries no attribute saying what it is.
    /// </exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "Concord reaches private target members by design; signatures are validated at resolve time.")]
    public static Dictionary<string, FieldInfo> BuildFieldMap(Type declarationType, Type targetType, out Dictionary<string, AttachedFieldSlot> attached) {
        FieldInfo[] declarationFields = declarationType.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Dictionary<string, FieldInfo> map = new Dictionary<string, FieldInfo>();
        attached = new Dictionary<string, AttachedFieldSlot>();

        foreach (FieldInfo declarationField in declarationFields) {
            if (declarationField.GetCustomAttribute<InjectFieldAttribute>() is not null) {
                continue;
            }

            FieldInfo? targetField = targetType.GetField(
                declarationField.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

            if (targetField is null) {
                bool marked = declarationField.GetCustomAttribute<AttachedAttribute>() is not null;
                if (declarationField.IsStatic) {
                    if (marked) {
                        throw new ConcordEmitException(
                            "CONC003",
                            $"Field '{declarationField.Name}' on declaration '{declarationType.Name}' is static, so there is no instance to attach it to. Drop [Attached]; a static field on a declaration is just a static field.");
                    }

                    continue;
                }

                // A backing field the compiler wrote (an auto-property, a field-like event) has nowhere
                // to hang an attribute, and it cannot be a shadow of anything. Attach it and move on.
                if (!marked && !IsCompilerGenerated(declarationField)) {
                    throw new ConcordEmitException(
                        "CONC003",
                        $"Field '{declarationField.Name}' on declaration '{declarationType.Name}' has no matching field on target '{targetType.Name}'. " +
                        "Mark it [Attached] to store it beside each instance, or [InjectField(\"name\")] when it shadows a target field under a different name.");
                }

                if (targetType.IsValueType) {
                    throw new ConcordEmitException(
                        "CONC003",
                        $"Field '{declarationField.Name}' on declaration '{declarationType.Name}' is [Attached], but target '{targetType.Name}' is a value type. Attached state is keyed by instance identity, which a struct does not have.");
                }

                if (declarationField.FieldType.ContainsGenericParameters) {
                    throw new ConcordEmitException(
                        "CONC003",
                        $"Field '{declarationField.Name}' on declaration '{declarationType.Name}' is [Attached] with the open type '{declarationField.FieldType}'. Attached storage is allocated per closed type, so name a concrete one.");
                }

                attached[declarationField.Name] = new AttachedFieldSlot(
                    AttachedStorage.SlotFor(declarationType, declarationField.Name, declarationField.FieldType),
                    declarationField.FieldType);
                continue;
            }

            if (declarationField.GetCustomAttribute<AttachedAttribute>() is not null) {
                throw new ConcordEmitException(
                    "CONC003",
                    $"Field '{declarationField.Name}' on declaration '{declarationType.Name}' is marked [Attached], but target '{targetType.Name}' already declares that field. Drop the attribute to shadow the real field.");
            }

            if (targetField.FieldType != declarationField.FieldType || targetField.IsStatic != declarationField.IsStatic) {
                throw new ConcordEmitException(
                    "CONC002",
                    $"Shadow field '{declarationField.Name}' on declaration '{declarationType.Name}' has type '{declarationField.FieldType}' / static={declarationField.IsStatic}, but target field has type '{targetField.FieldType}' / static={targetField.IsStatic}. Signatures must match exactly.");
            }

            map[declarationField.Name] = targetField;
        }

        return map;
    }

    private static bool IsCompilerGenerated(FieldInfo field) {
        return field.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;
    }
}
