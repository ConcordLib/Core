using System.Reflection;

namespace Concord.Emit;

internal sealed class InjectedMemberMap {
    private readonly Dictionary<string, FieldInfo> fields;
    private readonly Dictionary<string, MethodInfo?> methods;

    public InjectedMemberMap(Dictionary<string, FieldInfo> fields, Dictionary<string, MethodInfo?> methods) {
        this.fields = fields;
        this.methods = methods;
    }

    public static string MethodKey(MethodBase method) {
        return method.Module.ModuleVersionId + ":" + method.MetadataToken;
    }

    public bool TryGetField(string name, out FieldInfo field) {
        return fields.TryGetValue(name, out field!);
    }

    public bool TryGetMethod(MethodBase method, out MethodInfo? target) {
        return methods.TryGetValue(MethodKey(method), out target);
    }
}
