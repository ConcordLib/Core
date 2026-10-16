using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

internal static partial class InfixMapping {
    private const int MaxArity = 8;

    private static readonly object Gate = new object();

    private static readonly MethodInfo ReadCaptureMethod =
        typeof(InfixMapping).GetMethod(nameof(ReadCapture), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly Dictionary<(MethodInfo Method, Type? Outer), int> Slots =
        new Dictionary<(MethodInfo Method, Type? Outer), int>();

    private static Infix[] infixes = Array.Empty<Infix>();

    internal static string? Validate(MethodBase original, Patch patch) {
        InnerMethod? selector = patch.innerMethod;
        if (selector == null) {
            return Refuse(original, "Concord maps only method-call infixes");
        }

        MethodInfo inner = selector.Method;
        if (inner.DeclaringType == null) {
            return Refuse(original, "the inner method " + inner.Name + " has no declaring type");
        }

        if (inner.IsGenericMethod || inner.DeclaringType.ContainsGenericParameters) {
            return Refuse(original, "the inner method " + inner.Name + " is generic");
        }

        ParameterInfo[] innerParameters = inner.GetParameters();
        if (innerParameters.Length > MaxArity) {
            return Refuse(original, "the inner method " + inner.Name + " takes more than " + MaxArity + " arguments");
        }

        foreach (ParameterInfo parameter in innerParameters) {
            if (parameter.ParameterType.IsByRef || parameter.ParameterType.IsPointer) {
                return Refuse(original, "the inner method " + inner.Name + " takes " + parameter.Name + " by reference");
            }
        }

        foreach (int position in selector.positions) {
            if (position < 0) {
                return Refuse(original, "a negative inner position (" + position + ") is not mapped yet");
            }
        }

        MethodInfo method = patch.PatchMethod;
        if (!method.IsStatic) {
            return Refuse(original, "the patch method " + method.Name + " is not static");
        }

        foreach (ParameterInfo parameter in method.GetParameters()) {
            string? reason = ValidateParameter(original, inner, innerParameters, parameter);
            if (reason != null) {
                return reason;
            }
        }

        return null;
    }

    internal static IReadOnlyList<Injection> Map(MethodBase original, Patch patch, bool isPrefix) {
        InnerMethod selector = patch.innerMethod;
        MethodInfo inner = selector.Method;
        ParameterInfo[] innerParameters = inner.GetParameters();
        bool hasSelf = !inner.IsStatic;
        bool isVoid = inner.ReturnType == typeof(void);
        Type? outerType = NeedsOuter(patch.PatchMethod) ? original.DeclaringType : null;

        int slot = Register(original, patch.PatchMethod, inner, innerParameters, isPrefix, outerType);
        MethodInfo shim = Shim(innerParameters.Length, isVoid, hasSelf, outerType);
        Type[] shimArguments = ShimArguments(inner, innerParameters, hasSelf, isVoid);
        if (shimArguments.Length > 0) {
            shim = shim.MakeGenericMethod(shimArguments);
        }

        Type[] parameterTypes = new Type[innerParameters.Length];
        for (int i = 0; i < innerParameters.Length; i++) {
            parameterTypes[i] = innerParameters[i].ParameterType;
        }

        List<Injection> injections = new List<Injection>();
        foreach (uint by in Occurrences(selector.positions)) {
            injections.Add(new Injection(shim, new InjectAt.Invoke(inner.DeclaringType!, inner.Name, At.Around, by, parameterTypes), patch.owner, patch.priority) {
                ForeignOrigin = true,
                BoundArguments = new Dictionary<string, object?> { ["slot"] = slot },
                BeforeOwners = patch.before,
                AfterOwners = patch.after,
            });
        }

        return injections;
    }

    private static IReadOnlyList<uint> Occurrences(int[] positions) {
        if (positions.Length == 0) {
            return new uint[] { 0 };
        }

        List<uint> occurrences = new List<uint>();
        foreach (int position in positions) {
            occurrences.Add((uint)position);
        }

        return occurrences;
    }

    private static Type[] ShimArguments(MethodInfo inner, ParameterInfo[] innerParameters, bool hasSelf, bool isVoid) {
        List<Type> arguments = new List<Type>();
        if (hasSelf) {
            arguments.Add(inner.DeclaringType!);
        }

        foreach (ParameterInfo parameter in innerParameters) {
            arguments.Add(parameter.ParameterType);
        }

        if (!isVoid) {
            arguments.Add(inner.ReturnType);
        }

        return arguments.ToArray();
    }

    private static string? ValidateParameter(MethodBase original, MethodInfo inner, ParameterInfo[] innerParameters, ParameterInfo parameter) {
        string name = parameter.Name ?? string.Empty;
        if (HasOuterAttribute(parameter)) {
            return ValidateCaptured(original, parameter);
        }

        if (name == "__instance") {
            if (inner.IsStatic) {
                return Refuse(original, "the patch reads __instance but the inner method " + inner.Name + " is static");
            }

            if (parameter.ParameterType.IsByRef) {
                return Refuse(original, "the patch takes __instance by reference");
            }

            return null;
        }

        if (name == "__result") {
            if (inner.ReturnType == typeof(void)) {
                return Refuse(original, "the patch reads __result but the inner method " + inner.Name + " returns void");
            }

            return null;
        }

        if (name.StartsWith("__", StringComparison.Ordinal)) {
            return Refuse(original, "the patch parameter " + name + " is not mapped yet");
        }

        foreach (ParameterInfo innerParameter in innerParameters) {
            if (innerParameter.Name == name) {
                return null;
            }
        }

        return Refuse(original, "the patch parameter " + name + " matches no argument of the inner method " + inner.Name);
    }

    private static string? ValidateCaptured(MethodBase original, ParameterInfo parameter) {
        string name = parameter.Name ?? string.Empty;
        string? realName = CapturedName(parameter);
        if (realName == null) {
            return Refuse(original, "the patch parameter " + name + " reads outer scope, which is not mapped yet");
        }

        if (parameter.ParameterType.IsByRef) {
            return Refuse(original, "the patch parameter " + name + " writes a captured outer variable, which is not mapped yet");
        }

        string? reason = ResolveCapture(original, realName, out FieldInfo[]? chain);
        if (reason != null) {
            return Refuse(original, "the patch parameter " + name + " " + reason);
        }

        Type captured = chain![chain.Length - 1].FieldType;
        if (!parameter.ParameterType.IsAssignableFrom(captured)) {
            return Refuse(
                original,
                "the patch parameter " + name + " declares " + parameter.ParameterType.Name + " but the captured variable '" +
                realName + "' is " + captured.Name);
        }

        return null;
    }

    private static string? CapturedName(ParameterInfo parameter) {
        HarmonyArgument? argument = parameter.GetCustomAttribute<HarmonyArgument>();
        if (argument == null) {
            return null;
        }

        object? mode = typeof(HarmonyArgument)
            .GetProperty("Mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(argument);
        if (mode == null || mode.ToString() != "Captured") {
            return null;
        }

        return argument.NewName;
    }

    private static string? ResolveCapture(MethodBase original, string realName, out FieldInfo[]? chain) {
        chain = null;
        Type? root = original.DeclaringType;
        if (original.IsStatic || root == null) {
            return "reads captured variable '" + realName + "' but " + original.Name + " has no instance to read it from";
        }

        if (!IsGeneratedScope(root)) {
            return "reads captured variable '" + realName + "' but " + root.Name + " is not a compiler-generated closure";
        }

        List<FieldInfo[]> found = new List<FieldInfo[]>();
        VisitScope(root, Array.Empty<FieldInfo>(), new HashSet<Type>(), realName, found);
        if (found.Count == 0) {
            return "reads captured variable '" + realName + "', which is not reachable from " + root.Name;
        }

        if (found.Count > 1) {
            return "reads captured variable '" + realName + "', which is ambiguous in " + root.Name;
        }

        chain = found[0];
        return null;
    }

    private static void VisitScope(Type type, FieldInfo[] path, HashSet<Type> ancestors, string realName, List<FieldInfo[]> found) {
        if (!IsGeneratedScope(type) || ancestors.Contains(type)) {
            return;
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        foreach (FieldInfo field in fields) {
            if (CaptureMatches(field.Name, realName) && (realName != "this" || !IsGeneratedScope(field.FieldType))) {
                FieldInfo[] resolved = new FieldInfo[path.Length + 1];
                Array.Copy(path, resolved, path.Length);
                resolved[path.Length] = field;
                found.Add(resolved);
            }
        }

        HashSet<Type> seen = new HashSet<Type>(ancestors) { type };
        foreach (FieldInfo field in fields) {
            if (!IsGeneratedScope(field.FieldType) || !IsClosureLink(field.Name)) {
                continue;
            }

            FieldInfo[] linked = new FieldInfo[path.Length + 1];
            Array.Copy(path, linked, path.Length);
            linked[path.Length] = field;
            VisitScope(field.FieldType, linked, seen, realName, found);
        }
    }

    private static bool CaptureMatches(string fieldName, string realName) {
        if (fieldName.StartsWith("<>3__", StringComparison.Ordinal)) {
            return false;
        }

        if (realName == "this") {
            return fieldName == "<>4__this";
        }

        return fieldName == realName || fieldName.StartsWith("<" + realName + ">5__", StringComparison.Ordinal);
    }

    private static bool IsClosureLink(string name) {
        return name == "<>4__this" || name.StartsWith("CS$<>8__locals", StringComparison.Ordinal) ||
               name.StartsWith("<>8__", StringComparison.Ordinal);
    }

    private static bool IsGeneratedScope(Type type) {
        return type.Name.IndexOf('<') >= 0 && type.IsDefined(typeof(CompilerGeneratedAttribute), false);
    }

    private static bool NeedsOuter(MethodInfo method) {
        foreach (ParameterInfo parameter in method.GetParameters()) {
            if (HasOuterAttribute(parameter)) {
                return true;
            }
        }

        return false;
    }

    private static object? ReadCapture(object? root, FieldInfo[] chain) {
        object? current = root;
        foreach (FieldInfo field in chain) {
            if (current == null) {
                return null;
            }

            current = field.GetValue(current);
        }

        return current;
    }

    private static bool HasOuterAttribute(ParameterInfo parameter) {
        foreach (CustomAttributeData attribute in parameter.GetCustomAttributesData()) {
            string name = attribute.AttributeType.Name;
            if (name == "HarmonyOuter" || name == "HarmonyOuterAttribute") {
                return true;
            }
        }

        return false;
    }

    private static string Refuse(MethodBase original, string reason) {
        return "Harmony carries an infix on '" + original.Name + "' and " + reason + ", so this target cannot be composed.";
    }

    private static int Register(
        MethodBase original,
        MethodInfo method,
        MethodInfo inner,
        ParameterInfo[] innerParameters,
        bool isPrefix,
        Type? outerType) {
        lock (Gate) {
            if (Slots.TryGetValue((method, outerType), out int existing)) {
                return existing;
            }

            Infix infix = new Infix();
            if (isPrefix) {
                infix.Prefix = BuildPrefix(original, method, inner, innerParameters);
            } else {
                infix.Postfix = BuildPostfix(original, method, inner, innerParameters);
            }

            int slot = infixes.Length;
            Infix[] grown = new Infix[slot + 1];
            Array.Copy(infixes, grown, slot);
            grown[slot] = infix;
            infixes = grown;
            Slots[(method, outerType)] = slot;
            return slot;
        }
    }

    private static InfixPrefix BuildPrefix(MethodBase original, MethodInfo method, MethodInfo inner, ParameterInfo[] innerParameters) {
        ParameterExpression self = Expression.Parameter(typeof(object), "self");
        ParameterExpression outer = Expression.Parameter(typeof(object), "outer");
        ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
        ParameterExpression result = Expression.Parameter(typeof(object).MakeByRefType(), "result");

        Binding binding = Bind(original, method, inner, innerParameters, self, outer, args, result);
        List<Expression> body = new List<Expression>(binding.Load);
        ParameterExpression ran = Expression.Variable(typeof(bool), "ran");
        List<ParameterExpression> locals = new List<ParameterExpression>(binding.Locals) { ran };

        MethodCallExpression call = Expression.Call(method, binding.Arguments);
        if (method.ReturnType == typeof(bool)) {
            body.Add(Expression.Assign(ran, call));
        } else {
            body.Add(call);
            body.Add(Expression.Assign(ran, Expression.Constant(true)));
        }

        body.AddRange(binding.Store);
        body.Add(ran);

        return Expression.Lambda<InfixPrefix>(Expression.Block(locals, body), self, outer, args, result).Compile();
    }

    private static InfixPostfix BuildPostfix(MethodBase original, MethodInfo method, MethodInfo inner, ParameterInfo[] innerParameters) {
        ParameterExpression self = Expression.Parameter(typeof(object), "self");
        ParameterExpression outer = Expression.Parameter(typeof(object), "outer");
        ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
        ParameterExpression incoming = Expression.Parameter(typeof(object), "result");
        ParameterExpression result = Expression.Variable(typeof(object), "carried");

        Binding binding = Bind(original, method, inner, innerParameters, self, outer, args, result);
        List<ParameterExpression> locals = new List<ParameterExpression>(binding.Locals) { result };
        List<Expression> body = new List<Expression> { Expression.Assign(result, incoming) };
        body.AddRange(binding.Load);

        MethodCallExpression call = Expression.Call(method, binding.Arguments);
        if (method.ReturnType == typeof(void)) {
            body.Add(call);
        } else {
            body.Add(Expression.Assign(result, Expression.Convert(call, typeof(object))));
        }

        body.AddRange(binding.Store);
        body.Add(result);

        return Expression.Lambda<InfixPostfix>(Expression.Block(locals, body), self, outer, args, incoming).Compile();
    }

    private static Binding Bind(
        MethodBase original,
        MethodInfo method,
        MethodInfo inner,
        ParameterInfo[] innerParameters,
        ParameterExpression self,
        ParameterExpression outer,
        ParameterExpression args,
        Expression result) {
        Binding binding = new Binding();
        foreach (ParameterInfo parameter in method.GetParameters()) {
            string name = parameter.Name ?? string.Empty;
            Expression source;
            Expression? target;
            if (HasOuterAttribute(parameter)) {
                ResolveCapture(original, CapturedName(parameter)!, out FieldInfo[]? chain);
                source = Expression.Call(ReadCaptureMethod, outer, Expression.Constant(chain));
                target = null;
            } else if (name == "__instance") {
                source = self;
                target = null;
            } else if (name == "__result") {
                source = result;
                target = result;
            } else {
                int index = IndexOf(innerParameters, name);
                source = Expression.ArrayIndex(args, Expression.Constant(index));
                target = Expression.ArrayAccess(args, Expression.Constant(index));
            }

            if (!parameter.ParameterType.IsByRef) {
                binding.Arguments.Add(Cast(source, parameter.ParameterType));
                continue;
            }

            Type elementType = parameter.ParameterType.GetElementType()!;
            ParameterExpression local = Expression.Variable(elementType, name);
            binding.Locals.Add(local);
            binding.Load.Add(Expression.Assign(local, Cast(source, elementType)));
            binding.Arguments.Add(local);
            if (target != null) {
                binding.Store.Add(Expression.Assign(target, Expression.Convert(local, typeof(object))));
            }
        }

        _ = inner;
        return binding;
    }

    private static Expression Cast(Expression source, Type type) {
        if (type == typeof(object)) {
            return source;
        }

        return Expression.Convert(source, type);
    }

    private static int IndexOf(ParameterInfo[] innerParameters, string name) {
        for (int i = 0; i < innerParameters.Length; i++) {
            if (innerParameters[i].Name == name) {
                return i;
            }
        }

        throw new ArgumentException("No inner argument named " + name);
    }

    private sealed class Binding {
        internal List<ParameterExpression> Locals { get; } = new List<ParameterExpression>();

        internal List<Expression> Load { get; } = new List<Expression>();

        internal List<Expression> Store { get; } = new List<Expression>();

        internal List<Expression> Arguments { get; } = new List<Expression>();
    }
}
