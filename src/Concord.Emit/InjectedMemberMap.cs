using System.Reflection;

namespace Concord.Emit;

internal sealed class InjectedMemberMap {
    private readonly Dictionary<string, FieldInfo> fields;
    private readonly Dictionary<string, MethodInfo?> methods;
    private readonly Dictionary<string, AttachedFieldSlot> attached;
    private readonly string? declarationTypeName;

    public InjectedMemberMap(Dictionary<string, FieldInfo> fields, Dictionary<string, MethodInfo?> methods, Dictionary<string, AttachedFieldSlot> attached, string? declarationTypeName = null) {
        this.fields = fields;
        this.methods = methods;
        this.attached = attached;
        this.declarationTypeName = declarationTypeName;
    }

    public static string MethodKey(MethodBase method) {
        return method.Module.ModuleVersionId + ":" + method.MetadataToken;
    }

    // A field is only ours when the declaration owns it. Without this an injection body that touches a
    // same-named field on some other object gets rewritten to reach the declaration's member instead.
    public bool Owns(string? declaringTypeName) {
        return declarationTypeName is null || declaringTypeName == declarationTypeName;
    }

    public bool TryGetField(string name, out FieldInfo field) {
        return fields.TryGetValue(name, out field!);
    }

    public bool TryGetAttached(string name, out AttachedFieldSlot slot) {
        return attached.TryGetValue(name, out slot);
    }

    public bool TryGetMethod(MethodBase method, out MethodInfo? target) {
        return methods.TryGetValue(MethodKey(method), out target);
    }
}
