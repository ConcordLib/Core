using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod.Utils;

namespace Concord.BodyReader.Probe;

public static class Program {
    public static int Main(string[] args) {
        if (args.Length < 3) {
            Console.Error.WriteLine("usage: <shape> <probe> <marker-file>");
            return 3;
        }

        string shape = args[0];
        string probe = args[1];
        string marker = args[2];

        if (shape == "selftest-neverran") {
            Console.Error.WriteLine("planted: exiting before the marker");
            return 3;
        }

        File.AppendAllText(marker, "ENTER\t" + shape + "\t" + probe + "\n");
        Console.Out.WriteLine("ENTER " + shape + " " + probe);
        Console.Out.Flush();

        try {
            Report(marker, "OK", Run(shape, probe));
        } catch (Exception failure) {
            Report(marker, "EXC", failure.GetType().Name + ": " + Flatten(failure.Message) + " @ " + TopFrame(failure));
        }

        return 0;
    }

    [DllImport("libc.so.6", EntryPoint = "abort")]
    private static extern void Abort();

    private static string Run(string shape, string probe) {
        switch (shape) {
            case "selftest-ok":
                return "control";
            case "selftest-throw":
                throw new InvalidOperationException("planted break");
            case "selftest-abort":
                Abort();
                return "unreachable";
            default:
                break;
        }

        MethodBase target = Resolve(shape);
        switch (probe) {
            case "resolve":
                return (target.DeclaringType?.FullName ?? "?") + "." + target.Name;
            case "getbody":
                return target.GetMethodBody() is null ? "no body" : "got a MethodBody";
            case "localcount":
                return (target.GetMethodBody()?.LocalVariables.Count ?? -1) + " locals, no LocalType read";
            case "body":
                return Body(target);
            case "locals":
                return Locals(target);
            case "params":
                return Params(target);
            case "cecil":
                return Cecil(target);
            case "dmdctor":
                return "constructed, not disposed: " + new DynamicMethodDefinition(target).GetType().Name;
            case "dmd":
                return Dmd(target);
            case "dmddef":
                return DmdDefinition(target);
            case "dmdgen":
                return DmdGenerate(target);
            case "sig":
                return Signature(target);
            case "sigkind":
                return SignatureKind(target);
            case "localvt":
                return LocalValueTypes(target);
            case "cecilops":
                return CecilOperands(target);
            case "clone":
                return "cloned " + Concord.Emit.OriginalBody.Clone((MethodInfo)target).Name;
            default:
                throw new ArgumentException("unknown probe '" + probe + "'");
        }
    }

    private static MethodBase Resolve(string shape) {
        if (shape.StartsWith("ext:", StringComparison.Ordinal)) {
            string[] spec = shape.Substring(4).Split('|');
            Assembly assembly = Assembly.LoadFrom(spec[0]);
            Type foreign = assembly.GetType(spec[1], throwOnError: true)!;
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            foreach (MethodInfo candidate in foreign.GetMethods(Any)) {
                if (candidate.Name == spec[2]) {
                    return candidate;
                }
            }

            throw new MissingMethodException(spec[1], spec[2]);
        }

        if (shape == "DynamicAssemblyLocal") {
            return DynamicAssemblyLocalShape.Build();
        }

        bool closed = shape.EndsWith("Closed", StringComparison.Ordinal);
        string stem = closed ? shape.Substring(0, shape.Length - "Closed".Length) : shape;
        Type type = Type.GetType("Concord.BodyReader.Probe." + stem + "Shape", throwOnError: true)!;
        MethodBase method = type.GetMethod("M", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, "M");
        return closed ? ((MethodInfo)method).MakeGenericMethod(typeof(int)) : method;
    }

    private static string Body(MethodBase target) {
        MethodBody? body = target.GetMethodBody();
        if (body is null) {
            return "no body";
        }

        return body.LocalVariables.Count + " locals, " + body.ExceptionHandlingClauses.Count + " handlers, "
            + (body.GetILAsByteArray()?.Length ?? -1) + " il bytes";
    }

    private static string Locals(MethodBase target) {
        MethodBody? body = target.GetMethodBody();
        if (body is null) {
            return "no body";
        }

        List<string> parts = new List<string>();
        foreach (LocalVariableInfo local in body.LocalVariables) {
            Type type = local.LocalType;
            parts.Add(local.LocalIndex.ToString(CultureInfo.InvariantCulture) + ":" + Name(type) + (local.IsPinned ? "|pinned" : string.Empty));
        }

        return parts.Count + " locals [" + string.Join(", ", parts.ToArray()) + "]";
    }

    private static string Params(MethodBase target) {
        List<string> parts = new List<string>();
        if (target is MethodInfo info) {
            parts.Add("ret=" + Name(info.ReturnType));
        }

        foreach (ParameterInfo parameter in target.GetParameters()) {
            parts.Add(Name(parameter.ParameterType));
        }

        return "cc=" + target.CallingConvention + " [" + string.Join(", ", parts.ToArray()) + "]";
    }

    private static string Cecil(MethodBase target) {
        string path = target.Module.FullyQualifiedName;
        if (!File.Exists(path)) {
            return "no module file on disk: " + path;
        }

        using Mono.Cecil.ModuleDefinition module = Mono.Cecil.ModuleDefinition.ReadModule(path);
        if (module.LookupToken(target.MetadataToken) is not Mono.Cecil.MethodDefinition definition) {
            return "token 0x" + target.MetadataToken.ToString("X8", CultureInfo.InvariantCulture) + " did not resolve";
        }

        if (!definition.HasBody) {
            return "no body";
        }

        List<string> parts = new List<string>();
        foreach (Mono.Cecil.Cil.VariableDefinition variable in definition.Body.Variables) {
            parts.Add(variable.Index.ToString(CultureInfo.InvariantCulture) + ":" + variable.VariableType.FullName
                + (variable.VariableType.IsPinned ? "|pinned" : string.Empty));
        }

        return parts.Count + " locals [" + string.Join(", ", parts.ToArray()) + "]";
    }

    private static string Dmd(MethodBase target) {
        using DynamicMethodDefinition dmd = new DynamicMethodDefinition(target);
        return "constructed " + dmd.GetType().Name;
    }

    private static string DmdDefinition(MethodBase target) {
        using DynamicMethodDefinition dmd = new DynamicMethodDefinition(target);
        Mono.Cecil.Cil.MethodBody body = dmd.Definition.Body;
        return body.Instructions.Count + " instructions, " + body.Variables.Count + " locals";
    }

    private static string DmdGenerate(MethodBase target) {
        using DynamicMethodDefinition dmd = new DynamicMethodDefinition(target);
        MethodInfo generated = dmd.Generate();
        return "generated " + generated.Name;
    }

    private static string Signature(MethodBase target) {
        MethodBody? body = target.GetMethodBody();
        if (body is null) {
            return "no body";
        }

        int token = body.LocalSignatureMetadataToken;
        if (token == 0) {
            return "token 0 (runtime reports no local signature)";
        }

        byte[] blob = target.Module.ResolveSignature(token);
        return "token 0x" + token.ToString("X8", CultureInfo.InvariantCulture) + ", " + blob.Length + " bytes: " + Hex(blob);
    }

    private static string LocalValueTypes(MethodBase target) {
        MethodBody? body = target.GetMethodBody();
        if (body is null) {
            return "no body";
        }

        List<string> parts = new List<string>();
        foreach (LocalVariableInfo local in body.LocalVariables) {
            parts.Add(Name(local.LocalType) + " isValueType=" + local.LocalType.IsValueType);
        }

        return parts.Count + " locals [" + string.Join(", ", parts.ToArray()) + "]";
    }

    private static string SignatureKind(MethodBase target) {
        List<string> parts = new List<string>();
        if (target is MethodInfo info) {
            parts.Add("ret " + Kind(info.ReturnType));
        }

        foreach (ParameterInfo parameter in target.GetParameters()) {
            parts.Add(Kind(parameter.ParameterType));
        }

        return string.Join(" | ", parts.ToArray());
    }

    private static string Kind(Type type) {
        string flags = string.Empty;
#if NET
        flags = " isFnPtr=" + type.IsFunctionPointer;
#endif
        return "name='" + type.Name + "' full='" + (type.FullName ?? "<null>") + "' elem=" + type.HasElementType + flags;
    }

    private static string CecilOperands(MethodBase target) {
        string path = target.Module.FullyQualifiedName;
        if (!File.Exists(path)) {
            return "no module file on disk: " + path;
        }

        using Mono.Cecil.ModuleDefinition module = Mono.Cecil.ModuleDefinition.ReadModule(path);
        if (module.LookupToken(target.MetadataToken) is not Mono.Cecil.MethodDefinition definition) {
            return "token did not resolve";
        }

        if (!definition.HasBody) {
            return "no body";
        }

        List<string> parts = new List<string>();
        foreach (Mono.Cecil.Cil.Instruction instruction in definition.Body.Instructions) {
            if (instruction.Operand is Mono.Cecil.CallSite callSite) {
                parts.Add(instruction.OpCode.Name + " callsite " + callSite.FullName);
            }

            if (instruction.Operand is Mono.Cecil.FieldReference field) {
                parts.Add(instruction.OpCode.Name + " field " + field.FieldType.FullName);
            }

            if (instruction.Operand is Mono.Cecil.TypeReference type) {
                parts.Add(instruction.OpCode.Name + " type " + type.FullName);
            }
        }

        return parts.Count + " operands [" + string.Join(", ", parts.ToArray()) + "]";
    }

    private static string Name(Type? type) {
        if (type is null) {
            return "<null>";
        }

        try {
            return type.FullName ?? type.ToString();
        } catch (Exception failure) {
            return "<" + failure.GetType().Name + ">";
        }
    }

    private static string Hex(byte[] blob) {
        int take = blob.Length < 32 ? blob.Length : 32;
        char[] digits = "0123456789abcdef".ToCharArray();
        char[] text = new char[take * 2];
        for (int i = 0; i < take; i++) {
            text[i * 2] = digits[blob[i] >> 4];
            text[(i * 2) + 1] = digits[blob[i] & 0xF];
        }

        return new string(text) + (take < blob.Length ? "..." : string.Empty);
    }

    private static string TopFrame(Exception failure) {
        string[] lines = (failure.StackTrace ?? string.Empty).Split('\n');
        return lines.Length > 0 ? lines[0].Trim() : "<no stack>";
    }

    private static string Flatten(string text) {
        string one = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return one.Length > 240 ? one.Substring(0, 240) + "..." : one;
    }

    private static void Report(string marker, string kind, string detail) {
        File.AppendAllText(marker, "RESULT\t" + kind + "\t" + detail + "\n");
        Console.Out.WriteLine("RESULT " + kind + " " + detail);
        Console.Out.Flush();
    }
}
