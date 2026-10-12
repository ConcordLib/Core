using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Module = System.Reflection.Module;

namespace Concord.Emit;

internal static class BodyReader {
    internal const string RefusalCode = "CONC167";

    private static readonly bool Fenced = Type.GetType("Mono.Runtime") is not null;

    private static readonly object Gate = new object();

    private static readonly Dictionary<(Guid Mvid, int Token), string?> Cache =
        new Dictionary<(Guid Mvid, int Token), string?>();

    internal static DynamicMethodDefinition Open(MethodBase method) {
        Reject(method);
        return new DynamicMethodDefinition(method);
    }

    internal static void Reject(MethodBase method) {
        if (!Fenced) {
            return;
        }

        string? refusal = Inspect(method);
        if (refusal is null) {
            return;
        }

        throw new ConcordEmitException(
            RefusalCode,
            $"'{Describe(method)}' {refusal}. Reflecting over a function pointer on this runtime overflows the " +
            "process stack instead of throwing, so Concord refuses the method rather than reading its body.");
    }

    internal static string? Inspect(MethodBase method) {
        int token = Token(method);
        if (token == 0) {
            return null;
        }

        Module module = method.Module;
        Guid mvid = module.ModuleVersionId;
        lock (Gate) {
            if (Cache.TryGetValue((mvid, token), out string? hit)) {
                return hit;
            }
        }

        string? path = LocalNames.ModuleFile(module);
        if (path is null || !TryRead(path, mvid, token, out string? refusal)) {
            return null;
        }

        lock (Gate) {
            Cache[(mvid, token)] = refusal;
        }

        return refusal;
    }

    private static bool TryRead(string path, Guid mvid, int token, out string? refusal) {
        refusal = null;
        ModuleDefinition? module = null;
        try {
            module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadingMode = ReadingMode.Deferred });
            if (module.Mvid != mvid || module.LookupToken(token) is not MethodDefinition definition) {
                return false;
            }

            refusal = Scan(definition);
            return true;
        } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or BadImageFormatException) {
            return false;
        } finally {
            module?.Dispose();
        }
    }

    private static string? Scan(MethodDefinition definition) {
        Dictionary<string, bool> seen = new Dictionary<string, bool>();

        if (Carries(definition.ReturnType, seen) is { } returned) {
            return "returns " + returned;
        }

        foreach (ParameterDefinition parameter in definition.Parameters) {
            if (Carries(parameter.ParameterType, seen) is { } taken) {
                return $"takes {taken} as '{parameter.Name}'";
            }
        }

        if (!definition.HasBody) {
            return null;
        }

        foreach (VariableDefinition variable in definition.Body.Variables) {
            if (Carries(variable.VariableType, seen) is { } held) {
                return $"holds {held} in local {variable.Index}";
            }
        }

        foreach (Instruction instruction in definition.Body.Instructions) {
            if (Operand(instruction.Operand, seen) is { } used) {
                return $"{instruction.OpCode.Name} {used} at IL_{instruction.Offset:x4}";
            }
        }

        return null;
    }

    private static string? Operand(object? operand, Dictionary<string, bool> seen) {
        switch (operand) {
            case CallSite:
                return "calls through a function pointer";
            case TypeReference type:
                return Carries(type, seen);
            case FieldReference field:
                return Carries(field.FieldType, seen) ?? Carries(field.DeclaringType, seen);
            case MethodReference method:
                return Carries(method.DeclaringType, seen) ?? Signature(method, seen);
            default:
                return null;
        }
    }

    private static string? Signature(IMethodSignature signature, Dictionary<string, bool> seen) {
        if (Carries(signature.ReturnType, seen) is { } returned) {
            return returned;
        }

        if (!signature.HasParameters) {
            return null;
        }

        foreach (ParameterDefinition parameter in signature.Parameters) {
            if (Carries(parameter.ParameterType, seen) is { } taken) {
                return taken;
            }
        }

        return null;
    }

    private static string? Carries(TypeReference? type, Dictionary<string, bool> seen) {
        if (type is null) {
            return null;
        }

        TypeReference element = type;
        while (element is TypeSpecification spec and not GenericInstanceType and not FunctionPointerType) {
            element = spec.ElementType;
        }

        if (element is FunctionPointerType) {
            return $"the function pointer '{type.FullName}'";
        }

        if (element is GenericInstanceType instance) {
            foreach (TypeReference argument in instance.GenericArguments) {
                if (Carries(argument, seen) is { } found) {
                    return found;
                }
            }

            element = instance.ElementType;
        }

        if (element.IsPrimitive || element is GenericParameter) {
            return null;
        }

        return Layout(element, seen) ? $"the value type '{element.FullName}', whose fields carry a function pointer" : null;
    }

    private static bool Layout(TypeReference type, Dictionary<string, bool> seen) {
        string key = type.FullName;
        if (seen.TryGetValue(key, out bool cached)) {
            return cached;
        }

        seen[key] = false;

        bool carries = false;
        if (Resolve(type) is { IsValueType: true } definition) {
            foreach (FieldDefinition field in definition.Fields) {
                if (!field.IsStatic && Carries(field.FieldType, seen) is not null) {
                    carries = true;
                    break;
                }
            }
        }

        seen[key] = carries;
        return carries;
    }

    private static TypeDefinition? Resolve(TypeReference type) {
        try {
            return type.Resolve();
        } catch (Exception failure) when (failure is AssemblyResolutionException or NotSupportedException
                                             or BadImageFormatException or IOException) {
            return null;
        }
    }

    private static int Token(MethodBase method) {
        try {
            return method.MetadataToken;
        } catch (InvalidOperationException) {
            return 0;
        }
    }

    private static string Describe(MethodBase method) {
        return (method.DeclaringType?.FullName ?? "<no declaring type>") + "." + method.Name;
    }
}
