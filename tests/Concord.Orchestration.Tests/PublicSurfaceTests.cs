using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Concord.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Concord.Orchestration.Tests;

/// <summary>
///     Guards the shape of the shipped API. <c>Concord.Ref.csproj</c> globs every <c>*.cs</c> in the
///     five implementation projects into the reference assembly mods compile against, so a stray
///     <c>public</c> on an internal helper ships Mono.Cecil or MonoMod in Concord's published surface,
///     and taking it back out later is a breaking change. Both are <c>PrivateAssets="all"</c> in the ref
///     package, so either one on a public member breaks a mod's compile.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "SYSLIB1045", Justification = "GeneratedRegex needs net7 or later and this suite also targets net472.")]
public sealed class PublicSurfaceTests {
    // NonPublic is here for protected members: they are part of the surface a mod can reach by
    // deriving, and BindingFlags.Public alone does not return them. VisibleOutsideAssembly drops the
    // private and internal ones NonPublic also brings in.
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] BannedNamespaces = ["Mono.Cecil", "MonoMod"];

    // A whole project joining the ref assembly, flat or recursive. Both forms mean every public
    // member under that directory ships, so both have to be scanned. The captured group is a
    // DIRECTORY name, compared below against an ASSEMBLY name. Those agree for all five today, but
    // Concord.Ref.csproj itself sets AssemblyName to Concord, so a ref-globbed project overriding
    // AssemblyName would red here and the reason would not be obvious. Map it by hand if that happens.
    private static readonly Regex WholeProjectGlob = new Regex(@"^\.\./([^/]+)/(\*\*/)?\*\.cs$", RegexOptions.CultureInvariant);

    // Linked source out of src/Shared, which is not a project and produces no assembly of its own,
    // so it has no name to scan. `../Shared/Polyfills.cs` is the only instance today, but a glob is
    // the obvious edit the day Shared grows a second file, so both shapes are allowed. Deliberately
    // pinned to that directory: a linked file from a real project directory puts that project's
    // source in the ref assembly while leaving the project unscanned, which is the same premise
    // break a partial glob is.
    private static readonly Regex SharedDirectoryInclude = new Regex(@"^\.\./Shared/(\*\*/)?[^/]+\.cs$", RegexOptions.CultureInvariant);

    [Fact]
    public void NoPublicMemberExposesCecilOrMonoMod() {
        List<string> leaks = new List<string>();

        foreach (Assembly assembly in RefGlobbedAssemblies()) {
            Type[] types = ScannedTypes(assembly);

            // An assembly that yields no types would pass this test having tested nothing, which is
            // the failure mode the whole file exists to prevent.
            Assert.NotEmpty(types);

            foreach (Type type in types) {
                foreach (Type used in DeclarationTypes(type)) {
                    if (BannedNamespaces.Any(banned => IsIn(used, banned))) {
                        leaks.Add($"{assembly.GetName().Name}: {type.FullName} declaration exposes {used.FullName}");
                    }
                }

                foreach (MemberInfo member in type.GetMembers(Declared).Where(VisibleOutsideAssembly)) {
                    // Nested types come back from GetMembers too, and ScannedTypes already walks them
                    // as types in their own right, which reaches strictly more than a signature walk.
                    if (member is Type) {
                        continue;
                    }

                    foreach (Type used in SignatureTypes(member)) {
                        if (BannedNamespaces.Any(banned => IsIn(used, banned))) {
                            leaks.Add($"{assembly.GetName().Name}: {type.FullName}.{member.Name} exposes {used.FullName}");
                        }
                    }
                }
            }
        }

        // Assert.Empty truncates its collection, so the one time this fires you cannot read what leaked.
        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
    }

    // Read out of Concord.Ref.csproj itself rather than restated here. A sixth project joining the
    // ref assembly reds this test, which is the whole point: the scan list has to come from the file
    // that decides it, not from a copy of that file kept in step by hand.
    [Fact]
    public void EveryRefGlobbedAssemblyIsScanned() {
        Assert.Equal(
            RefGlobbedProjectNames(),
            RefGlobbedAssemblies().Select(assembly => assembly.GetName().Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    private static Assembly[] RefGlobbedAssemblies() {
        return [
            typeof(LocalAttribute).Assembly,
            typeof(Concord.Detour.IDetourBackend).Assembly,
            typeof(Concord.AttachedData.AttachedStorage).Assembly,
            typeof(Concord.PatchAttribute).Assembly,
            typeof(Concord.ExtendedEnum<>).Assembly,
        ];
    }

    // An allowlist, not a denylist. Anything that is neither a whole-project glob nor linked Shared source
    // fails here rather than getting skipped, because a partial include such as
    // `..\Concord.NewThing\Sub\*.cs` breaks the premise this whole test rests on: that the ref
    // assembly's surface is the union of the assemblies below. Scanning by name would not catch it.
    private static IEnumerable<string> RefGlobbedProjectNames() {
        string path = Path.Combine(AppContext.BaseDirectory, "Concord.Ref.csproj");
        Assert.True(File.Exists(path), $"Concord.Ref.csproj was not copied to {AppContext.BaseDirectory}.");

        List<string> names = new List<string>();
        foreach (XElement compile in XDocument.Load(path).Descendants("Compile")) {
            // Remove narrows an existing glob and Update only attaches metadata. Neither changes
            // which projects the ref assembly compiles, and neither carries an Include to classify.
            // MSBuild trims an item's Include, so trim before matching or a stray space reds a
            // csproj that builds.
            string? attribute = (string?)compile.Attribute("Include");
            if (attribute is null) {
                continue;
            }

            string include = attribute.Replace('\\', '/').Trim();

            // Shared is tested first because it also fits WholeProjectGlob's shape. Matched the
            // other way round, `../Shared/*.cs` would be recorded as an assembly name that does not
            // exist and red a correct build.
            if (SharedDirectoryInclude.IsMatch(include)) {
                continue;
            }

            Match project = WholeProjectGlob.Match(include);
            if (project.Success) {
                names.Add(project.Groups[1].Value);
                continue;
            }

            Assert.Fail(
                $"Concord.Ref.csproj has a Compile Include this guard does not recognise: '{include}'. " +
                "It is neither a whole-project glob nor linked source out of Shared, so the ref assembly's " +
                "surface is no longer the union of the assemblies this test scans. Teach the guard the shape.");
        }

        Assert.NotEmpty(names);
        return names.Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    private static bool IsIn(Type type, string bannedNamespace) {
        string? candidate = type.Namespace;
        return candidate is not null
            && candidate.StartsWith(bannedNamespace, StringComparison.Ordinal)
            && (candidate.Length == bannedNamespace.Length || candidate[bannedNamespace.Length] == '.');
    }

    private static bool VisibleOutsideAssembly(MemberInfo member) {
        switch (member) {
            case FieldInfo field:
                return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
            case MethodBase method:
                return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
            case PropertyInfo property:
                return property.GetAccessors(true).Any(VisibleOutsideAssembly);
            case EventInfo declaredEvent:
                return new MethodInfo?[] { declaredEvent.AddMethod, declaredEvent.RemoveMethod }
                    .Any(accessor => accessor is not null && VisibleOutsideAssembly(accessor));
            case Type nested:
                return nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamORAssem;
            default:
                return false;
        }
    }

    // The composer's copy and the analyzer's copy cannot be unified: the analyzer targets
    // netstandard2.0 and reads At values out of the user's compilation, while the composer switches
    // on a runtime InjectAt graph, so neither can call the other. Reading both out of the built
    // assemblies is the only drift control left.
    [Fact]
    public void PositionHelpMatchesTheAnalyzersTwin() {
        FieldInfo composer = typeof(WrapperComposer).GetField("LocalPositionHelp", BindingFlags.NonPublic | BindingFlags.Static)
                             ?? throw new InvalidOperationException("WrapperComposer has no const 'LocalPositionHelp'.");

        Assert.Equal(composer.GetRawConstantValue(), AnalyzerConstant("LocalPositionHelp"));
    }

    [Fact]
    public void WriteHelpMatchesTheAnalyzersTwin() {
        FieldInfo composer = typeof(WrapperComposer).GetField("LocalWriteHelp", BindingFlags.NonPublic | BindingFlags.Static)
                             ?? throw new InvalidOperationException("WrapperComposer has no const 'LocalWriteHelp'.");

        Assert.Equal(composer.GetRawConstantValue(), AnalyzerConstant("LocalWriteHelp"));
    }

    [Fact]
    public void LocalBindingPositionsMatchTheAnalyzersTwin() {
        Assert.Equal(ComposerAcceptedPositions("SupportsLocalBinding"), AnalyzerAcceptedPositions("SupportsLocalBinding"));
    }

    [Fact]
    public void LocalWritePositionsMatchTheAnalyzersTwin() {
        Assert.Equal(ComposerAcceptedPositions("SupportsLocalWrite"), AnalyzerAcceptedPositions("SupportsLocalWrite"));
    }

    [Fact]
    public void CancelSkipPositionsMatchTheAnalyzersTwin() {
        Assert.Equal(ComposerAcceptedPositions("SupportsCancelSkip"), AnalyzerAcceptedPositions("SupportsCancelSkip"));
    }

    private static string[] ComposerAcceptedPositions(string predicateName) {
        MethodInfo predicate = typeof(WrapperComposer).GetMethod(
                                   predicateName, BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(InjectAt)], null)
                               ?? throw new InvalidOperationException($"WrapperComposer has no private static '{predicateName}(InjectAt)'.");
        SortedSet<string> accepted = new SortedSet<string>(StringComparer.Ordinal);
        string[]? callSiteShifts = null;
        string firstCallSite = string.Empty;

        foreach (Type position in typeof(InjectAt).GetNestedTypes().Where(candidate => typeof(InjectAt).IsAssignableFrom(candidate))) {
            if (!TakesAShift(position)) {
                if (Accepts(predicate, Construct(position, At.Head))) {
                    accepted.Add(position.Name);
                }

                continue;
            }

            string[] shifts = ((At[])Enum.GetValues(typeof(At)))
                .Where(shift => Accepts(predicate, Construct(position, shift)))
                .Select(shift => shift.ToString())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (callSiteShifts is null) {
                callSiteShifts = shifts;
                firstCallSite = position.Name;
            } else {
                Assert.True(
                    callSiteShifts.SequenceEqual(shifts),
                    $"InjectAt.{firstCallSite} accepts shifts [{string.Join(", ", callSiteShifts)}] but InjectAt.{position.Name} accepts "
                    + $"[{string.Join(", ", shifts)}]. The analyzer's twin runs one shared call-site branch and cannot express a "
                    + "difference between them.");
            }

            accepted.UnionWith(shifts);
        }

        return accepted.ToArray();
    }

    private static bool Accepts(MethodInfo predicate, InjectAt at) {
        return (bool)predicate.Invoke(null, [at])!;
    }

    private static bool TakesAShift(Type position) {
        return PrimaryConstructor(position).GetParameters().Any(parameter => parameter.ParameterType == typeof(At));
    }

    private static ConstructorInfo PrimaryConstructor(Type position) {
        return position.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();
    }

    private static InjectAt Construct(Type position, At shift) {
        ConstructorInfo constructor = PrimaryConstructor(position);
        object?[] arguments = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(At) ? (object?)shift
                : parameter.ParameterType == typeof(Type) ? typeof(PublicSurfaceTests)
                : parameter.ParameterType == typeof(string) ? "Anything"
                : parameter.ParameterType == typeof(object) ? 1
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType)
                : null)
            .ToArray();

        try {
            return (InjectAt)constructor.Invoke(arguments);
        } catch (Exception failure) {
            throw new InvalidOperationException(
                $"PublicSurfaceTests cannot construct InjectAt.{position.Name}, so it cannot ask either predicate about it.", failure);
        }
    }

    private static string[] AnalyzerAcceptedPositions(string predicateName) {
        string path = Path.Combine(AppContext.BaseDirectory, "Concord.Analyzers.dll");
        using ModuleDefinition module = ModuleDefinition.ReadModule(path);
        TypeDefinition type = module.GetType("Concord.Analyzers.InjectedMemberAnalyzer")
                              ?? throw new InvalidOperationException("Concord.Analyzers.dll has no InjectedMemberAnalyzer.");
        MethodDefinition method = type.Methods.FirstOrDefault(candidate => candidate.Name == predicateName)
                                  ?? throw new InvalidOperationException($"InjectedMemberAnalyzer has no method '{predicateName}'.");
        SortedSet<string> names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Instruction instruction in method.Body.Instructions) {
            if (instruction.OpCode == OpCodes.Ldstr) {
                names.Add((string)instruction.Operand);
            }
        }

        Assert.True(
            names.Count > 0,
            $"InjectedMemberAnalyzer.{predicateName} compiled to no string literals, so this test can no longer read its position "
            + "list out of the IL and is passing without comparing anything.");

        return names.ToArray();
    }

    // Read out of the analyzer's metadata rather than by loading the type: InjectedMemberAnalyzer
    // derives from a Roslyn base class that this assembly does not reference, so Type.GetType on it
    // throws before it can reach the field.
    private static object AnalyzerConstant(string name) {
        string path = Path.Combine(AppContext.BaseDirectory, "Concord.Analyzers.dll");
        using ModuleDefinition module = ModuleDefinition.ReadModule(path);
        TypeDefinition type = module.GetType("Concord.Analyzers.InjectedMemberAnalyzer")
                              ?? throw new InvalidOperationException("Concord.Analyzers.dll has no InjectedMemberAnalyzer.");
        FieldDefinition field = type.Fields.FirstOrDefault(candidate => candidate.Name == name)
                                ?? throw new InvalidOperationException($"InjectedMemberAnalyzer has no const '{name}'.");
        return field.Constant;
    }

    // GetExportedTypes stops at public-all-the-way, so a nested protected type on a public
    // unsealed class is missing from it while still being reachable by deriving.
    private static Type[] ScannedTypes(Assembly assembly) {
        List<Type> found = new List<Type>();
        Queue<Type> pending = new Queue<Type>(assembly.GetExportedTypes());

        while (pending.Count > 0) {
            Type type = pending.Dequeue();
            found.Add(type);
            foreach (Type nested in type.GetNestedTypes(Declared).Where(VisibleOutsideAssembly)) {
                pending.Enqueue(nested);
            }
        }

        return found.Distinct().ToArray();
    }

    // Explicit interface implementations need no case: the method is private, but the interface
    // that forces the reference is in GetInterfaces.
    private static IEnumerable<Type> DeclarationTypes(Type type) {
        IEnumerable<Type> declared = type.BaseType is null ? [] : Expand(type.BaseType);

        return declared
            .Concat(type.GetInterfaces().SelectMany(Expand))
            .Concat(GenericParameters(type).SelectMany(Expand))
            .Concat(AttributeTypes(type));
    }

    // GetGenericArguments returns the parameters only on a definition; on a constructed type it
    // returns the arguments, which belong to whatever named that type.
    private static IEnumerable<Type> GenericParameters(MethodBase method) {
        return method.IsGenericMethodDefinition ? method.GetGenericArguments() : [];
    }

    private static IEnumerable<Type> GenericParameters(Type type) {
        return type.IsGenericTypeDefinition ? type.GetGenericArguments() : [];
    }

    // Read rather than instantiated, so a missing attribute assembly cannot throw here. Not covered:
    // custom modifiers and default parameter values, neither of which C# can point at a Cecil type.
    private static IEnumerable<Type> AttributeTypes(MemberInfo member) {
        return member.GetCustomAttributesData().SelectMany(AttributeTypes);
    }

    private static IEnumerable<Type> AttributeTypes(ParameterInfo parameter) {
        return parameter.GetCustomAttributesData().SelectMany(AttributeTypes);
    }

    private static IEnumerable<Type> AttributeTypes(CustomAttributeData attribute) {
        return Expand(attribute.AttributeType)
            .Concat(attribute.ConstructorArguments.SelectMany(ArgumentTypes))
            .Concat(attribute.NamedArguments.Select(named => named.TypedValue).SelectMany(ArgumentTypes));
    }

    private static IEnumerable<Type> ArgumentTypes(CustomAttributeTypedArgument argument) {
        IEnumerable<Type> declared = Expand(argument.ArgumentType);

        return argument.Value is Type value ? declared.Concat(Expand(value)) : declared;
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) {
        IEnumerable<Type> attributes = AttributeTypes(member);

        switch (member) {
            case FieldInfo field:
                return attributes.Concat(Expand(field.FieldType));
            case PropertyInfo property:
                return attributes
                    .Concat(Expand(property.PropertyType))
                    .Concat(property.GetIndexParameters().SelectMany(ParameterTypes));
            case MethodInfo method:
                return attributes
                    .Concat(Expand(method.ReturnType))
                    .Concat(AttributeTypes(method.ReturnParameter))
                    .Concat(method.GetParameters().SelectMany(ParameterTypes))
                    .Concat(GenericParameters(method).SelectMany(Expand));
            case ConstructorInfo constructor:
                return attributes
                    .Concat(constructor.GetParameters().SelectMany(ParameterTypes))
                    .Concat(GenericParameters(constructor).SelectMany(Expand));
            case EventInfo declaredEvent:
                return declaredEvent.EventHandlerType is null ? attributes : attributes.Concat(Expand(declaredEvent.EventHandlerType));
            default:
                return attributes;
        }
    }

    private static IEnumerable<Type> ParameterTypes(ParameterInfo parameter) {
        return Expand(parameter.ParameterType).Concat(AttributeTypes(parameter));
    }

    // A Cecil type hides just as well inside IReadOnlyList<VariableDefinition> or VariableDefinition[]
    // as it does on its own, so unwrap arrays, byrefs and generic arguments before testing.
    private static IEnumerable<Type> Expand(Type type) {
        List<Type> found = new List<Type>();
        Expand(type, new HashSet<Type>(), found);
        return found;
    }

    // A generic parameter stands in for its constraints: T is namespaced to whoever declared it, so
    // `where T : MethodDefinition` is only visible through GetGenericParameterConstraints.
    private static void Expand(Type type, HashSet<Type> seen, List<Type> found) {
        // `where T : IComparable<T>` walks back into T, so the visited set is load-bearing.
        if (!seen.Add(type)) {
            return;
        }

        if (type.HasElementType) {
            Expand(type.GetElementType()!, seen, found);
            return;
        }

        if (type.IsGenericParameter) {
            foreach (Type constraint in type.GetGenericParameterConstraints()) {
                Expand(constraint, seen, found);
            }

            return;
        }

        found.Add(type);

        if (type.IsGenericType) {
            foreach (Type argument in type.GetGenericArguments()) {
                Expand(argument, seen, found);
            }
        }
    }
}
