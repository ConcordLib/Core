namespace Concord;

/// <summary>
///     Marks a declaration property as the patched target instance for explicit-target declarations.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectInstanceAttribute : Attribute { }

/// <summary>
///     Marks a declaration field as an injected field declaration for a field on the patched target type.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
[JetBrains.Annotations.MeansImplicitUse(
    JetBrains.Annotations.ImplicitUseKindFlags.Access | JetBrains.Annotations.ImplicitUseKindFlags.Assign)]
public sealed class InjectFieldAttribute : Attribute {
    /// <summary>Initializes a declaration that resolves to a target field with the same name.</summary>
    public InjectFieldAttribute() { }

    /// <summary>Initializes a declaration that resolves to the named target field.</summary>
    /// <param name="targetName">The target field name.</param>
    public InjectFieldAttribute(string targetName) {
        TargetName = targetName;
    }

    /// <summary>Gets the target member name, or <see langword="null" /> to use the declaration name.</summary>
    public string? TargetName { get; }
}

/// <summary>
///     Marks a declaration property as an injected property declaration for a property on the patched target type.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectPropertyAttribute : Attribute {
    /// <summary>Initializes a declaration that resolves to a target property with the same name.</summary>
    public InjectPropertyAttribute() { }

    /// <summary>Initializes a declaration that resolves to the named target property.</summary>
    /// <param name="targetName">The target property name.</param>
    public InjectPropertyAttribute(string targetName) {
        TargetName = targetName;
    }

    /// <summary>Gets the target member name, or <see langword="null" /> to use the declaration name.</summary>
    public string? TargetName { get; }
}

/// <summary>
///     Marks a declaration method as an injected member declaration for a method on the patched target type.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InjectMethodAttribute : Attribute {
    /// <summary>Initializes a declaration that resolves to a target method with the same name.</summary>
    public InjectMethodAttribute() { }

    /// <summary>Initializes a declaration that resolves to the named target method.</summary>
    /// <param name="targetName">The target method name.</param>
    public InjectMethodAttribute(string targetName) {
        TargetName = targetName;
    }

    /// <summary>Gets the target member name, or <see langword="null" /> to use the declaration name.</summary>
    public string? TargetName { get; }
}
