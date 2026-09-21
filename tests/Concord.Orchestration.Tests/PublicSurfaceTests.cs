using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Concord.Emit;
using Mono.Cecil;
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
            foreach (Type type in assembly.GetExportedTypes()) {
                foreach (MemberInfo member in type.GetMembers(Declared).Where(VisibleOutsideAssembly)) {
                    foreach (Type used in SignatureTypes(member)) {
                        if (BannedNamespaces.Any(banned => IsIn(used, banned))) {
                            leaks.Add($"{assembly.GetName().Name}: {type.FullName}.{member.Name} exposes {used.FullName}");
                        }
                    }
                }
            }
        }

        Assert.Empty(leaks);
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

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) {
        switch (member) {
            case FieldInfo field:
                return Expand(field.FieldType);
            case PropertyInfo property:
                return Expand(property.PropertyType).Concat(property.GetIndexParameters().SelectMany(p => Expand(p.ParameterType)));
            case MethodInfo method:
                return Expand(method.ReturnType).Concat(method.GetParameters().SelectMany(p => Expand(p.ParameterType)));
            case ConstructorInfo constructor:
                return constructor.GetParameters().SelectMany(p => Expand(p.ParameterType));
            case EventInfo declaredEvent:
                return declaredEvent.EventHandlerType is null ? [] : Expand(declaredEvent.EventHandlerType);
            default:
                return [];
        }
    }

    // A Cecil type hides just as well inside IReadOnlyList<VariableDefinition> or VariableDefinition[]
    // as it does on its own, so unwrap arrays, byrefs and generic arguments before testing.
    private static IEnumerable<Type> Expand(Type type) {
        if (type.HasElementType) {
            return Expand(type.GetElementType()!);
        }

        return type.IsGenericType
            ? new[] { type }.Concat(type.GetGenericArguments().SelectMany(Expand))
            : [type];
    }
}
