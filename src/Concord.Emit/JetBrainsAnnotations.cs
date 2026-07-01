namespace JetBrains.Annotations;

/// <summary>
///     Defines how a declaration is implicitly used.
/// </summary>
[Flags]
public enum ImplicitUseKindFlags {
    /// <summary>The declaration is accessed, assigned, and instantiated with fixed constructor signature.</summary>
    Default = Access | Assign | InstantiatedWithFixedConstructorSignature,

    /// <summary>The declaration is implicitly accessed.</summary>
    Access = 1,

    /// <summary>The declaration is implicitly assigned.</summary>
    Assign = 2,

    /// <summary>The declaration is implicitly instantiated with fixed constructor signature.</summary>
    InstantiatedWithFixedConstructorSignature = 4,

    /// <summary>The declaration is implicitly instantiated with no fixed constructor signature.</summary>
    InstantiatedNoFixedConstructorSignature = 8,
}

/// <summary>
///     Defines which declarations are implicitly used.
/// </summary>
[Flags]
public enum ImplicitUseTargetFlags {
    /// <summary>The marked declaration itself is implicitly used.</summary>
    Default = Itself,

    /// <summary>The marked declaration itself is implicitly used.</summary>
    Itself = 1,

    /// <summary>The marked type's members are implicitly used.</summary>
    Members = 2,

    /// <summary>The marked type's inheritors are implicitly used.</summary>
    WithInheritors = 4,

    /// <summary>The marked type and its members are implicitly used.</summary>
    WithMembers = Itself | Members,
}

/// <summary>
///     Marks another attribute as implying use of the declaration it is applied to.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter | AttributeTargets.GenericParameter)]
public sealed class MeansImplicitUseAttribute : Attribute {
    /// <summary>Initializes a new instance of the <see cref="MeansImplicitUseAttribute" /> class.</summary>
    public MeansImplicitUseAttribute()
        : this(ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.Default) { }

    /// <summary>Initializes a new instance of the <see cref="MeansImplicitUseAttribute" /> class.</summary>
    /// <param name="useKindFlags">The implied use kind.</param>
    public MeansImplicitUseAttribute(ImplicitUseKindFlags useKindFlags)
        : this(useKindFlags, ImplicitUseTargetFlags.Default) { }

    /// <summary>Initializes a new instance of the <see cref="MeansImplicitUseAttribute" /> class.</summary>
    /// <param name="targetFlags">The implied use target.</param>
    public MeansImplicitUseAttribute(ImplicitUseTargetFlags targetFlags)
        : this(ImplicitUseKindFlags.Default, targetFlags) { }

    /// <summary>Initializes a new instance of the <see cref="MeansImplicitUseAttribute" /> class.</summary>
    /// <param name="useKindFlags">The implied use kind.</param>
    /// <param name="targetFlags">The implied use target.</param>
    public MeansImplicitUseAttribute(ImplicitUseKindFlags useKindFlags, ImplicitUseTargetFlags targetFlags) {
        UseKindFlags = useKindFlags;
        TargetFlags = targetFlags;
    }

    /// <summary>Gets the implied use kind.</summary>
    public ImplicitUseKindFlags UseKindFlags { get; }

    /// <summary>Gets the implied use target.</summary>
    public ImplicitUseTargetFlags TargetFlags { get; }
}
