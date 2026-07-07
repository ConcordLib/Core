using Microsoft.CodeAnalysis;

namespace Concord.Generators;

internal static class TargetResolution {
    public static bool HasPatchAttribute(INamedTypeSymbol declaration) {
        foreach (AttributeData attribute in declaration.GetAttributes()) {
            if (attribute.AttributeClass?.ToDisplayString() == "Concord.PatchAttribute") {
                return true;
            }
        }

        return false;
    }

    public static INamedTypeSymbol? ResolveTarget(INamedTypeSymbol declaration, Compilation compilation) {
        foreach (AttributeData attribute in declaration.GetAttributes()) {
            if (attribute.AttributeClass?.ToDisplayString() != "Concord.PatchAttribute") {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1) {
                TypedConstant argument = attribute.ConstructorArguments[0];
                if (argument.Kind == TypedConstantKind.Type) {
                    return argument.Value as INamedTypeSymbol;
                }

                if (argument.Value is string metadataName) {
                    return compilation.GetTypeByMetadataName(metadataName);
                }
            }
        }

        if (declaration.BaseType is { SpecialType: SpecialType.None } baseType) {
            return baseType;
        }

        return null;
    }
}
