using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;
using HarmonyLib;

namespace Concord.Waylith.RimProbe;

using CodeInstruction = HarmonyLib.CodeInstruction;

/// <summary>
///     Offline census of which real Harmony patch methods Concord's foreign-patch mappers would refuse.
///     Reads metadata only; <c>verify</c> checks the reimplementation against the real validators.
/// </summary>
public static class Census {
    private const string ResultName = "__result";

    private const string ExceptionName = "__exception";

    private const string CodeInstructionName = "HarmonyLib.CodeInstruction";

    private const string EnumerableName = "System.Collections.Generic.IEnumerable`1";

    private static readonly string[] EnumerableLikeNames = {
        EnumerableName,
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
    };

    private static readonly Dictionary<string, string> RoleAttributes = new Dictionary<string, string> {
        ["HarmonyLib.HarmonyPrefix"] = "Prefix",
        ["HarmonyLib.HarmonyPostfix"] = "Postfix",
        ["HarmonyLib.HarmonyTranspiler"] = "Transpiler",
        ["HarmonyLib.HarmonyFinalizer"] = "Finalizer",
        ["HarmonyLib.HarmonyReversePatch"] = "ReversePatch",
    };

    private static readonly HashSet<string> RoleNames = new HashSet<string> { "Prefix", "Postfix", "Transpiler", "Finalizer" };

    private static readonly List<string> SearchDirs = new List<string>();

    public static int Run(string[] args) {
        if (args.Length == 0) {
            Console.Error.WriteLine("usage: scan (<dll>... | --from-tsv <reach-census.tsv>) [--game <Assembly-CSharp.dll>] [--out <tsv>]");
            Console.Error.WriteLine("       verify --bridge <Concord.Harmony.dll> --concord <dir> --harmony <dir>");
            return 2;
        }

        string outPath = null;
        string gamePath = null;
        string fromTsv = null;
        string bridge = null;
        List<string> positional = new List<string>();
        for (int i = 1; i < args.Length; i++) {
            switch (args[i]) {
                case "--out": outPath = args[++i]; break;
                case "--game": gamePath = args[++i]; break;
                case "--from-tsv": fromTsv = args[++i]; break;
                case "--bridge": bridge = args[++i]; break;
                case "--concord": SearchDirs.Add(args[++i]); break;
                case "--harmony": SearchDirs.Add(args[++i]); break;
                default: positional.Add(args[i]); break;
            }
        }

        AssemblyLoadContext.Default.Resolving += ResolveFromSearchDirs;

        switch (args[0]) {
            case "scan":
                if (fromTsv != null) {
                    positional.AddRange(PathsFromTsv(fromTsv));
                }

                return Scan(positional, gamePath, outPath);
            case "verify":
                return Verify(bridge);
            default:
                Console.Error.WriteLine("unknown mode '" + args[0] + "'");
                return 2;
        }
    }

    private static Assembly ResolveFromSearchDirs(AssemblyLoadContext context, AssemblyName name) {
        foreach (string dir in SearchDirs) {
            string candidate = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(candidate)) {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> PathsFromTsv(string tsv) {
        string[] lines = File.ReadAllLines(tsv);
        int column = Array.IndexOf(lines[0].Split('\t'), "path");
        return lines.Skip(1).Select(line => line.Split('\t')[column]).Distinct(StringComparer.Ordinal);
    }

    private static int Scan(List<string> paths, string gamePath, string outPath) {
        TypeIndex game = gamePath == null ? null : TypeIndex.Open(gamePath);
        List<Row> rows = new List<Row>();
        int scanned = 0;
        int failed = 0;
        int innerApiReferences = 0;
        foreach (string path in paths) {
            try {
                Module module = Module.Open(path, game);
                rows.AddRange(module.Rows());
                innerApiReferences += module.ReferencesInnerPatchApi ? 1 : 0;
                scanned++;
            } catch (Exception error) {
                failed++;
                Console.Error.WriteLine("load failed\t" + path + "\t" + error.GetType().Name + ": " + error.Message);
            }
        }

        if (outPath != null) {
            WriteTsv(rows, outPath);
        }

        Summarize(rows, scanned, failed, innerApiReferences, game);
        return 0;
    }

    private static int ByRefReturningMethods(TypeIndex index) {
        int count = 0;
        TyProvider provider = index.Provider;
        foreach (MethodDefinitionHandle handle in index.Reader.MethodDefinitions) {
            if (index.Reader.GetMethodDefinition(handle).DecodeSignature(provider, null).ReturnType.IsByRef) {
                count++;
            }
        }

        return count;
    }

    private static void WriteTsv(List<Row> rows, string outPath) {
        using StreamWriter writer = new StreamWriter(outPath, false, new UTF8Encoding(false));
        writer.WriteLine("assembly\tmod\ttype\tmethod\trole\tstatic\treturn\tparams\tfirstRefusal\tparamKinds\tunchecked\ttargetKey\ttargetStatus\ttargetReturn\tgeneric\tpath");
        foreach (Row row in rows) {
            writer.WriteLine(string.Join("\t",
                row.Assembly, row.Mod, row.Type, row.Method, row.Role, row.IsStatic ? "static" : "instance", row.Return,
                row.Params, row.FirstRefusal ?? string.Empty, string.Join("+", row.ParamKinds), row.Unchecked ? "True" : "False",
                row.TargetKey, row.TargetStatus, row.TargetReturn ?? string.Empty, row.Generic, row.Path));
        }
    }

    private static void Summarize(List<Row> rows, int scanned, int failed, int innerApiReferences, TypeIndex game) {
        Console.WriteLine("assemblies scanned\t" + scanned);
        Console.WriteLine("assemblies failed to load\t" + failed);
        Console.WriteLine("patch methods, mapped roles\t" + rows.Count);
        Console.WriteLine("assemblies referencing AddInnerPrefix/AddInnerPostfix\t" + innerApiReferences);
        Console.WriteLine("game index\t" + (game != null ? "yes" : "no"));
        if (game != null) {
            Console.WriteLine("game methods returning by-ref (bounds the skipped target-byref rule)\t" + ByRefReturningMethods(game) + "\tof\t" + game.Reader.MethodDefinitions.Count);
        }

        Console.WriteLine("accepted without the target-byref check (target not resolved)\t" + rows.Count(row => row.FirstRefusal == null && row.TargetStatus != "resolved"));

        foreach (IGrouping<string, Row> role in rows.GroupBy(row => row.Role).OrderBy(group => group.Key)) {
            int refused = role.Count(row => row.FirstRefusal != null);
            int uncheckedCount = role.Count(row => row.FirstRefusal == null && row.Unchecked);
            Console.WriteLine("role\t" + role.Key + "\ttotal\t" + role.Count() + "\trefused\t" + refused + "\tunchecked\t" + uncheckedCount +
                "\taccepted\t" + (role.Count() - refused - uncheckedCount));
        }

        Console.WriteLine();
        Console.WriteLine("first refusal per bucket (the reason Validate returns first)");
        foreach (IGrouping<string, Row> bucket in rows.Where(row => row.FirstRefusal != null)
            .GroupBy(row => row.Role + "\t" + row.FirstRefusal).OrderByDescending(group => group.Count())) {
            Console.WriteLine("bucket\t" + bucket.Key + "\t" + bucket.Count());
        }

        Console.WriteLine();
        Console.WriteLine("parameter kinds present on refused prefix/postfix/finalizer methods (a method counts once per kind)");
        foreach (IGrouping<string, Row> kind in rows.Where(row => row.FirstRefusal != null && row.Role != "Transpiler")
            .SelectMany(row => row.ParamKinds.Select(k => new { k, row })).GroupBy(pair => pair.k, pair => pair.row)
            .OrderByDescending(group => group.Count())) {
            Console.WriteLine("kind\t" + kind.Key + "\t" + kind.Count());
        }

        Console.WriteLine();
        Console.WriteLine("target resolution");
        foreach (IGrouping<string, Row> status in rows.GroupBy(row => row.TargetStatus).OrderByDescending(group => group.Count())) {
            Console.WriteLine("target status\t" + status.Key + "\t" + status.Count());
        }

        Console.WriteLine();
        Console.WriteLine("generic verdict on the declared target type (per patch method)");
        foreach (IGrouping<string, Row> generic in rows.GroupBy(row => row.Generic).OrderByDescending(group => group.Count())) {
            Console.WriteLine("generic\t" + generic.Key + "\t" + generic.Count());
        }

        Console.WriteLine();
        List<IGrouping<string, Row>> targets = rows.GroupBy(row => row.TargetKey).ToList();
        int targetsRefused = targets.Count(t => t.Any(row => row.FirstRefusal != null || row.Generic == "ref-inst" || row.Generic == "open"));
        int targetsUnchecked = targets.Count(t => t.All(row => row.FirstRefusal == null && row.Generic != "ref-inst" && row.Generic != "open") && t.Any(row => row.Unchecked || row.Generic == "inst-unknown"));
        int targetsUnkeyed = targets.Count(t => t.Key.StartsWith("?", StringComparison.Ordinal));
        Console.WriteLine("targets (distinct declared target keys)\t" + targets.Count);
        Console.WriteLine("targets keyed only by patch class (target not declared by attribute)\t" + targetsUnkeyed);
        Console.WriteLine("targets non-suppressible (at least one patch refused)\t" + targetsRefused);
        Console.WriteLine("targets unchecked (no refusal, but a target-side rule could not run)\t" + targetsUnchecked);
        Console.WriteLine("targets accepted (every patch accepted, every rule ran)\t" + (targets.Count - targetsRefused - targetsUnchecked));
        Console.WriteLine("targets non-suppressible fraction\t" + Fraction(targetsRefused, targets.Count));
        int multiMod = targets.Count(t => t.Select(row => row.Mod).Distinct().Count() > 1);
        Console.WriteLine("targets patched by more than one mod\t" + multiMod);

        Console.WriteLine();
        Console.WriteLine("mods by refused patch methods (top 15)");
        int totalRefused = rows.Count(row => row.FirstRefusal != null);
        int running = 0;
        foreach (IGrouping<string, Row> mod in rows.Where(row => row.FirstRefusal != null).GroupBy(row => row.Mod)
            .OrderByDescending(group => group.Count()).Take(15)) {
            running += mod.Count();
            int modTotal = rows.Count(row => row.Mod == mod.Key);
            Console.WriteLine("mod\t" + mod.Key + "\t" + mod.First().Assembly + "\trefused\t" + mod.Count() + "\tof\t" + modTotal +
                "\tcumulative share of all refusals\t" + Fraction(running, totalRefused));
        }

        int modsWithPatches = rows.Select(row => row.Mod).Distinct().Count();
        int modsFullyClean = rows.GroupBy(row => row.Mod).Count(group => group.All(row => row.FirstRefusal == null));
        Console.WriteLine("mods with mapped patch methods\t" + modsWithPatches);
        Console.WriteLine("mods with every patch method accepted or unchecked\t" + modsFullyClean);
        Console.WriteLine("mods with at least one refusal\t" + (modsWithPatches - modsFullyClean));
        Console.WriteLine();
        Console.WriteLine("patch methods refused\t" + totalRefused + "\tof\t" + rows.Count + "\t" + Fraction(totalRefused, rows.Count));
    }

    private static string Fraction(int part, int whole) {
        return whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private static int Verify(string bridgePath) {
        if (bridgePath == null) {
            Console.Error.WriteLine("verify needs --bridge <Concord.Harmony.dll>");
            return 2;
        }

        Assembly bridge = Assembly.LoadFrom(bridgePath);
        Assembly concord = ResolveFromSearchDirs(AssemblyLoadContext.Default, new AssemblyName("Concord"));
        Type prefixMapping = bridge.GetType("Concord.Harmony.PrefixMapping", true);
        Type postfixMapping = bridge.GetType("Concord.Harmony.PostfixMapping", true);
        Type finalizerShape = bridge.GetType("Concord.Harmony.HarmonyFinalizerShape", true);
        Type transpilerAdapter = bridge.GetType("Concord.Harmony.HarmonyTranspilerAdapter", true);
        Type composer = concord.GetType("Concord.Emit.WrapperComposer", true);
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        Console.WriteLine("bridge\t" + bridge.Location + "\tmvid=" + bridge.ManifestModule.ModuleVersionId);
        Console.WriteLine("concord\t" + concord.Location + "\tmvid=" + concord.ManifestModule.ModuleVersionId);
        Console.WriteLine("harmony\t" + typeof(HarmonyLib.Harmony).Assembly.Location + "\tversion=" + typeof(HarmonyLib.Harmony).Assembly.GetName().Version);
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            string simple = loaded.GetName().Name;
            if (simple == "0Harmony" || simple == "Concord" || simple == "Concord.Harmony") {
                Console.WriteLine("loaded\t" + simple + "\t" + loaded.Location + "\tmvid=" + loaded.ManifestModule.ModuleVersionId);
            }
        }

        Assembly self = typeof(Census).Assembly;
        Module module = Module.Open(self.Location, null);
        List<Row> rows = module.Rows().Where(row => row.Type.Contains("CensusFixture")).ToList();

        int agreements = 0;
        int disagreements = 0;
        int uncheckedRows = 0;
        HashSet<string> buckets = new HashSet<string>();
        HashSet<string> acceptedRoles = new HashSet<string>();
        HashSet<string> genericVerdicts = new HashSet<string>();
        int genericAgreements = 0;
        int genericDisagreements = 0;

        foreach (Row row in rows) {
            Type patchClass = self.GetType(row.Type.Replace('/', '+'), true);
            MethodInfo patch = patchClass.GetMethod(row.Method, any | BindingFlags.Instance);
            HarmonyMethod merged = HarmonyMethodExtensions.GetMergedFromType(patchClass);
            MethodBase target = AccessTools.DeclaredMethod(merged.declaringType, merged.methodName, merged.argumentTypes);
            if (target == null) {
                Console.WriteLine("FAIL\t" + row.Type + "." + row.Method + "\tHarmony could not resolve the fixture target");
                disagreements++;
                continue;
            }

            string real;
            switch (row.Role) {
                case "Prefix": real = (string)prefixMapping.GetMethod("Validate", any).Invoke(null, new object[] { target, patch }); break;
                case "Postfix": real = (string)postfixMapping.GetMethod("Validate", any).Invoke(null, new object[] { target, patch }); break;
                case "Finalizer": real = (string)finalizerShape.GetMethod("Validate", any).Invoke(null, new object[] { target, patch }); break;
                case "Transpiler": real = (string)transpilerAdapter.GetMethod("Validate", any).Invoke(null, new object[] { patch }); break;
                default: continue;
            }

            if (row.Role == "Transpiler") {
                Type bridgeView = bridge.GetType("Concord.Harmony.HarmonyTranspile", true).GetMethod("Invoke").ReturnType;
                if (bridgeView != typeof(IEnumerable<CodeInstruction>)) {
                    Console.WriteLine("FAIL identity\t" + row.Method + "\tprobe sees " + typeof(IEnumerable<CodeInstruction>).AssemblyQualifiedName +
                        "\tbridge sees " + bridgeView.AssemblyQualifiedName);
                    disagreements++;
                }
            }

            string realGeneric = RealGenericRefusal(target, composer);
            bool realRefusedGeneric = realGeneric != null;
            bool censusRefusedGeneric = row.Generic == "ref-inst" || row.Generic == "open";
            genericVerdicts.Add(row.Generic);
            if (realRefusedGeneric == censusRefusedGeneric) {
                genericAgreements++;
            } else {
                genericDisagreements++;
                Console.WriteLine("FAIL generic\t" + row.Type + "\treal=" + (realGeneric ?? "accepted") + "\tcensus=" + row.Generic);
            }

            if (row.Unchecked && row.FirstRefusal == null) {
                uncheckedRows++;
                Console.WriteLine("unchecked\t" + row.Type + "." + row.Method + "\treal=" + (real == null ? "accepted" : "refused") + "\tcensus=no verdict offline");
                continue;
            }

            bool realRefused = real != null;
            bool censusRefused = row.FirstRefusal != null;
            if (realRefused == censusRefused) {
                agreements++;
                if (censusRefused) {
                    buckets.Add(row.Role + ":" + row.FirstRefusal);
                } else {
                    acceptedRoles.Add(row.Role);
                }

                Console.WriteLine("agree\t" + row.Type + "." + row.Method + "\t" + (censusRefused ? "refused " + row.FirstRefusal : "accepted") + "\treal=" + Flatten(real));
            } else {
                disagreements++;
                Console.WriteLine("FAIL\t" + row.Type + "." + row.Method + "\tcensus=" + (row.FirstRefusal ?? "accepted") + "\treal=" + Flatten(real));
            }
        }

        Console.WriteLine();
        Console.WriteLine("fixture patch methods\t" + rows.Count);
        Console.WriteLine("agreements\t" + agreements);
        Console.WriteLine("disagreements\t" + disagreements);
        Console.WriteLine("unchecked offline\t" + uncheckedRows);
        Console.WriteLine("distinct refusal buckets produced\t" + buckets.Count);
        Console.WriteLine("roles with an accepted fixture method\t" + acceptedRoles.Count);
        Console.WriteLine("generic agreements\t" + genericAgreements);
        Console.WriteLine("generic disagreements\t" + genericDisagreements);
        Console.WriteLine("distinct generic verdicts produced\t" + genericVerdicts.Count);

        bool ok = disagreements == 0 && genericDisagreements == 0 && buckets.Count >= 12 && acceptedRoles.Count == 4
                  && genericVerdicts.Count >= 3 && uncheckedRows == 1 && rows.Count >= 30;
        Console.WriteLine(ok ? "verify PASS" : "verify FAIL");
        return ok ? 0 : 1;
    }

    private static string RealGenericRefusal(MethodBase target, Type composer) {
        if (target.ContainsGenericParameters) {
            return "open generic";
        }

        if ((bool)composer.GetMethod("SharesGenericBody", new[] { typeof(MethodBase) }).Invoke(null, new object[] { target })) {
            return "shares generic body";
        }

        try {
            composer.GetMethod("RejectSharedGenericInstantiation", new[] { typeof(MethodBase) }).Invoke(null, new object[] { target });
            return null;
        } catch (TargetInvocationException thrown) {
            return thrown.InnerException?.Message ?? thrown.Message;
        }
    }

    private static string Flatten(string text) {
        return text == null ? "accepted" : text.Replace('\t', ' ').Replace('\n', ' ');
    }

    private sealed class Row {
        public string Assembly;
        public string Mod;
        public string Path;
        public string Type;
        public string Method;
        public string Role;
        public bool IsStatic;
        public string Return;
        public string Params;
        public string FirstRefusal;
        public bool Unchecked;
        public List<string> ParamKinds = new List<string>();
        public string TargetKey;
        public string TargetStatus;
        public string TargetReturn;
        public string Generic;
    }

    private sealed class Ty {
        public static readonly Ty[] None = Array.Empty<Ty>();
        public string Name;
        public string Kind;
        public Ty Element;
        public Ty[] Args = None;
        public EntityHandle Handle;
        public PrimitiveTypeCode Code;
        public string Assembly;

        public bool IsByRef => Kind == "byref";

        public Ty Deref => IsByRef ? Element : this;

        public override string ToString() {
            switch (Kind) {
                case "byref": return Element + "&";
                case "ptr": return Element + "*";
                case "array": return Element + "[]";
                case "inst": return Name + "<" + string.Join(",", Args.Select(arg => arg.ToString())) + ">";
                default: return Name;
            }
        }
    }

    private sealed class TyProvider : ISignatureTypeProvider<Ty, object>, ICustomAttributeTypeProvider<Ty> {
        private readonly MetadataReader reader;

        public TyProvider(MetadataReader reader) {
            this.reader = reader;
        }

        public Ty GetPrimitiveType(PrimitiveTypeCode code) {
            return new Ty { Name = "System." + PrimitiveName(code), Kind = "prim", Code = code };
        }

        public Ty GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind) {
            return new Ty { Name = FullName(r, handle), Kind = "def", Handle = handle };
        }

        public Ty GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind) {
            return new Ty { Name = FullName(r, handle), Kind = "ref", Handle = handle };
        }

        public Ty GetTypeFromSpecification(MetadataReader r, object context, TypeSpecificationHandle handle, byte rawTypeKind) {
            return r.GetTypeSpecification(handle).DecodeSignature(this, context);
        }

        public Ty GetSZArrayType(Ty element) {
            return new Ty { Name = element.Name + "[]", Kind = "array", Element = element };
        }

        public Ty GetArrayType(Ty element, ArrayShape shape) {
            return new Ty { Name = element.Name + "[,]", Kind = "array", Element = element };
        }

        public Ty GetByReferenceType(Ty element) {
            return new Ty { Name = element.Name + "&", Kind = "byref", Element = element };
        }

        public Ty GetPointerType(Ty element) {
            return new Ty { Name = element.Name + "*", Kind = "ptr", Element = element };
        }

        public Ty GetGenericInstantiation(Ty generic, ImmutableArray<Ty> args) {
            return new Ty { Name = generic.Name, Kind = "inst", Handle = generic.Handle, Element = generic, Args = args.ToArray() };
        }

        public Ty GetGenericMethodParameter(object context, int index) {
            return new Ty { Name = "!!" + index, Kind = "gparam" };
        }

        public Ty GetGenericTypeParameter(object context, int index) {
            return new Ty { Name = "!" + index, Kind = "gparam" };
        }

        public Ty GetFunctionPointerType(MethodSignature<Ty> signature) {
            return new Ty { Name = "fnptr", Kind = "fnptr" };
        }

        public Ty GetModifiedType(Ty modifier, Ty unmodified, bool isRequired) {
            return unmodified;
        }

        public Ty GetPinnedType(Ty element) {
            return element;
        }

        public Ty GetSystemType() {
            return new Ty { Name = "System.Type", Kind = "ref" };
        }

        public bool IsSystemType(Ty type) {
            return type.Name == "System.Type";
        }

        public Ty GetTypeFromSerializedName(string name) {
            int index = 0;
            return ParseSerialized(name, ref index);
        }

        private static Ty ParseSerialized(string text, ref int index) {
            StringBuilder name = new StringBuilder();
            while (index < text.Length && text[index] != '[' && text[index] != ',' && text[index] != ']') {
                name.Append(text[index++]);
            }

            Ty type = new Ty { Name = name.ToString().Trim().Replace('+', '/'), Kind = "named" };
            while (index + 1 < text.Length && text[index] == '[') {
                if (text[index + 1] == '[') {
                    index++;
                    List<Ty> args = new List<Ty>();
                    while (index < text.Length && text[index] == '[') {
                        index++;
                        args.Add(ParseSerialized(text, ref index));
                        if (index < text.Length && text[index] == ']') {
                            index++;
                        }

                        if (index < text.Length && text[index] == ',') {
                            index++;
                        }
                    }

                    if (index < text.Length && text[index] == ']') {
                        index++;
                    }

                    type = new Ty { Name = type.Name, Kind = "inst", Element = type, Args = args.ToArray() };
                } else if (text[index + 1] == ']') {
                    index += 2;
                    type = new Ty { Name = type.Name + "[]", Kind = "array", Element = type };
                } else {
                    break;
                }
            }

            if (index < text.Length && text[index] == ',') {
                index++;
                StringBuilder assembly = new StringBuilder();
                while (index < text.Length && text[index] != ']' && text[index] != ',') {
                    assembly.Append(text[index++]);
                }

                while (index < text.Length && text[index] != ']') {
                    index++;
                }

                string owner = assembly.ToString().Trim();
                for (Ty current = type; current != null; current = current.Element) {
                    current.Assembly ??= owner;
                }
            }

            return type;
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(Ty type) {
            return PrimitiveTypeCode.Int32;
        }

        private static string PrimitiveName(PrimitiveTypeCode code) {
            switch (code) {
                case PrimitiveTypeCode.Boolean: return "Boolean";
                case PrimitiveTypeCode.Byte: return "Byte";
                case PrimitiveTypeCode.SByte: return "SByte";
                case PrimitiveTypeCode.Char: return "Char";
                case PrimitiveTypeCode.Int16: return "Int16";
                case PrimitiveTypeCode.UInt16: return "UInt16";
                case PrimitiveTypeCode.Int32: return "Int32";
                case PrimitiveTypeCode.UInt32: return "UInt32";
                case PrimitiveTypeCode.Int64: return "Int64";
                case PrimitiveTypeCode.UInt64: return "UInt64";
                case PrimitiveTypeCode.Single: return "Single";
                case PrimitiveTypeCode.Double: return "Double";
                case PrimitiveTypeCode.IntPtr: return "IntPtr";
                case PrimitiveTypeCode.UIntPtr: return "UIntPtr";
                case PrimitiveTypeCode.Object: return "Object";
                case PrimitiveTypeCode.String: return "String";
                case PrimitiveTypeCode.TypedReference: return "TypedReference";
                case PrimitiveTypeCode.Void: return "Void";
                default: return code.ToString();
            }
        }
    }

    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle) {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);
        TypeDefinitionHandle declaring = definition.GetDeclaringType();
        if (!declaring.IsNil) {
            return FullName(reader, declaring) + "/" + name;
        }

        string ns = reader.GetString(definition.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string FullName(MetadataReader reader, TypeReferenceHandle handle) {
        TypeReference reference = reader.GetTypeReference(handle);
        string name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference) {
            return FullName(reader, (TypeReferenceHandle)reference.ResolutionScope) + "/" + name;
        }

        string ns = reader.GetString(reference.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute) {
        switch (attribute.Constructor.Kind) {
            case HandleKind.MemberReference: {
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind == HandleKind.TypeReference ? FullName(reader, (TypeReferenceHandle)member.Parent)
                    : member.Parent.Kind == HandleKind.TypeDefinition ? FullName(reader, (TypeDefinitionHandle)member.Parent) : null;
            }

            case HandleKind.MethodDefinition: {
                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return FullName(reader, method.GetDeclaringType());
            }

            default:
                return null;
        }
    }

    private sealed class Declared {
        public Ty Type;
        public string TypeName;
        public string MethodName;
        public int? MethodType;
        public Ty[] ArgTypes;

        public Declared Over(Declared detail) {
            return new Declared {
                Type = detail.Type ?? Type,
                TypeName = detail.TypeName ?? TypeName,
                MethodName = detail.MethodName ?? MethodName,
                MethodType = detail.MethodType ?? MethodType,
                ArgTypes = detail.ArgTypes ?? ArgTypes,
            };
        }
    }

    private sealed class TypeIndex {
        public MetadataReader Reader;
        public TyProvider Provider;
        public Dictionary<string, TypeDefinitionHandle> Types = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        private PEReader pe;

        public static TypeIndex Open(string path) {
            TypeIndex index = new TypeIndex { pe = new PEReader(File.OpenRead(path)) };
            index.Reader = index.pe.GetMetadataReader();
            index.Provider = new TyProvider(index.Reader);
            foreach (TypeDefinitionHandle handle in index.Reader.TypeDefinitions) {
                index.Types[FullName(index.Reader, handle)] = handle;
            }

            return index;
        }
    }

    private sealed class Module {
        private readonly string path;
        private readonly PEReader pe;
        private readonly MetadataReader reader;
        private readonly TyProvider provider;
        private readonly TypeIndex own;
        private readonly TypeIndex game;
        private readonly string assemblyName;

        private Module(string path, TypeIndex game) {
            this.path = path;
            this.game = game;
            pe = new PEReader(File.OpenRead(path));
            reader = pe.GetMetadataReader();
            provider = new TyProvider(reader);
            own = new TypeIndex { Reader = reader, Provider = provider };
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions) {
                own.Types[FullName(reader, handle)] = handle;
            }

            assemblyName = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (MemberReferenceHandle handle in reader.MemberReferences) {
                string name = reader.GetString(reader.GetMemberReference(handle).Name);
                if (name == "AddInnerPrefix" || name == "AddInnerPostfix" || name == "AddInnerPrefixes" || name == "AddInnerPostfixes") {
                    ReferencesInnerPatchApi = true;
                }
            }
        }

        public bool ReferencesInnerPatchApi { get; private set; }

        public static Module Open(string path, TypeIndex game) {
            return new Module(path, game);
        }

        public IEnumerable<Row> Rows() {
            string mod = ModOf(path);
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions) {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                Declared classDeclared = null;
                bool patchClass = false;
                foreach (CustomAttributeHandle attributeHandle in type.GetCustomAttributes()) {
                    CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
                    string name = AttributeTypeName(reader, attribute);
                    if (name == "HarmonyLib.HarmonyPatch") {
                        patchClass = true;
                        Declared declared = DecodePatch(attribute);
                        classDeclared = classDeclared == null ? declared : classDeclared.Over(declared);
                    } else if (name == "HarmonyLib.HarmonyPatchAll") {
                        patchClass = true;
                    }
                }

                string typeName = FullName(reader, typeHandle);
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods()) {
                    MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                    string methodName = reader.GetString(method.Name);
                    string role = null;
                    Declared methodDeclared = null;
                    foreach (CustomAttributeHandle attributeHandle in method.GetCustomAttributes()) {
                        CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
                        string name = AttributeTypeName(reader, attribute);
                        if (name == null) {
                            continue;
                        }

                        if (RoleAttributes.TryGetValue(name, out string attributedRole)) {
                            role ??= attributedRole;
                        } else if (name == "HarmonyLib.HarmonyPatch") {
                            Declared declared = DecodePatch(attribute);
                            methodDeclared = methodDeclared == null ? declared : methodDeclared.Over(declared);
                        }
                    }

                    if (role == null && patchClass && RoleNames.Contains(methodName)) {
                        role = methodName;
                    }

                    if (role == null || !RoleNames.Contains(role)) {
                        continue;
                    }

                    Declared target = classDeclared ?? new Declared();
                    if (methodDeclared != null) {
                        target = target.Over(methodDeclared);
                    }

                    MethodSignature<Ty> signature = method.DecodeSignature(provider, null);
                    List<(string Name, Ty Type)> parameters = new List<(string, Ty)>();
                    foreach (ParameterHandle parameterHandle in method.GetParameters()) {
                        Parameter parameter = reader.GetParameter(parameterHandle);
                        if (parameter.SequenceNumber >= 1 && parameter.SequenceNumber <= signature.ParameterTypes.Length) {
                            parameters.Add((reader.GetString(parameter.Name), signature.ParameterTypes[parameter.SequenceNumber - 1]));
                        }
                    }

                    Row row = new Row {
                        Assembly = assemblyName,
                        Mod = mod,
                        Path = path,
                        Type = typeName,
                        Method = methodName,
                        Role = role,
                        IsStatic = (method.Attributes & MethodAttributes.Static) != 0,
                        Return = signature.ReturnType.ToString(),
                        Params = string.Join(", ", parameters.Select(p => p.Type + " " + p.Name)),
                    };

                    Resolved resolved = Resolve(target);
                    row.TargetKey = resolved.Key ?? "?" + assemblyName + "/" + typeName;
                    row.TargetStatus = resolved.Status;
                    row.TargetReturn = resolved.Return?.ToString();
                    row.Generic = GenericVerdict(target.Type);
                    Classify(row, role, signature.ReturnType, parameters, resolved);
                    yield return row;
                }
            }
        }

        private Declared DecodePatch(CustomAttribute attribute) {
            Declared declared = new Declared();
            CustomAttributeValue<Ty> value;
            try {
                value = attribute.DecodeValue(provider);
            } catch (Exception) {
                return declared;
            }

            bool firstString = true;
            foreach (CustomAttributeTypedArgument<Ty> argument in value.FixedArguments) {
                if (argument.Value is Ty type) {
                    declared.Type = type;
                } else if (argument.Value is string text) {
                    if (argument.Type.Name == "System.String" && firstString) {
                        if (declared.Type == null && value.FixedArguments.Length >= 2 && value.FixedArguments[1].Value is string) {
                            declared.TypeName = text;
                        } else {
                            declared.MethodName = text;
                        }

                        firstString = false;
                    } else if (argument.Type.Name == "System.String") {
                        declared.MethodName ??= text;
                    }
                } else if (argument.Type.Name == "HarmonyLib.MethodType" && argument.Value is int methodType) {
                    declared.MethodType = methodType;
                } else if (argument.Value is ImmutableArray<CustomAttributeTypedArgument<Ty>> array && argument.Type.Name == "System.Type[]") {
                    declared.ArgTypes = array.Select(item => item.Value as Ty).ToArray();
                }
            }

            return declared;
        }

        private sealed class Resolved {
            public string Key;
            public string Status;
            public Ty Return;
            public bool IsVoidTarget => Return != null && Return.Name == "System.Void" && Return.Kind == "prim";
        }

        private Resolved Resolve(Declared target) {
            Ty type = target.Type;
            string typeName = type?.Name ?? target.TypeName;
            if (typeName == null) {
                return new Resolved { Status = "undeclared-type" };
            }

            string member;
            switch (target.MethodType ?? 0) {
                case 0: member = target.MethodName; break;
                case 1: member = target.MethodName == null ? null : "get_" + target.MethodName; break;
                case 2: member = target.MethodName == null ? null : "set_" + target.MethodName; break;
                case 3: member = ".ctor"; break;
                case 4: member = ".cctor"; break;
                default: member = null; break;
            }

            string key = typeName + (type != null && type.Kind == "inst" ? "<" + string.Join(",", type.Args.Select(a => a.ToString())) + ">" : string.Empty)
                         + "::" + (member ?? "methodType=" + target.MethodType) +
                         (target.ArgTypes == null ? string.Empty : "(" + string.Join(",", target.ArgTypes.Select(a => a?.ToString() ?? "?")) + ")");
            if (member == null) {
                return new Resolved { Key = key, Status = target.MethodType.HasValue ? "methodType-" + target.MethodType : "undeclared-member" };
            }

            TypeIndex index = type == null ? game : IndexFor(type);
            if (index == null) {
                return new Resolved { Key = key, Status = "external-type" };
            }

            if (!index.Types.TryGetValue(typeName, out TypeDefinitionHandle typeHandle) && !(game != null && game.Types.TryGetValue(typeName, out typeHandle) && (index = game) != null)) {
                return new Resolved { Key = key, Status = "missing-type" };
            }

            for (int depth = 0; depth < 32; depth++) {
                TypeDefinition definition = index.Reader.GetTypeDefinition(typeHandle);
                List<MethodDefinition> candidates = new List<MethodDefinition>();
                foreach (MethodDefinitionHandle methodHandle in definition.GetMethods()) {
                    MethodDefinition method = index.Reader.GetMethodDefinition(methodHandle);
                    if (index.Reader.GetString(method.Name) == member) {
                        candidates.Add(method);
                    }
                }

                if (candidates.Count > 0) {
                    List<MethodSignature<Ty>> signatures = candidates.Select(c => c.DecodeSignature(index.Provider, null)).ToList();
                    if (target.ArgTypes != null) {
                        signatures = signatures.Where(s => s.ParameterTypes.Length == target.ArgTypes.Length
                            && s.ParameterTypes.Zip(target.ArgTypes, (have, want) => want == null || have.Deref.Name == want.Deref.Name).All(same => same)).ToList();
                    }

                    if (signatures.Count == 1) {
                        return new Resolved { Key = key, Status = "resolved", Return = signatures[0].ReturnType };
                    }

                    return new Resolved { Key = key, Status = signatures.Count == 0 ? "missing-member" : "ambiguous" };
                }

                EntityHandle baseHandle = definition.BaseType;
                if (baseHandle.IsNil) {
                    break;
                }

                string baseName = baseHandle.Kind == HandleKind.TypeDefinition ? FullName(index.Reader, (TypeDefinitionHandle)baseHandle)
                    : baseHandle.Kind == HandleKind.TypeReference ? FullName(index.Reader, (TypeReferenceHandle)baseHandle) : null;
                if (baseName == null || !index.Types.TryGetValue(baseName, out TypeDefinitionHandle next)) {
                    if (baseName != null && game != null && game.Types.TryGetValue(baseName, out next)) {
                        index = game;
                        typeHandle = next;
                        continue;
                    }

                    return new Resolved { Key = key, Status = "missing-member" };
                }

                typeHandle = next;
            }

            return new Resolved { Key = key, Status = "missing-member" };
        }

        private TypeIndex IndexFor(Ty type) {
            switch (type.Kind) {
                case "def":
                    return own;
                case "ref":
                    return type.Handle.Kind == HandleKind.TypeReference ? ExternalIndex((TypeReferenceHandle)type.Handle) : null;
                case "inst":
                    return type.Element == null ? null : IndexFor(type.Element);
                case "named":
                    if (type.Assembly == null || type.Assembly == assemblyName) {
                        return own;
                    }

                    return type.Assembly == "Assembly-CSharp" ? game : null;
                default:
                    return null;
            }
        }

        private static readonly HashSet<string> CoreValueTypes = new HashSet<string> {
            "System.Boolean", "System.Byte", "System.SByte", "System.Char", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
            "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal", "System.IntPtr", "System.UIntPtr",
            "System.DateTime", "System.TimeSpan", "System.Guid", "System.Nullable`1", "System.Collections.Generic.KeyValuePair`2",
            "System.ValueTuple`2", "System.ValueTuple`3", "System.ValueTuple`4",
        };

        private static readonly HashSet<string> CoreReferenceTypes = new HashSet<string> {
            "System.String", "System.Object", "System.Type", "System.Delegate", "System.MulticastDelegate", "System.Array", "System.Exception",
            "System.Attribute", "System.Collections.Generic.List`1", "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.IList`1", "System.Func`1", "System.Func`2", "System.Action`1", "System.Action",
        };

        private TypeIndex ExternalIndex(TypeReferenceHandle handle) {
            TypeReference reference = reader.GetTypeReference(handle);
            while (reference.ResolutionScope.Kind == HandleKind.TypeReference) {
                reference = reader.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope);
            }

            if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference) {
                return null;
            }

            string scope = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
            return scope == "Assembly-CSharp" ? game : null;
        }

        private bool? IsValueType(Ty type) {
            switch (type.Kind) {
                case "prim":
                    return type.Code != PrimitiveTypeCode.String && type.Code != PrimitiveTypeCode.Object && type.Code != PrimitiveTypeCode.TypedReference && type.Code != PrimitiveTypeCode.Void;
                case "array":
                case "byref":
                case "ptr":
                    return false;
                case "inst":
                    return type.Element == null ? null : IsValueType(type.Element);
                case "def":
                case "ref":
                case "named": {
                    if (CoreValueTypes.Contains(type.Name)) {
                        return true;
                    }

                    if (CoreReferenceTypes.Contains(type.Name)) {
                        return false;
                    }

                    TypeIndex index = IndexFor(type);
                    if (index == null) {
                        return null;
                    }

                    if (!index.Types.TryGetValue(type.Name, out TypeDefinitionHandle handle)) {
                        if (type.Kind != "named" || type.Assembly != null || game == null || !game.Types.TryGetValue(type.Name, out handle)) {
                            return null;
                        }

                        index = game;
                    }

                    EntityHandle baseHandle = index.Reader.GetTypeDefinition(handle).BaseType;
                    if (baseHandle.IsNil) {
                        return false;
                    }

                    string baseName = baseHandle.Kind == HandleKind.TypeDefinition ? FullName(index.Reader, (TypeDefinitionHandle)baseHandle)
                        : baseHandle.Kind == HandleKind.TypeReference ? FullName(index.Reader, (TypeReferenceHandle)baseHandle) : null;
                    return baseName == "System.ValueType" || baseName == "System.Enum";
                }

                default:
                    return null;
            }
        }

        private string GenericVerdict(Ty type) {
            if (type == null) {
                return "undeclared";
            }

            if (type.Kind == "inst") {
                bool unknown = false;
                foreach (Ty argument in type.Args) {
                    if (argument.Kind == "gparam") {
                        return "open";
                    }

                    bool? valueType = IsValueType(argument);
                    if (valueType == false) {
                        return "ref-inst";
                    }

                    unknown |= valueType == null;
                }

                return unknown ? "inst-unknown" : "value-inst";
            }

            return type.Name.Contains('`') ? "open" : "none";
        }

        private static string KindOf(string name) {
            if (name == "__instance" || name == "__state" || name == "__args" || name == "__originalMethod" || name == "__runOriginal" || name == ExceptionName || name == ResultName) {
                return name;
            }

            if (name.StartsWith("___", StringComparison.Ordinal)) {
                return "___field";
            }

            if (name.StartsWith("__", StringComparison.Ordinal) && name.Length > 2 && name.Skip(2).All(char.IsDigit)) {
                return "__N";
            }

            return "argument";
        }

        private static void Classify(Row row, string role, Ty returnType, List<(string Name, Ty Type)> parameters, Resolved target) {
            if (role != "Transpiler") {
                row.ParamKinds = parameters.Select(p => KindOf(p.Name)).Distinct().ToList();
            }

            if (!row.IsStatic) {
                row.FirstRefusal = "instance";
                return;
            }

            if (role == "Transpiler") {
                ClassifyTranspiler(row, returnType, parameters);
                return;
            }

            bool returnOk = role == "Prefix" ? IsVoid(returnType) || returnType.Name == "System.Boolean"
                : role == "Postfix" ? IsVoid(returnType)
                : IsVoid(returnType) || returnType.Name == "System.Exception" || returnType.Name.EndsWith("Exception", StringComparison.Ordinal);
            if (!returnOk) {
                row.FirstRefusal = "return-type";
                return;
            }

            bool targetKnown = target.Status == "resolved";
            if (targetKnown && target.Return.IsByRef) {
                row.FirstRefusal = "target-byref";
                return;
            }

            bool voidTarget = targetKnown ? target.IsVoidTarget : target.Status == "methodType-3" || target.Status == "methodType-4";
            bool voidKnown = targetKnown || target.Status == "methodType-3" || target.Status == "methodType-4";

            foreach ((string name, Ty type) in parameters) {
                if (role == "Finalizer" && name == ExceptionName) {
                    if (type.IsByRef) {
                        row.FirstRefusal = "exception-byref";
                        return;
                    }

                    if (type.Name != "System.Exception" && type.Name != "System.Object") {
                        row.FirstRefusal = "exception-type";
                        return;
                    }

                    continue;
                }

                if (name != ResultName) {
                    row.FirstRefusal = "param:" + KindOf(name);
                    return;
                }

                if (voidKnown && voidTarget) {
                    row.FirstRefusal = "result-on-void";
                    return;
                }

                if (!voidKnown) {
                    row.Unchecked = true;
                    continue;
                }

                Ty declared = type.Deref;
                if (type.IsByRef && declared.ToString() != target.Return.ToString()) {
                    row.FirstRefusal = "result-type";
                    return;
                }

                if (!type.IsByRef && declared.ToString() != target.Return.ToString() && declared.Name != "System.Object") {
                    row.Unchecked = true;
                }
            }
        }

        private static void ClassifyTranspiler(Row row, Ty returnType, List<(string Name, Ty Type)> parameters) {
            bool returnOk = returnType.Kind == "inst" && EnumerableLikeNames.Contains(returnType.Name) && returnType.Args.Length == 1 && returnType.Args[0].Name == CodeInstructionName
                            || returnType.Kind == "array" && returnType.Element.Name == CodeInstructionName;
            if (!returnOk) {
                row.FirstRefusal = "return-type";
                return;
            }

            foreach ((string name, Ty type) in parameters) {
                bool ok = type.Name == "System.Reflection.Emit.ILGenerator" || type.Name == "System.Reflection.MethodBase"
                          || type.Kind == "inst" && type.Name == EnumerableName && type.Args.Length == 1 && type.Args[0].Name == CodeInstructionName;
                if (!ok) {
                    row.FirstRefusal = "param:" + type.Deref.Name.Split('.', '/').Last();
                    return;
                }
            }
        }

        private static bool IsVoid(Ty type) {
            return type.Kind == "prim" && type.Code == PrimitiveTypeCode.Void;
        }

        private static string ModOf(string path) {
            string[] parts = path.Replace('\\', '/').Split('/');
            int workshop = Array.IndexOf(parts, "294100");
            if (workshop >= 0 && workshop + 1 < parts.Length) {
                return parts[workshop + 1];
            }

            int mods = Array.LastIndexOf(parts, "Mods");
            return mods >= 0 && mods + 1 < parts.Length ? parts[mods + 1] : System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }
}

public static class CensusFixtureTargets {
    public static int IntValue() {
        return 1;
    }

    public static void VoidValue() {
    }

    public static object ObjectValue() {
        return null;
    }
}

[HarmonyPatch(typeof(CensusFixtureTargets), nameof(CensusFixtureTargets.IntValue))]
public class CensusFixtureInt {
    public static bool Prefix(ref int __result) {
        return true;
    }

    public static void Postfix(ref int __result) {
    }

    public static void Finalizer(Exception __exception, ref int __result) {
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        return instructions;
    }

    [HarmonyPrefix]
    public static void PrefixNone() {
    }

    [HarmonyPostfix]
    public static void PostfixNone() {
    }

    [HarmonyFinalizer]
    public static Exception FinalizerReturns(Exception __exception) {
        return __exception;
    }

    [HarmonyPrefix]
    public static bool PrefixInstance(object __instance) {
        return true;
    }

    [HarmonyPrefix]
    public static bool PrefixArgument(int value) {
        return true;
    }

    [HarmonyPrefix]
    public static bool PrefixPositional(int __0) {
        return true;
    }

    [HarmonyPrefix]
    public static bool PrefixState(ref object __state) {
        return true;
    }

    [HarmonyPrefix]
    public static bool PrefixField(int ___field) {
        return true;
    }

    [HarmonyPrefix]
    public static int PrefixReturnsInt() {
        return 0;
    }

    [HarmonyPrefix]
    public bool PrefixNotStatic() {
        return true;
    }

    [HarmonyPrefix]
    public static bool PrefixResultMismatch(ref long __result) {
        return true;
    }

    [HarmonyPostfix]
    public static int PostfixPassthrough(int __result) {
        return __result;
    }

    [HarmonyPostfix]
    public static void PostfixArgs(object[] __args) {
    }

    [HarmonyPostfix]
    public static void PostfixOriginal(MethodBase __originalMethod) {
    }

    [HarmonyPostfix]
    public static void PostfixRunOriginal(bool __runOriginal) {
    }

    [HarmonyFinalizer]
    public static void FinalizerInstance(Exception __exception, object __instance) {
    }

    [HarmonyFinalizer]
    public static void FinalizerExceptionByRef(ref Exception __exception) {
    }

    [HarmonyFinalizer]
    public static void FinalizerExceptionWrongType(string __exception) {
    }

    [HarmonyFinalizer]
    public static int FinalizerReturnsInt() {
        return 0;
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> TranspilerExtra(IEnumerable<CodeInstruction> instructions, Type extra) {
        return instructions;
    }

    [HarmonyTranspiler]
    public static List<CodeInstruction> TranspilerList(IEnumerable<CodeInstruction> instructions) {
        return null;
    }

    [HarmonyTranspiler]
    public static void TranspilerVoid(IEnumerable<CodeInstruction> instructions) {
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> TranspilerMethodInfo(IEnumerable<CodeInstruction> instructions, MethodInfo original) {
        return instructions;
    }
}

[HarmonyPatch(typeof(CensusFixtureTargets), nameof(CensusFixtureTargets.VoidValue))]
public static class CensusFixtureVoid {
    public static void Postfix(ref int __result) {
    }

    [HarmonyPrefix]
    public static bool PrefixOk() {
        return true;
    }
}

[HarmonyPatch(typeof(CensusFixtureTargets), nameof(CensusFixtureTargets.ObjectValue))]
public static class CensusFixtureObject {
    public static void Postfix(object __result) {
    }

    [HarmonyPostfix]
    public static void PostfixString(string __result) {
    }
}

[HarmonyPatch(typeof(List<string>), nameof(List<string>.Clear))]
public static class CensusFixtureRefInst {
    public static void Postfix() {
    }
}

[HarmonyPatch(typeof(List<int>), nameof(List<int>.Clear))]
public static class CensusFixtureValueInst {
    public static void Postfix() {
    }
}

[HarmonyPatch(typeof(List<>), "Clear")]
public static class CensusFixtureOpen {
    public static void Postfix() {
    }
}
