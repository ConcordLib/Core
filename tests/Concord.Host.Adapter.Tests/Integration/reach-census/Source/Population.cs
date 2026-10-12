using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Concord.Waylith.ReachCensus {
    public sealed class ModSelection {
        public string ModDir;
        public string ChosenDir;
        public string ChosenKey;
        public string ScanRoot;
        public List<string> RejectedKeys = new List<string>();
        public List<string> Assemblies = new List<string>();
        public string Skip;
    }

    public static class Population {
        private static readonly string[] BundledLibraries = {
            "0harmony",
            "lib.harmony",
            "harmonyx",
            "mono.cecil",
            "monomod",
            "monomod.runtimedetour",
            "monomod.utils",
            "mono.cecil.pdb",
            "mono.cecil.mdb",
            "mono.cecil.rocks",
            "newtonsoft.json",
            "system.runtime.compilerservices.unsafe",
            "mscorlib",
            "netstandard",
            "assembly-csharp",
            "assembly-csharp-firstpass",
        };

        public static bool IsBundledLibrary(string assemblyName) {
            return assemblyName != null && BundledLibraries.Contains(assemblyName.ToLowerInvariant());
        }

        public static List<ModSelection> SelectMods(string modsRoot) {
            List<ModSelection> selections = new List<ModSelection>();
            foreach (string modDir in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.Ordinal)) {
                selections.Add(SelectOne(modDir));
            }

            return selections;
        }

        public static ModSelection SelectOne(string modDir) {
            ModSelection selection = new ModSelection { ModDir = modDir };

            List<string> candidates;
            try {
                candidates = Directory
                    .GetDirectories(modDir, "Assemblies", SearchOption.AllDirectories)
                    .Where(IsEligible)
                    .ToList();
            } catch (Exception error) {
                selection.Skip = "enumeration failed: " + error.GetType().Name;
                return selection;
            }

            if (candidates.Count == 0) {
                selection.Skip = Directory.EnumerateFiles(modDir, "*.dll", SearchOption.AllDirectories).Any()
                    ? "dlls present but no Assemblies directory"
                    : "no assemblies";
                return selection;
            }

            List<Tuple<Version, string, string>> ranked = candidates
                .Select(dir => Tuple.Create(VersionOf(dir, modDir), KeyOf(dir, modDir), dir))
                .OrderByDescending(entry => entry.Item1)
                .ThenBy(entry => entry.Item3, StringComparer.Ordinal)
                .ToList();

            selection.ChosenDir = ranked[0].Item3;
            selection.ChosenKey = ranked[0].Item2;
            foreach (Tuple<Version, string, string> rejected in ranked.Skip(1)) {
                selection.RejectedKeys.Add(rejected.Item2);
            }

            string parent = Path.GetDirectoryName(selection.ChosenDir);
            string scanRoot = parent != null && Path.GetFullPath(parent) != Path.GetFullPath(modDir)
                ? parent
                : selection.ChosenDir;
            selection.ScanRoot = scanRoot;

            selection.Assemblies = Directory
                .GetFiles(scanRoot, "*.dll", SearchOption.AllDirectories)
                .Where(path => IsEligible(Path.GetDirectoryName(path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            if (selection.Assemblies.Count == 0) {
                selection.Skip = "chosen Assemblies directory is empty";
            }

            return selection;
        }

        private static bool IsEligible(string dir) {
            string normalized = dir.Replace('\\', '/');
            return !normalized.Contains("/obj/")
                && !normalized.Contains("/bin/")
                && !normalized.Contains("/Source/")
                && !normalized.Contains("/.git/");
        }

        private static string KeyOf(string dir, string modDir) {
            string relative = Path.GetRelativePath(modDir, dir).Replace('\\', '/');
            return relative;
        }

        private static Version VersionOf(string dir, string modDir) {
            string relative = KeyOf(dir, modDir);
            if (relative == "Assemblies") {
                return new Version(0, 1);
            }

            string parent = relative.Substring(0, relative.Length - "Assemblies".Length).TrimEnd('/');
            if (parent.Contains('/')) {
                return new Version(0, 0);
            }

            if (string.Equals(parent, "Current", StringComparison.OrdinalIgnoreCase)) {
                return new Version(999, 0);
            }

            string text = parent.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? parent.Substring(1) : parent;
            return Version.TryParse(text, out Version parsed) ? parsed : new Version(0, 0);
        }
    }
}
