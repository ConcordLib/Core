using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using CustomAttribute = dnlib.DotNet.CustomAttribute;

namespace Concord.Waylith.ReachCensus {
    public enum Reach {
        Allowed,
        TypeHidden,
        MemberHidden,
    }

    public sealed class PatchMethod {
        public string Assembly;
        public string AssemblyPath;
        public string TypeName;
        public string MethodName;
        public string Role;
        public bool Mintable;
        public bool TypeVisible;
        public string TypeBlocker;
        public string MemberLevel;
        public Reach Verdict;
        public bool IlReachable;
        public bool SelfTarget;
    }

    public sealed class ModuleReport {
        public string Path;
        public string Assembly;
        public Guid Mvid;
        public string LoadError;
        public bool ReferencesHarmony;
        public bool ReferencesManualPatchApi;
        public int AttributedTypes;
        public List<string> InternalsVisibleTo = new List<string>();
        public List<PatchMethod> Patches = new List<PatchMethod>();
    }

    public static class Classifier {
        private const string PatchAttribute = "HarmonyLib.HarmonyPatch";
        private const string PatchAllAttribute = "HarmonyLib.HarmonyPatchAll";

        private static readonly Dictionary<string, string> RoleAttributes = new Dictionary<string, string> {
            ["HarmonyLib.HarmonyPrefix"] = "Prefix",
            ["HarmonyLib.HarmonyPostfix"] = "Postfix",
            ["HarmonyLib.HarmonyTranspiler"] = "Transpiler",
            ["HarmonyLib.HarmonyFinalizer"] = "Finalizer",
            ["HarmonyLib.HarmonyReversePatch"] = "ReversePatch",
        };

        private static readonly HashSet<string> RoleNames = new HashSet<string>(StringComparer.Ordinal) {
            "Prefix",
            "Postfix",
            "Transpiler",
            "Finalizer",
        };

        private static readonly HashSet<string> MintableRoles = new HashSet<string>(StringComparer.Ordinal) {
            "Prefix",
            "Postfix",
            "Finalizer",
        };

        private static readonly string[] ManualPatchApis = {
            "HarmonyLib.HarmonyMethod",
            "HarmonyLib.PatchProcessor",
            "HarmonyLib.PatchClassProcessor",
        };

        public static ModuleReport Scan(string path, string attributeNamespace, bool enclosing = false) {
            ModuleReport report = new ModuleReport { Path = path };
            ModuleDefMD module;
            try {
                module = ModuleDefMD.Load(path);
            } catch (Exception error) {
                report.LoadError = error.GetType().Name + ": " + error.Message;
                report.Assembly = Path.GetFileNameWithoutExtension(path);
                return report;
            }

            using (module) {
                report.Assembly = module.Assembly?.Name ?? module.Name ?? Path.GetFileNameWithoutExtension(path);
                report.Mvid = module.Mvid ?? Guid.Empty;

                HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (TypeRef reference in module.GetTypeRefs()) {
                    referenced.Add(reference.FullName);
                }

                report.ReferencesHarmony = referenced.Any(name => name.StartsWith("HarmonyLib.", StringComparison.Ordinal));
                report.ReferencesManualPatchApi = ManualPatchApis.Any(referenced.Contains);

                if (module.Assembly != null) {
                    foreach (CustomAttribute attribute in module.Assembly.CustomAttributes) {
                        if (attribute.AttributeType?.FullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute") {
                            report.InternalsVisibleTo.Add(ArgumentText(attribute));
                        }
                    }
                }

                string patchAttribute = Rename(PatchAttribute, attributeNamespace);
                string patchAllAttribute = Rename(PatchAllAttribute, attributeNamespace);

                foreach (TypeDef type in module.GetTypes()) {
                    IEnumerable<TypeDef> carriers = enclosing ? EnclosingChain(type) : new[] { type };
                    bool patchClass = carriers
                        .Any(candidate => HasAttribute(candidate, patchAttribute) || HasAttribute(candidate, patchAllAttribute));

                    if (patchClass) {
                        report.AttributedTypes++;
                    }

                    foreach (MethodDef method in type.Methods) {
                        string role = RoleOf(method, patchClass, attributeNamespace);
                        if (role == null) {
                            continue;
                        }

                        PatchMethod described = Describe(report.Assembly, path, type, method, role);
                        described.SelfTarget = TargetsOwnAssembly(type, module, patchAttribute);
                        report.Patches.Add(described);
                    }
                }
            }

            return report;
        }

        private static PatchMethod Describe(string assembly, string path, TypeDef type, MethodDef method, string role) {
            TypeDef blocker = FirstHiddenType(type);
            string level = AssemblyPrivateLevel(method);
            Reach verdict = blocker != null
                ? Reach.TypeHidden
                : level != null ? Reach.MemberHidden : Reach.Allowed;

            return new PatchMethod {
                Assembly = assembly,
                AssemblyPath = path,
                TypeName = type.FullName,
                MethodName = method.Name,
                Role = role,
                Mintable = MintableRoles.Contains(role),
                TypeVisible = blocker == null,
                TypeBlocker = blocker?.FullName,
                MemberLevel = DeclaredLevel(method),
                Verdict = verdict,
                IlReachable = blocker == null && method.IsPublic,
                SelfTarget = false,
            };
        }

        private static string Rename(string fullName, string attributeNamespace) {
            int dot = fullName.LastIndexOf('.');
            return attributeNamespace + fullName.Substring(dot);
        }

        private static string RoleOf(MethodDef method, bool patchClass, string attributeNamespace) {
            foreach (CustomAttribute attribute in method.CustomAttributes) {
                string name = attribute.AttributeType?.FullName;
                if (name == null) {
                    continue;
                }

                foreach (KeyValuePair<string, string> pair in RoleAttributes) {
                    if (name == Rename(pair.Key, attributeNamespace)) {
                        return pair.Value;
                    }
                }
            }

            return patchClass && RoleNames.Contains(method.Name) ? method.Name.String : null;
        }

        private static IEnumerable<TypeDef> EnclosingChain(TypeDef type) {
            for (TypeDef current = type; current != null; current = current.DeclaringType) {
                yield return current;
            }
        }

        private static bool HasAttribute(TypeDef type, string fullName) {
            foreach (CustomAttribute attribute in type.CustomAttributes) {
                if (attribute.AttributeType?.FullName == fullName) {
                    return true;
                }
            }

            return false;
        }

        public static TypeDef FirstHiddenType(TypeDef type) {
            for (TypeDef current = type; current != null; current = current.DeclaringType) {
                bool visible = current.DeclaringType == null ? current.IsPublic : current.IsNestedPublic;
                if (!visible) {
                    return current;
                }
            }

            return null;
        }

        public static string AssemblyPrivateLevel(MethodDef method) {
            if (method.IsPrivate) {
                return "private";
            }

            if (method.IsAssembly) {
                return "internal";
            }

            return method.IsFamilyAndAssembly ? "private protected" : null;
        }

        public static string DeclaredLevel(MethodDef method) {
            if (method.IsPublic) {
                return "public";
            }

            if (method.IsPrivate) {
                return "private";
            }

            if (method.IsAssembly) {
                return "internal";
            }

            if (method.IsFamily) {
                return "protected";
            }

            if (method.IsFamilyOrAssembly) {
                return "protected internal";
            }

            return method.IsFamilyAndAssembly ? "private protected" : "compiler-controlled";
        }

        private static bool TargetsOwnAssembly(TypeDef type, ModuleDef module, string patchAttribute) {
            foreach (TypeDef candidate in EnclosingChain(type)) {
                foreach (CustomAttribute attribute in candidate.CustomAttributes) {
                    if (attribute.AttributeType?.FullName != patchAttribute) {
                        continue;
                    }

                    foreach (CAArgument argument in attribute.ConstructorArguments) {
                        if (argument.Value is TypeSig signature) {
                            string owner = signature.ToTypeDefOrRef()?.DefinitionAssembly?.Name;
                            if (owner != null && owner == module.Assembly?.Name) {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static string ArgumentText(dnlib.DotNet.CustomAttribute attribute) {
            if (attribute.ConstructorArguments.Count == 0) {
                return "<none>";
            }

            object value = attribute.ConstructorArguments[0].Value;
            return value?.ToString() ?? "<null>";
        }
    }
}
