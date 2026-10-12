using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Concord.Host.Adapter;

namespace Concord.Waylith.ReachCensus {
    public static class Program {
        public static int Main(string[] args) {
            if (args.Length == 0) {
                Console.Error.WriteLine("usage: ReachCensus verify <fixture.dll> | mods <modsRoot>... | files <dll>...");
                return 2;
            }

            string mode = args[0];
            string[] rest = args.Skip(1).ToArray();
            string outPath = null;
            string attributeNamespace = "HarmonyLib";
            bool expectNone = false;
            bool enclosing = false;
            List<string> positional = new List<string>();

            for (int i = 0; i < rest.Length; i++) {
                switch (rest[i]) {
                    case "--out":
                        outPath = rest[++i];
                        break;
                    case "--attribute-namespace":
                        attributeNamespace = rest[++i];
                        break;
                    case "--expect-none":
                        expectNone = true;
                        break;
                    case "--enclosing":
                        enclosing = true;
                        break;
                    default:
                        positional.Add(rest[i]);
                        break;
                }
            }

            switch (mode) {
                case "verify":
                    return Verify(positional[0]);
                case "mods":
                    return Census(positional.SelectMany(ExpandMods).ToList(), outPath, attributeNamespace, expectNone, true, enclosing);
                case "files":
                    return Census(positional.Select(path => new ModSelection {
                        ModDir = Path.GetDirectoryName(path),
                        ChosenDir = Path.GetDirectoryName(path),
                        ChosenKey = ".",
                        Assemblies = { path },
                    }).ToList(), outPath, attributeNamespace, expectNone, false, enclosing);
                default:
                    Console.Error.WriteLine("unknown mode '" + mode + "'");
                    return 2;
            }
        }

        private static IEnumerable<ModSelection> ExpandMods(string root) {
            return Population.SelectMods(root);
        }

        private static int Census(
            List<ModSelection> selections,
            string outPath,
            string attributeNamespace,
            bool expectNone,
            bool reportSelection,
            bool enclosing) {
            List<ModuleReport> reports = new List<ModuleReport>();
            HashSet<Guid> seenMvids = new HashSet<Guid>();
            int duplicates = 0;
            int bundled = 0;
            List<string> bundledNames = new List<string>();
            List<string> loadErrors = new List<string>();
            int filesSeen = 0;

            foreach (ModSelection selection in selections) {
                foreach (string path in selection.Assemblies) {
                    filesSeen++;
                    ModuleReport report = Classifier.Scan(path, attributeNamespace, enclosing);
                    if (report.LoadError != null) {
                        loadErrors.Add(report.Assembly + "\t" + path + "\t" + report.LoadError);
                        continue;
                    }

                    if (Population.IsBundledLibrary(report.Assembly)) {
                        bundled++;
                        bundledNames.Add(report.Assembly);
                        continue;
                    }

                    if (report.Mvid != Guid.Empty && !seenMvids.Add(report.Mvid)) {
                        duplicates++;
                        continue;
                    }

                    reports.Add(report);
                }
            }

            List<PatchMethod> patches = reports.SelectMany(report => report.Patches).ToList();

            if (reportSelection) {
                int noAssemblies = selections.Count(selection => selection.Skip == "no assemblies");
                int otherSkips = selections.Count(selection => selection.Skip != null && selection.Skip != "no assemblies");
                Console.WriteLine("mod directories\t" + selections.Count);
                Console.WriteLine("mods with no assemblies\t" + noAssemblies);
                Console.WriteLine("mods skipped for another reason\t" + otherSkips);
                Console.WriteLine("version folders rejected\t" + selections.Sum(selection => selection.RejectedKeys.Count));
                foreach (IGrouping<string, ModSelection> group in selections
                    .Where(selection => selection.ChosenKey != null)
                    .GroupBy(selection => selection.ChosenKey)
                    .OrderByDescending(group => group.Count())) {
                    Console.WriteLine("chosen folder\t" + group.Key + "\t" + group.Count());
                }
            }

            Console.WriteLine("dll files seen\t" + filesSeen);
            Console.WriteLine("dlls that would not load\t" + loadErrors.Count);
            Console.WriteLine("dlls dropped as bundled libraries\t" + bundled);
            Console.WriteLine("dlls dropped as duplicate mvid\t" + duplicates);
            Console.WriteLine("assemblies scanned\t" + reports.Count);
            Console.WriteLine("assemblies referencing HarmonyLib\t" + reports.Count(report => report.ReferencesHarmony));
            Console.WriteLine("assemblies with a HarmonyPatch-attributed type\t" + reports.Count(report => report.AttributedTypes > 0));
            Console.WriteLine("HarmonyPatch-attributed types\t" + reports.Sum(report => report.AttributedTypes));
            Console.WriteLine("assemblies carrying patch methods\t" + reports.Count(report => report.Patches.Count > 0));
            Console.WriteLine("assemblies referencing manual patch api\t" + reports.Count(report => report.ReferencesManualPatchApi));
            Console.WriteLine("assemblies with manual patch api and no patch methods\t"
                + reports.Count(report => report.ReferencesManualPatchApi && report.Patches.Count == 0));
            Console.WriteLine("assemblies granting InternalsVisibleTo\t" + reports.Count(report => report.InternalsVisibleTo.Count > 0));
            foreach (ModuleReport report in reports.Where(report => report.InternalsVisibleTo.Count > 0)) {
                Console.WriteLine("ivt grant\t" + report.Assembly + "\t" + string.Join(";", report.InternalsVisibleTo));
            }

            Console.WriteLine("patch methods\t" + patches.Count);
            foreach (IGrouping<string, PatchMethod> group in patches.GroupBy(patch => patch.Role).OrderBy(group => group.Key)) {
                Console.WriteLine("role\t" + group.Key + "\t" + group.Count());
            }

            Report("all roles", patches);
            Report("mintable roles only", patches.Where(patch => patch.Mintable).ToList());
            ReportClasses("all roles", patches);
            ReportClasses("mintable roles only", patches.Where(patch => patch.Mintable).ToList());

            Console.WriteLine("self-targeting patch methods\t" + patches.Count(patch => patch.SelfTarget));
            foreach (IGrouping<string, PatchMethod> group in patches
                .GroupBy(patch => patch.MemberLevel)
                .OrderByDescending(group => group.Count())) {
                Console.WriteLine("declared level\t" + group.Key + "\t" + group.Count());
            }

            Console.WriteLine("mintable patch methods MemberReach allows but IL cannot reach\t"
                + patches.Count(patch => patch.Mintable && patch.Verdict == Reach.Allowed && !patch.IlReachable));
            foreach (PatchMethod gap in patches.Where(patch => patch.Verdict == Reach.Allowed && !patch.IlReachable)) {
                Console.WriteLine("reach gap\t" + gap.Assembly + "\t" + gap.TypeName + "." + gap.MethodName + "\t" + gap.MemberLevel);
            }

            if (outPath != null) {
                WriteTsv(outPath, patches);
                WriteLines(Path.ChangeExtension(outPath, ".load-errors.tsv"), loadErrors);
                WriteLines(Path.ChangeExtension(outPath, ".bundled.txt"), bundledNames.Distinct().OrderBy(name => name, StringComparer.Ordinal));
                Console.WriteLine("wrote\t" + outPath);
            }

            if (expectNone) {
                Console.WriteLine(patches.Count == 0 ? "expect-none\tPASS" : "expect-none\tFAIL");
                return patches.Count == 0 ? 0 : 1;
            }

            return patches.Count == 0 ? 2 : 0;
        }

        private static void Report(string label, List<PatchMethod> patches) {
            int clean = patches.Count(patch => patch.TypeVisible && patch.MemberLevel == "public");
            int typeOnly = patches.Count(patch => !patch.TypeVisible && patch.MemberLevel == "public");
            int memberOnly = patches.Count(patch => patch.TypeVisible && patch.MemberLevel != "public");
            int both = patches.Count(patch => !patch.TypeVisible && patch.MemberLevel != "public");
            int refused = patches.Count(patch => patch.Verdict != Reach.Allowed);

            Console.WriteLine("-- " + label);
            Console.WriteLine("  total\t" + patches.Count);
            Console.WriteLine("  clean (type visible, member public)\t" + clean);
            Console.WriteLine("  type hidden only\t" + typeOnly);
            Console.WriteLine("  member not public only\t" + memberOnly);
            Console.WriteLine("  both\t" + both);
            Console.WriteLine("  MemberReach refuses\t" + refused);
            Console.WriteLine("  MemberReach refuses for the type\t" + patches.Count(patch => patch.Verdict == Reach.TypeHidden));
            Console.WriteLine("  MemberReach refuses for the member\t" + patches.Count(patch => patch.Verdict == Reach.MemberHidden));
            Console.WriteLine("  MemberReach allows\t" + patches.Count(patch => patch.Verdict == Reach.Allowed));
            Console.WriteLine("  refused fraction\t" + Fraction(refused, patches.Count));
        }

        private static void ReportClasses(string label, List<PatchMethod> patches) {
            List<bool> classTypeVisible = patches
                .GroupBy(patch => patch.AssemblyPath + "\t" + patch.TypeName)
                .Select(group => group.First().TypeVisible)
                .ToList();
            int hidden = classTypeVisible.Count(visible => !visible);

            Console.WriteLine("-- " + label + " classes");
            Console.WriteLine("  total\t" + classTypeVisible.Count);
            Console.WriteLine("  type visible\t" + (classTypeVisible.Count - hidden));
            Console.WriteLine("  type hidden: refuses every patch method inside\t" + hidden);
            Console.WriteLine("  refused fraction\t" + Fraction(hidden, classTypeVisible.Count));
        }

        private static string Fraction(int part, int whole) {
            return whole == 0
                ? "n/a"
                : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static void WriteTsv(string path, List<PatchMethod> patches) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            StringBuilder text = new StringBuilder();
            text.AppendLine("assembly\ttype\tmethod\trole\tmintable\ttypeVisible\ttypeBlocker\tmemberLevel\tverdict\tilReachable\tselfTarget\tpath");
            foreach (PatchMethod patch in patches) {
                text.AppendLine(string.Join("\t",
                    patch.Assembly,
                    patch.TypeName,
                    patch.MethodName,
                    patch.Role,
                    patch.Mintable,
                    patch.TypeVisible,
                    patch.TypeBlocker ?? "",
                    patch.MemberLevel,
                    patch.Verdict,
                    patch.IlReachable,
                    patch.SelfTarget,
                    patch.AssemblyPath));
            }

            File.WriteAllText(path, text.ToString());
        }

        private static void WriteLines(string path, IEnumerable<string> lines) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllLines(path, lines);
        }

        private static int Verify(string fixturePath) {
            ModuleReport metadata = Classifier.Scan(fixturePath, "HarmonyLib");
            if (metadata.LoadError != null) {
                Console.Error.WriteLine("fixture would not load: " + metadata.LoadError);
                return 2;
            }

            Assembly fixture = Assembly.LoadFrom(Path.GetFullPath(fixturePath));
            Module consumer = typeof(Program).Module;
            Console.WriteLine("consumer module\t" + consumer.Name);
            Console.WriteLine("fixture patch methods found by metadata scan\t" + metadata.Patches.Count);

            int agreed = 0;
            int disagreed = 0;
            int unresolved = 0;

            Console.WriteLine("type\tmethod\trole\tcensus verdict\tExplain says\tagree");
            foreach (PatchMethod patch in metadata.Patches.OrderBy(patch => patch.TypeName, StringComparer.Ordinal)
                .ThenBy(patch => patch.MethodName, StringComparer.Ordinal)) {
                Type declaring = fixture.GetType(patch.TypeName.Replace('/', '+'), false);
                MethodInfo method = declaring?.GetMethod(
                    patch.MethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method == null) {
                    unresolved++;
                    Console.WriteLine(patch.TypeName + "\t" + patch.MethodName + "\t" + patch.Role + "\t" + patch.Verdict + "\tUNRESOLVED\tno");
                    continue;
                }

                string explanation = MemberReach.Explain(method, consumer);
                Reach actual = explanation == null
                    ? Reach.Allowed
                    : explanation.Contains("is not visible outside") ? Reach.TypeHidden : Reach.MemberHidden;
                bool agree = actual == patch.Verdict;
                if (agree) {
                    agreed++;
                } else {
                    disagreed++;
                }

                Console.WriteLine(patch.TypeName + "\t" + patch.MethodName + "\t" + patch.Role + "\t" + patch.Verdict
                    + "\t" + actual + "\t" + (agree ? "yes" : "NO"));
            }

            Console.WriteLine("agreed\t" + agreed);
            Console.WriteLine("disagreed\t" + disagreed);
            Console.WriteLine("unresolved\t" + unresolved);

            int distinct = metadata.Patches.Select(patch => patch.Verdict).Distinct().Count();
            Console.WriteLine("distinct verdicts produced by the fixture\t" + distinct);
            bool discriminates = distinct >= 3;
            Console.WriteLine("negative control (fixture produces every verdict)\t" + (discriminates ? "PASS" : "FAIL"));

            int gaps = metadata.Patches.Count(patch => patch.Verdict == Reach.Allowed && !patch.IlReachable);
            Console.WriteLine("MemberReach allows but IL cannot reach\t" + gaps);
            foreach (PatchMethod gap in metadata.Patches.Where(patch => patch.Verdict == Reach.Allowed && !patch.IlReachable)) {
                Console.WriteLine("reach gap\t" + gap.TypeName + "." + gap.MethodName + "\t" + gap.MemberLevel);
            }

            List<PatchMethod> mintable = metadata.Patches.Where(patch => patch.Mintable).ToList();
            int mintableRefused = mintable.Count(patch => patch.Verdict != Reach.Allowed);
            List<bool> classTypeVisible = mintable
                .GroupBy(patch => patch.TypeName)
                .Select(group => group.First().TypeVisible)
                .ToList();
            int classesHidden = classTypeVisible.Count(visible => !visible);
            string classFraction = Fraction(classesHidden, classTypeVisible.Count);
            string methodFraction = Fraction(mintableRefused, mintable.Count);
            Console.WriteLine("fixture patch classes, mintable roles\t" + classTypeVisible.Count);
            Console.WriteLine("fixture patch classes type-hidden, mintable roles\t" + classesHidden);
            Console.WriteLine("fixture patch classes refused fraction, mintable roles\t" + classFraction);
            Console.WriteLine("fixture patch methods refused fraction, mintable roles\t" + methodFraction);
            bool classesDiffer = classTypeVisible.Count > 0 && classFraction != methodFraction;
            Console.WriteLine("negative control (class fraction differs from method fraction)\t" + (classesDiffer ? "PASS" : "FAIL"));

            return disagreed == 0 && unresolved == 0 && discriminates ? 0 : 1;
        }
    }
}
