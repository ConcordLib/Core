using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Resolves declaration shadow fields to their matching fields on the real target type.
/// </summary>
internal static class ShadowResolver {
    /// <summary>
    ///     Builds a field remap from declaration field name to the corresponding target field.
    ///     A declaration field with no matching field on the target is not a shadow; it is an
    ///     attached-property declaration and is skipped rather than mapped.
    /// </summary>
    /// <param name="declarationType">The patch declaration type that declares shadow fields.</param>
    /// <param name="targetType">The target type whose real fields should be accessed.</param>
    /// <returns>A map keyed by declaration field name, omitting fields absent on the target.</returns>
    /// <exception cref="ConcordEmitException">
    ///     Thrown with <c>CONC002</c> when a field matches by name but the signatures differ.
    /// </exception>
    public static Dictionary<string, FieldInfo> BuildFieldMap(Type declarationType, Type targetType) {
        FieldInfo[] declarationFields = declarationType.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); // NOSONAR concord reaches private target members by design; validated at resolve time

        Dictionary<string, FieldInfo> map = new Dictionary<string, FieldInfo>();

        foreach (FieldInfo declarationField in declarationFields) {
            if (declarationField.GetCustomAttribute<InjectFieldAttribute>() is not null) {
                continue;
            }

            FieldInfo? targetField = targetType.GetField(
                declarationField.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); // NOSONAR concord reaches private target members by design; validated at resolve time

            if (targetField is null) {
                continue;
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
}
